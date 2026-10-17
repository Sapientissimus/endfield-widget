using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EndfieldWidget {
  public sealed class CalendarDetailRecord {
    public string Key {get;set;}
    public string PublicationCode {get;set;}
    public string Source {get;set;}
    public string Description {get;set;}
    public string Participation {get;set;}
    public DateTimeOffset Checked {get;set;}
  }
  public sealed class CalendarDetailsFile {
    public int Schema {get;set;}
    public List<CalendarDetailRecord> Items {get;set;}
  }
  static class CalendarDetailsData {
    public static string Key(CalendarEntry entry){return entry.Source+"|"+entry.Kind+"|"+CalendarData.Normalize(entry.Name??entry.Title)+"|"+entry.At.UtcTicks+"|"+(entry.PeriodStart.HasValue?entry.PeriodStart.Value.UtcTicks:0);}
    static string Clean(string value){return Regex.Replace(value??"",@"[\t ]+"," ").Trim(' ','\r','\n','·','•');}
    static string Limit(string text,int length){text=Clean(text);if(text.Length<=length)return text;int stop=text.LastIndexOf(' ',length-1,length/2);return text.Substring(0,stop>0?stop:length-1).TrimEnd()+"…";}
    static string Block(string text,string title,CalendarEntry entry) {
      if(BannerClient.IsOverviewTitle(title)) {
        var headings=Regex.Matches(text,@"(?m)^[ \t]*\d+[.)][ \t]*(?<title>[^\r\n]*\[[^\]\r\n]+\][^\r\n]*)\r?$");
        for(int i=0;i<headings.Count;i++) {
          int from=headings[i].Index+headings[i].Length,to=i+1<headings.Count?headings[i+1].Index:text.Length;string header=headings[i].Groups["title"].Value,body=text.Substring(from,to-from);
          bool matching=GameEventDates.Eligible(header) && CalendarData.Normalize(GameEventDates.Name(header))==CalendarData.Normalize(entry.Name);
          if(entry.Kind==CalendarKind.Debut || entry.Kind==CalendarKind.BannerEnd){var featured=BannerClient.FeaturedName(body);matching=BannerClient.IsDebutTitle(header) && featured!=null && CalendarData.Normalize(featured)==CalendarData.Normalize(entry.Name);}
          if(!matching)continue;
          var end=Regex.Match(body,@"(?m)^[ \t]*(?:■[ \t]*(?:New |Event &|System |Story |Other |Optim)|▼\s*//\s*(?:Optim|Bug))",RegexOptions.IgnoreCase);
          return end.Success?body.Substring(0,end.Index):body;
        }
        if(entry.Kind!=CalendarKind.Maintenance)throw new FormatException("The requested event section is unavailable.");
        var content=Regex.Match(text,@"(?m)^[ \t]*(?:■[ \t]*New |▼\s*//[ \t]*(?:New |Content|Optimizations|Bug Fixes))",RegexOptions.IgnoreCase);
        return content.Success?text.Substring(0,content.Index):text;
      }
      if(entry.Kind==CalendarKind.EventEnd && CalendarData.Normalize(GameEventDates.Name(title))!=CalendarData.Normalize(entry.Name))throw new FormatException("The announcement describes a different event.");
      if(entry.Kind==CalendarKind.Debut || entry.Kind==CalendarKind.BannerEnd) {
        var heading=Regex.Match(text,@"(?m)^[ \t]*▼\s*//[^\r\n]*Chartered Headhunting[ \t]*\r?$",RegexOptions.IgnoreCase);
        if(!heading.Success || !CalendarData.Normalize(text).Contains("["+CalendarData.Normalize(entry.Name)+"]"))throw new FormatException("The requested operator section is unavailable.");
        string block=text.Substring(heading.Index+heading.Length);var end=Regex.Match(block,@"(?m)^[ \t]*▼\s*//",RegexOptions.IgnoreCase);
        return end.Success?block.Substring(0,end.Index):block;
      }
      return text;
    }
    public static CalendarDetailRecord Parse(string html,string title,CalendarEntry entry,DateTimeOffset now) {
      if(!CalendarData.Official(entry.Source))throw new FormatException("An official source is required.");
      if(string.IsNullOrWhiteSpace(html) || html.Length>2000000)throw new FormatException("The announcement is empty or too large.");
      html=Regex.Replace(html,@"<(?:style|noscript)\b[^>]*>.*?</(?:style|noscript)>","",RegexOptions.Singleline|RegexOptions.IgnoreCase);
      string body=Block(ArticleLanguage.Text(NewsClient.PlainText(html),false),title,entry);
      var description=new List<string>();var requirements=new List<string>();var fallback=new List<string>();string section="";
      foreach(string raw in body.Split('\n')) {
        string line=Clean(raw);if(line.Length==0)continue;
        var label=Regex.Match(line,@"^(?:▼\s*//[ \t]*)?(?<label>(?:Event|Headhunting)\s+(?:Details|Description|Rules|Time|Period|Duration|Availability|Content Update Time)|Participation(?:\s+(?:Criteria|Requirements))?|Requirements|Eligibility|Availability|Description|Details|Gameplay|How to Participate)\s*[:：]\s*(?<value>.*)$",RegexOptions.IgnoreCase);
        if(label.Success) {
          string name=label.Groups["label"].Value;section=Regex.IsMatch(name,"Participation|Requirements|Eligibility|How to Participate",RegexOptions.IgnoreCase)?"requirements":Regex.IsMatch(name,"Details|Description|Rules|Gameplay",RegexOptions.IgnoreCase)?"description":"time";
          line=Clean(label.Groups["value"].Value);
        }else if(Regex.IsMatch(raw.TrimStart(),@"^[·•■▼]")) {
          if(section!="description")section="";
          if(Regex.IsMatch(line,@"^[^:：]{1,70}[:：]"))section="skip";
        }
        if(line.Length==0 || Regex.IsMatch(line,@"^(?:Dear Endministrator|Уважаемый Эндминистратор|▼|■|https?://)",RegexOptions.IgnoreCase))continue;
        if(section=="description")description.Add(line);
        else if(section=="requirements")requirements.Add(line);
        else if(section=="" && line.Length>=30 && !Regex.IsMatch(line,@"UTC|server time|\d{1,2}:\d{2}",RegexOptions.IgnoreCase))fallback.Add(line);
      }
      if(description.Count==0)description.AddRange(fallback.Take(2));
      return new CalendarDetailRecord{PublicationCode=Publications.Current.Code,Key=Key(entry),Source=entry.Source,Description=Limit(string.Join("\n\n",description),1600),Participation=Limit(string.Join("\n",requirements),1000),Checked=now};
    }
  }
  static class CalendarDetailsStore {
    static readonly object gate=new object();
    static string Filename {get{return Path.Combine(Storage.DirectoryPath,Publications.Cache("calendar-details"));}}
    static bool Valid(CalendarDetailRecord item){return item!=null && CalendarData.Official(item.Source) && item.Key!=null && item.Key.StartsWith(item.Source+"|",StringComparison.Ordinal) && item.Key.Length<600 && (item.Description??"").Length<=1600 && (item.Participation??"").Length<=1000;}
    static CalendarDetailsFile Read(string path) {
      try{if(new FileInfo(path).Length>1000000)return null;var file=Json.Decode(File.ReadAllText(path));var parsed=new System.Web.Script.Serialization.JavaScriptSerializer().ConvertToType<CalendarDetailsFile>(file);return parsed!=null && parsed.Schema==1 && parsed.Items!=null && parsed.Items.Count<=128 && parsed.Items.All(Valid)?parsed:null;}catch{return null;}
    }
    public static Dictionary<string,CalendarDetailRecord> Load() {
      lock(gate){var value=Read(Filename)??Read(Filename+".bak");return value==null?new Dictionary<string,CalendarDetailRecord>():value.Items.GroupBy(i=>i.Key).ToDictionary(g=>g.Key,g=>g.OrderByDescending(i=>i.Checked).First());}
    }
    public static void Save(CalendarDetailRecord record) {
      if(!Valid(record))throw new FormatException("Invalid public calendar detail.");
      lock(gate) {
        string path=Path.Combine(Storage.DirectoryPath,Publications.Cache("calendar-details",record.PublicationCode??Publications.Current.Code));var old=Read(path)??Read(path+".bak");var items=old==null?new List<CalendarDetailRecord>():old.Items.Where(i=>i.Key!=record.Key).ToList();items.Insert(0,record);
        var value=new CalendarDetailsFile{Schema=1,Items=items.Take(128).ToList()};Directory.CreateDirectory(Storage.DirectoryPath);
        Storage.AtomicWrite(path+".bak",Json.Encode(old??value));Storage.AtomicWrite(path,Json.Encode(value));
      }
    }
  }
  partial class BannerClient {
    internal async Task<CalendarDetailRecord> FetchCalendarDetail(CalendarEntry entry) {return await InPublication(()=>FetchCalendarDetailCore(entry)).ConfigureAwait(false);}
    internal async Task<CalendarDetailRecord> FetchCalendarDetailCore(CalendarEntry entry) {
      if(!CalendarData.Official(entry.Source))throw new FormatException("An official source is required.");
      string cid=entry.Source.Substring(entry.Source.LastIndexOf('/')+1);var value=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);
      var result=CalendarDetailsData.Parse(Json.Str(value,"data","data"),Json.Str(value,"data","title"),entry,DateTimeOffset.UtcNow);
      result.Description=LocalizedArticles.TranslateExcerpt(value,result.Description,"\n\n");result.Participation=LocalizedArticles.TranslateExcerpt(value,result.Participation,"\n");return result;
    }
  }
}
