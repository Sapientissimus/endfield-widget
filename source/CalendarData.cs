using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  enum CalendarKind {Unknown,Maintenance,Debut,BannerEnd,EventEnd,EventStart,Milestone,RerunStart,RerunEnd,WeaponStart,WeaponEnd}
  sealed class CalendarEntry {
    public string Id,Title,Name,Source;
    public CalendarKind Kind;
    public DateTimeOffset At;
    public DateTimeOffset? End;
    public DateTimeOffset? PeriodStart,PeriodEnd;
    public long Published;
    public DateTimeOffset? RangeEnd {get{return End??(CalendarData.IsEnd(Kind)?null:PeriodEnd);}}
  }
  static class CalendarData {
    public const int AllKinds=(1<<(int)CalendarKind.WeaponEnd)-1;
    public static bool Official(string source){return source!=null && Publications.Official(source);}
    public static string Normalize(string text){return Regex.Replace((text??"").Normalize(NormalizationForm.FormKC).Replace('\u2019','\''),@"\s+"," ").Trim().ToLowerInvariant();}
    public static string Label(CalendarKind kind) {
      switch(kind) {
        case CalendarKind.Maintenance:return L.T("Maintenance");
        case CalendarKind.Debut:return L.T("Debut");
        case CalendarKind.BannerEnd:return L.T("Banner ends");
        case CalendarKind.EventEnd:return L.T("Event ends");
        case CalendarKind.EventStart:return L.T("Event starts");
        case CalendarKind.Milestone:return L.T("Milestone");
        case CalendarKind.RerunStart:return L.T("Rerun")+" · "+L.T("START");
        case CalendarKind.RerunEnd:return L.T("Rerun")+" · "+L.T("END");
        case CalendarKind.WeaponStart:return L.T("Weapon banner")+" · "+L.T("START");
        case CalendarKind.WeaponEnd:return L.T("Weapon banner")+" · "+L.T("END");
        default:return L.T("Event");
      }
    }
    public static bool IsEnd(CalendarKind kind){return kind==CalendarKind.BannerEnd || kind==CalendarKind.EventEnd || kind==CalendarKind.RerunEnd || kind==CalendarKind.WeaponEnd;}
    public static int Mask(CalendarKind kind){return kind==CalendarKind.Unknown?AllKinds:1<<((int)kind-1);}
    static DateTimeOffset? Unix(long value){try{return value>0?(DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(value):null;}catch(ArgumentOutOfRangeException){return null;}}
    static CalendarEntry Period(CalendarEntry entry,DateTimeOffset? start,DateTimeOffset? end){if(entry!=null && start.HasValue && end.HasValue && end>start){entry.PeriodStart=start;entry.PeriodEnd=end;}return entry;}
    static CalendarEntry Entry(CalendarKind kind,string id,string name,string source,DateTimeOffset at,DateTimeOffset? end,long published) {
      if(!Official(source) || at==DateTimeOffset.MinValue || string.IsNullOrWhiteSpace(name) || name.Length>200)return null;
      if(end.HasValue && (end<=at || end-at>TimeSpan.FromDays(30)))return null;
      string clean=Regex.Replace(name,@"\s+"," ").Trim();
      return new CalendarEntry{Kind=kind,Id=id,Name=clean,Title=Label(kind)+" · "+clean,Source=source,At=at.ToUniversalTime(),End=end.HasValue?(DateTimeOffset?)end.Value.ToUniversalTime():null,Published=published};
    }
    public static List<CalendarEntry> Entries(Schedule schedule,BannerState banners,bool asia){return Entries(schedule,banners,asia,null);}
    public static List<CalendarEntry> Entries(Schedule schedule,BannerState banners,bool asia,CalendarFeedState feed) {
      var result=new List<CalendarEntry>();
      Action<CalendarEntry> add=entry=>{if(entry!=null)result.Add(entry);};
      var schedules=new List<Schedule>();if(schedule!=null)schedules.Add(schedule);if(feed!=null && feed.Schedules!=null)schedules.AddRange(feed.Schedules.Where(s=>s!=null));
      foreach(var item in schedules)add(Period(Entry(CalendarKind.Maintenance,"maintenance:"+Normalize(item.Version),item.Version,item.Source,item.Start,item.End,item.PublishedUnix>0?item.PublishedUnix:item.Checked.ToUnixTimeSeconds()),item.Start,item.End));
      // Once available, the independent feed supersedes the saved banner snapshot.
      var records=feed!=null && feed.Schema==CalendarFeedState.CurrentSchema?feed.Events:banners==null?null:banners.GameEvents;
      var events=records==null?new List<GameEvent>():GameEventDates.Latest(records).Select(e=>e.WithMaintenanceEnd(schedules)).ToList();
      if(banners!=null && banners.Debuts!=null)foreach(var debut in banners.Debuts.Where(d=>d!=null && !string.IsNullOrWhiteSpace(d.Name)).GroupBy(d=>Normalize(d.Name)).Select(g=>g.OrderBy(d=>asia?d.AsiaStart:d.EuropeStart).First())) {
        var start=asia?debut.AsiaStart:debut.EuropeStart;var name=Normalize(debut.Name);
        var end=BannerClient.End(debut,asia);string endSource=debut.Source;long published=0;
        if(!end.HasValue && Official(debut.Source) && start!=DateTimeOffset.MinValue) {
          var notice=events.Where(e=>e.Category==GameEventCategory.Debut && Normalize(e.Name)==name && Unix(asia?e.AsiaStartUnix:e.EuropeStartUnix)==start && e.End(asia)>start)
            .OrderByDescending(e=>e.Published).FirstOrDefault();
          if(notice!=null){end=notice.End(asia);endSource=notice.Source;published=notice.Published;}
        }
        add(Period(Entry(CalendarKind.Debut,"debut:"+name,debut.Name,debut.Source,start,null,0),start,end));
        if(end.HasValue)add(Period(Entry(CalendarKind.BannerEnd,"banner-end:"+name,debut.Name,endSource,end.Value,null,published),start,end));
      }
      foreach(var item in events.Where(e=>e.Category!=GameEventCategory.Debut)) {
        var start=Unix(asia?item.AsiaStartUnix:item.EuropeStartUnix);var end=item.End(asia);
        var startKind=CalendarKind.EventStart;var endKind=CalendarKind.EventEnd;
        switch(item.Category) {
          case GameEventCategory.Rerun:startKind=CalendarKind.RerunStart;endKind=CalendarKind.RerunEnd;break;
          case GameEventCategory.Weapon:startKind=CalendarKind.WeaponStart;endKind=CalendarKind.WeaponEnd;break;
          case GameEventCategory.Area:
          case GameEventCategory.ContentUpdate:if(!end.HasValue)startKind=CalendarKind.Milestone;break;
        }
        string id="game:"+item.Identity;
        if(start.HasValue)add(Period(Entry(startKind,id+":start",item.Name,item.Source,start.Value,null,item.Published),start,end));
        if(end.HasValue)add(Period(Entry(endKind,id,item.Name,item.Source,end.Value,null,item.Published),start,end));
      }
      return Ordered(result.GroupBy(e=>e.Id,StringComparer.Ordinal).Select(g=>g.OrderByDescending(e=>e.Published).First())).ToList();
    }
    public static IOrderedEnumerable<CalendarEntry> Ordered(IEnumerable<CalendarEntry> entries){return entries.OrderBy(e=>e.At).ThenBy(e=>e.Kind).ThenBy(e=>e.Name??e.Title,StringComparer.OrdinalIgnoreCase).ThenBy(e=>e.Id,StringComparer.Ordinal);}
    public static IEnumerable<DateTime> GridDays(CalendarEntry entry) {
      if(entry==null || entry.At==DateTimeOffset.MinValue || entry.End.HasValue && entry.End<=entry.At)yield break;
      var first=Clock.Local(entry.At).Date;
      yield return first;
      if(entry.End.HasValue) {
        var last=Clock.Local(entry.End.Value).Date;
        if(last!=first)yield return last;
      }
    }
    public static bool Affects(CalendarEntry entry,DateTime day){return GridDays(entry).Contains(day.Date);}
    public static IEnumerable<CalendarEntry> OnDay(IEnumerable<CalendarEntry> entries,DateTime day){return entries.Where(e=>Affects(e,day));}
    public static DateTime? NearestDay(IEnumerable<CalendarEntry> entries,DateTimeOffset now) {
      var next=Ordered(entries.Where(e=>e!=null && (e.At>=now || e.End.HasValue && e.End.Value>now))).OrderBy(e=>e.At<now?now:e.At).FirstOrDefault();
      return next==null?(DateTime?)null:Clock.Local(next.At<now?now:next.At).Date;
    }
    public static string Fingerprint(IEnumerable<CalendarEntry> entries) {
      var text=string.Join("\n",entries.Select(e=>(e.Id??"")+"|"+e.Kind+"|"+e.Title+"|"+e.Source+"|"+e.At.UtcTicks+"|"+(e.End.HasValue?e.End.Value.UtcTicks:0)+"|"+(e.PeriodStart.HasValue?e.PeriodStart.Value.UtcTicks:0)+"|"+(e.PeriodEnd.HasValue?e.PeriodEnd.Value.UtcTicks:0)));
      using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
    public static string ZoneKey(){var zone=Clock.ZoneOverride==null?TimeZoneInfo.Local:Clock.ZoneOverride();return zone.ToSerializedString();}
  }
  sealed class CalendarSnapshot {
    readonly Dictionary<DateTime,ReadOnlyCollection<CalendarEntry>> days=new Dictionary<DateTime,ReadOnlyCollection<CalendarEntry>>();
    readonly ReadOnlyCollection<CalendarEntry> entries;
    public ReadOnlyCollection<CalendarEntry> Entries {get{return entries;}}
    public string Fingerprint {get;private set;}
    public CalendarSnapshot(IEnumerable<CalendarEntry> source,int mask,string query) {
      string search=CalendarData.Normalize(query);
      entries=CalendarData.Ordered(source.Where(e=>(CalendarData.Mask(e.Kind)&mask)!=0 && (search.Length==0 || CalendarData.Normalize((e.Name??e.Title)+" "+CalendarData.Label(e.Kind)).Contains(search)))).ToList().AsReadOnly();
      Fingerprint=CalendarData.Fingerprint(entries)+"|"+CalendarData.ZoneKey();
      var building=new Dictionary<DateTime,List<CalendarEntry>>();
      foreach(var entry in entries)foreach(var day in CalendarData.GridDays(entry)) {
        List<CalendarEntry> values;if(!building.TryGetValue(day,out values)){values=new List<CalendarEntry>();building[day]=values;}
        values.Add(entry);
      }
      foreach(var pair in building)days[pair.Key]=pair.Value.AsReadOnly();
    }
    public ReadOnlyCollection<CalendarEntry> OnDay(DateTime day){ReadOnlyCollection<CalendarEntry> value;return days.TryGetValue(day.Date,out value)?value:Empty;}
    static readonly ReadOnlyCollection<CalendarEntry> Empty=new List<CalendarEntry>().AsReadOnly();
    public DateTime? NearestDay(DateTimeOffset now){return CalendarData.NearestDay(entries,now);}
    public IEnumerable<CalendarEntry> Agenda(DateTimeOffset now,int dayCount) {
      var through=Clock.Local(now).Date.AddDays(dayCount);
      return entries.Where(e=>(e.At>=now || e.End.HasValue && e.End>now) && Clock.Local(e.At).Date<through).OrderBy(e=>e.At<now?now:e.At).ThenBy(e=>e.Kind).ThenBy(e=>e.Id,StringComparer.Ordinal);
    }
    public static DateTime FirstDay(DateTime month){return new DateTime(month.Year,month.Month,1).AddDays(-(((int)new DateTime(month.Year,month.Month,1).DayOfWeek+6)%7));}
  }
}
