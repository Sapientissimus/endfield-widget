using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace EndfieldWidget {
  public sealed class CalendarSourceState {
    public string Source {get;set;}
    public long Published {get;set;}
    public DateTimeOffset LastSuccess {get;set;}
    public DateTimeOffset LastAttempt {get;set;}
    public string Problem {get;set;}
    public bool Undated {get;set;}
    public List<GameEvent> Events {get;set;}
    public Schedule Maintenance {get;set;}
    public List<string> WithdrawnNames {get;set;}
  }
  public sealed class CalendarFeedState {
    public const int CurrentSchema=1;
    public string PublicationCode {get;set;}
    public int Schema {get;set;}
    public DateTimeOffset Checked {get;set;}
    public bool Partial {get;set;}
    public int SuccessCount {get;set;}
    public List<CalendarSourceState> Sources {get;set;}
    public List<GameEvent> Events {get;set;}
    public List<Schedule> Schedules {get;set;}
    public static CalendarFeedState FromSources(IEnumerable<CalendarSourceState> sources,DateTimeOffset now,int successes,bool failed) {
      var list=sources.Where(s=>s!=null && CalendarData.Official(s.Source)).ToList();
      var withdrawn=list.SelectMany(s=>(s.WithdrawnNames??new List<string>()).Select(n=>new{ Name=CalendarData.Normalize(n),Published=s.Published})).ToList();
      var events=list.SelectMany(s=>(s.Events??new List<GameEvent>()).Where(e=>e!=null && e.Source==s.Source))
        .Where(e=>!withdrawn.Any(w=>w.Name==CalendarData.Normalize(e.Name) && w.Published>=e.Published && w.Published-e.Published<=86400L*45));
      return new CalendarFeedState{PublicationCode=Publications.Current.Code,Schema=CurrentSchema,Checked=now,Partial=failed || list.Any(s=>!string.IsNullOrEmpty(s.Problem)),SuccessCount=successes,Sources=list,Events=GameEventDates.Latest(events),Schedules=list.Where(s=>s.Maintenance!=null && s.Maintenance.Source==s.Source).Select(s=>s.Maintenance).ToList()};
    }
    public static CalendarFeedState Seed(BannerState banners) {
      var events=banners==null || banners.GameEvents==null?new List<GameEvent>():banners.GameEvents;
      return FromSources(events.Where(e=>e!=null && CalendarData.Official(e.Source)).GroupBy(e=>e.Source).Select(g=>new CalendarSourceState{Source=g.Key,Published=g.Max(e=>e.Published),Events=g.ToList(),LastSuccess=banners.Checked}),DateTimeOffset.MinValue,0,false);
    }
  }
  static class CalendarFeedStore {
    static readonly object gate=new object();
    static string Filename {get{return Publications.Cache("calendar");}}
    internal static bool Valid(CalendarFeedState value) {
      if(value==null || value.Schema!=CalendarFeedState.CurrentSchema || value.Sources==null || value.Sources.Count>96)return false;
      return value.Sources.All(s=>s!=null && CalendarData.Official(s.Source) && (s.Events==null || s.Events.Count<=128 && s.Events.All(e=>e!=null && e.Source==s.Source && !string.IsNullOrWhiteSpace(e.Name) && e.Name.Length<=160)) && (s.Maintenance==null || s.Maintenance.Source==s.Source && s.Maintenance.End>s.Maintenance.Start && s.Maintenance.End-s.Maintenance.Start<=TimeSpan.FromDays(3)));
    }
    static CalendarFeedState Read(string path) {
      try{if(new FileInfo(path).Length>4000000)return null;var state=new JavaScriptSerializer{MaxJsonLength=4000000}.Deserialize<CalendarFeedState>(File.ReadAllText(path,Encoding.UTF8));if(!Valid(state))return null;var restored=CalendarFeedState.FromSources(state.Sources,state.Checked,0,state.Partial);restored.PublicationCode=state.PublicationCode??Publications.Current.Code;return restored;}catch{return null;}
    }
    public static CalendarFeedState Load(BannerState fallback) {
      lock(gate){string path=Path.Combine(Storage.DirectoryPath,Filename);return Read(path)??Read(path+".bak")??CalendarFeedState.Seed(fallback);}
    }
    public static void Save(CalendarFeedState state) {
      if(!Valid(state))throw new FormatException("Invalid public calendar snapshot.");
      lock(gate) {
        Directory.CreateDirectory(Storage.DirectoryPath);string path=Path.Combine(Storage.DirectoryPath,Publications.Cache("calendar",state.PublicationCode??Publications.Current.Code));var old=Read(path);
        Storage.AtomicWrite(path+".bak",Json.Encode(old??state));Storage.AtomicWrite(path,Json.Encode(state));
      }
    }
  }
  partial class BannerClient {
    static string CalendarProblem(Exception error){var bulletin=error as BulletinException;return bulletin!=null?bulletin.Problem:error is OperationCanceledException?"timeout":error is HttpRequestException?"network":error is FormatException?"format":"source";}
    public async Task<CalendarFeedState> FetchCalendar(CalendarFeedState previous) {return await InPublication(()=>FetchCalendarCore(previous)).ConfigureAwait(false);}
    public async Task<CalendarFeedState> FetchCalendarCore(CalendarFeedState previous) {
      var now=DateTimeOffset.UtcNow;previous=previous??CalendarFeedState.Seed(null);
      List<Dictionary<string,object>> archive;
      try{archive=await Archive().ConfigureAwait(false);}catch(Exception error) {
        var retained=previous.Sources??new List<CalendarSourceState>();var copied=retained.Select(s=>CopyCalendarSource(s,now,CalendarProblem(error))).ToList();
        return CalendarFeedState.FromSources(copied,now,0,true);
      }
      var candidates=archive.Where(a=>IsOverviewTitle(Json.Str(a,"title"))).Take(3)
        .Concat(archive.Where(a=>GameEventDates.Eligible(Json.Str(a,"title"))).Take(12))
        .Concat(archive.Where(a=>ArticleLanguage.MaintenanceTitle(Json.Str(a,"title"))).Take(5)).ToList();
      foreach(var old in previous.Sources??new List<CalendarSourceState>())if((old.Events??new List<GameEvent>()).Any(e=>e!=null && (e.End(false)>now || e.End(true)>now))) {
        string cid=old.Source.Substring(old.Source.LastIndexOf('/')+1);var article=archive.FirstOrDefault(a=>Json.Str(a,"cid")==cid);if(article!=null)candidates.Add(article);
      }
      candidates=candidates.GroupBy(a=>Json.Str(a,"cid")).Select(g=>g.First()).Take(64).ToList();
      using(var throttle=new SemaphoreSlim(3)) {
        var tasks=candidates.Select(async article=>{
          await throttle.WaitAsync().ConfigureAwait(false);
          try{return await FetchCalendarSource(article,previous,now).ConfigureAwait(false);}finally{throttle.Release();}
        }).ToArray();
        var sources=await Task.WhenAll(tasks).ConfigureAwait(false);
        return CalendarFeedState.FromSources(sources,now,sources.Count(s=>s.LastSuccess==now),sources.Length==0);
      }
    }
    static CalendarSourceState CopyCalendarSource(CalendarSourceState value,DateTimeOffset now,string problem) {
      return new CalendarSourceState{Source=value.Source,Published=value.Published,LastSuccess=value.LastSuccess,LastAttempt=now,Problem=problem,Undated=value.Undated,Events=value.Events,Maintenance=value.Maintenance,WithdrawnNames=value.WithdrawnNames};
    }
    async Task<CalendarSourceState> FetchCalendarSource(Dictionary<string,object> article,CalendarFeedState previous,DateTimeOffset now) {
      string cid=Json.Str(article,"cid"),title=Json.Str(article,"title"),url=NewsClient.NewsUrl+"/"+cid;long published=Json.Num(article,"displayTime");
      var old=(previous.Sources??new List<CalendarSourceState>()).FirstOrDefault(s=>s.Source==url)??new CalendarSourceState{Source=url,Events=new List<GameEvent>()};
      var state=CopyCalendarSource(old,now,null);state.Published=published;state.Undated=false;
      try {
        var response=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);string html=Json.Str(response,"data","data"),text=NewsClient.StructuredText(html);title=FetchedTitle(response,article);
        if(string.IsNullOrWhiteSpace(text))throw new FormatException("Empty official article.");
        if(IsOverviewTitle(title) || GameEventDates.Eligible(title)) {
          try {
            var events=GameEventDates.Parse(html,title,cid,published);state.Events=events;
            var names=GameEventDates.DeclaredNames(html,title);state.WithdrawnNames=names.Where(n=>!events.Any(e=>CalendarData.Normalize(e.Name)==CalendarData.Normalize(n))).ToList();state.Undated=state.WithdrawnNames.Count>0;
          }catch(FormatException){state.Problem="format";}
        }
        if(IsOverviewTitle(title) || ArticleLanguage.MaintenanceTitle(title)) {
          try{state.Maintenance=NewsClient.ParseSchedule(html,title,url,DateTimeOffset.FromUnixTimeSeconds(published));state.Maintenance.PublishedUnix=published;}
          catch(FormatException){if(Regex.IsMatch(text,@"Server[^\r\n]*UTC",RegexOptions.IgnoreCase))state.Problem="format";else{state.Maintenance=null;state.Undated=true;}}
        }
        if(state.Problem==null)state.LastSuccess=now;
      }catch(Exception error){state.Problem=CalendarProblem(error);}
      return state;
    }
  }
}
