using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  enum CalendarKind {Unknown,Maintenance,Debut,BannerEnd,EventEnd}
  sealed class CalendarEntry {
    public string Id,Title,Name,Source;
    public CalendarKind Kind;
    public DateTimeOffset At;
    public DateTimeOffset? End;
    public DateTimeOffset? PeriodStart,PeriodEnd;
    public long Published;
  }
  static class CalendarData {
    public const int AllKinds=15;
    public static bool Official(string source){return source!=null && Publications.Official(source);}
    public static string Normalize(string text){return Regex.Replace((text??"").Normalize(NormalizationForm.FormKC).Replace('\u2019','\''),@"\s+"," ").Trim().ToLowerInvariant();}
    public static string Label(CalendarKind kind) {
      switch(kind){case CalendarKind.Maintenance:return L.T("Maintenance");case CalendarKind.Debut:return L.T("Debut");case CalendarKind.BannerEnd:return L.T("Banner ends");case CalendarKind.EventEnd:return L.T("Event ends");default:return L.T("Event");}
    }
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
      if(banners!=null && banners.Debuts!=null)foreach(var debut in banners.Debuts.Where(d=>d!=null && !string.IsNullOrWhiteSpace(d.Name)).GroupBy(d=>Normalize(d.Name)).Select(g=>g.OrderBy(d=>asia?d.AsiaStart:d.EuropeStart).First())) {
        var start=asia?debut.AsiaStart:debut.EuropeStart;
        var end=BannerClient.End(debut,asia);add(Period(Entry(CalendarKind.Debut,"debut:"+Normalize(debut.Name),debut.Name,debut.Source,start,null,0),start,end));
        if(end.HasValue)add(Period(Entry(CalendarKind.BannerEnd,"banner-end:"+Normalize(debut.Name),debut.Name,debut.Source,end.Value,null,0),start,end));
      }
      // The independent feed is authoritative once available: a withdrawal must
      // not be resurrected from an older banner snapshot.
      var events=feed!=null && feed.Schema==CalendarFeedState.CurrentSchema?feed.Events:banners==null?null:banners.GameEvents;
      if(events!=null)foreach(var item in GameEventDates.Latest(events)) {
        var end=item.End(asia);if(end.HasValue)add(Period(Entry(CalendarKind.EventEnd,"game:"+item.Identity,item.Name,item.Source,end.Value,null,item.Published),Unix(asia?item.AsiaStartUnix:item.EuropeStartUnix),end));
      }
      return Ordered(result.GroupBy(e=>e.Id,StringComparer.Ordinal).Select(g=>g.OrderByDescending(e=>e.Published).First())).ToList();
    }
    public static IOrderedEnumerable<CalendarEntry> Ordered(IEnumerable<CalendarEntry> entries){return entries.OrderBy(e=>e.At).ThenBy(e=>e.Kind).ThenBy(e=>e.Name??e.Title,StringComparer.OrdinalIgnoreCase).ThenBy(e=>e.Id,StringComparer.Ordinal);}
    public static bool Affects(CalendarEntry entry,DateTime day) {
      if(entry==null || entry.At==DateTimeOffset.MinValue)return false;
      var start=Clock.Local(entry.At).Date;
      if(!entry.End.HasValue)return start==day.Date;
      if(entry.End<=entry.At)return false;
      return start<=day.Date && Clock.Local(entry.End.Value.AddTicks(-1)).Date>=day.Date;
    }
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
      foreach(var entry in entries) {
        DateTime first=Clock.Local(entry.At).Date,last=entry.End.HasValue && entry.End>entry.At?Clock.Local(entry.End.Value.AddTicks(-1)).Date:first;
        if(last<first || (last-first).TotalDays>31)continue;
        for(DateTime day=first;day<=last;day=day.AddDays(1)){List<CalendarEntry> values;if(!building.TryGetValue(day,out values)){values=new List<CalendarEntry>();building[day]=values;}values.Add(entry);if(day==DateTime.MaxValue.Date)break;}
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
