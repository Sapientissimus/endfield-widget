using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EndfieldWidget {
  public sealed class GameEvent {
    public string Name {get;set;}
    public string Source {get;set;}
    public long Published {get;set;}
    public string Identity {get;set;}
    public bool FromOverview {get;set;}
    public long EuropeStartUnix {get;set;}
    public long AsiaStartUnix {get;set;}
    public long EuropeEndUnix {get;set;}
    public long AsiaEndUnix {get;set;}
    public DateTimeOffset? End(bool asia) {
      if(!CalendarData.Official(Source) || string.IsNullOrWhiteSpace(Name) || Name.Length>160)return null;
      long value=asia?AsiaEndUnix:EuropeEndUnix;if(value<=0)return null;
      try{return DateTimeOffset.FromUnixTimeSeconds(value);}catch(ArgumentOutOfRangeException){return null;}
    }
  }
  static class GameEventDates {
    const RegexOptions Options=RegexOptions.IgnoreCase|RegexOptions.Multiline;
    const string DateToken=@"[A-Za-z]+\.?\s+\d{1,2}(?:,?\s+\d{4})?\s*(?:at\s*)?\d{1,2}:\d{2}";
    const string RegionEurope=@"(?:Americas\s*/\s*Europe|Europe\s*/\s*Americas|Europe)";
    public static bool Eligible(string title) {
      title=ArticleLanguage.Title(title);
      return !string.IsNullOrWhiteSpace(title) && title.Contains("[")
        && Regex.IsMatch(title,@"(?:Narrative|Story|Challenge|Camera|Fun\s*&\s*Games|Guide|Fest(?:ival)?|Exploration|Combat|Co-op|AIC\s+Production|Limited[- ]Time)\s+Event",RegexOptions.IgnoreCase)
        && !Regex.IsMatch(title,@"Sign[- ]?In|Log[- ]?In|Daily|Weekly|Headhunting|Arsenal|Battle\s*Pass|Web\s+Event|Discord|Share\s*&\s*Win|Sanity Supply",RegexOptions.IgnoreCase);
    }
    internal static string Name(string title) {
      title=ArticleLanguage.Title(title);
      var names=Regex.Matches(title,@"\[([^\]\r\n]+)\]");
      return names.Count==0?null:names[names.Count-1].Groups[1].Value.Trim();
    }
    static TimeSpan? ExplicitOffset(string text,string region) {
      var match=Regex.Match(text,region+@"\s*Server(?:\s+Time\s+Zone)?\s*:\s*[^\r\n]*?UTC\s*(?<hours>[+-]\d{1,2})(?::(?<minutes>\d{2}))?",RegexOptions.IgnoreCase);
      if(!match.Success)return null;
      int hours=int.Parse(match.Groups["hours"].Value,CultureInfo.InvariantCulture),minutes=match.Groups["minutes"].Success?int.Parse(match.Groups["minutes"].Value,CultureInfo.InvariantCulture):0;
      if(minutes>=60)return null;var offset=TimeSpan.FromMinutes(hours*60+(match.Groups["hours"].Value.StartsWith("-")?-minutes:minutes));
      return offset.Duration()<=TimeSpan.FromHours(14)?(TimeSpan?)offset:null;
    }
    static long Deadline(string timeBlock,string article,bool asia,long published) {
      string region=asia?"Asia":RegionEurope;
      var regions=Regex.Matches(timeBlock,@"(?:Asia|"+RegionEurope+@")\s*Server\s*:\s*",RegexOptions.IgnoreCase);
      string range=timeBlock;
      if(regions.Count>0) {
        Match selected=null;int length=0;
        for(int i=0;i<regions.Count;i++)if(Regex.IsMatch(regions[i].Value,"^"+region+@"\s*Server",RegexOptions.IgnoreCase)){selected=regions[i];length=(i+1<regions.Count?regions[i+1].Index:timeBlock.Length)-selected.Index-selected.Length;break;}
        if(selected==null)return 0;range=timeBlock.Substring(selected.Index+selected.Length,length);
      }
      var separator=Regex.Match(range,@"[–—]|\s-\s");if(!separator.Success)return 0;
      string endPart=range.Substring(separator.Index+separator.Length).Trim();
      var date=Regex.Match(endPart,@"^(?:(?:Before[^\r\n]{0,160}maintenance[ \t]+on|until)[ \t]+)?(?<date>"+DateToken+")",RegexOptions.IgnoreCase);if(!date.Success)return 0;
      // A start/content-update/shop date must never become a guessed event deadline.
      if(Regex.IsMatch(endPart,@"^(?:after|opens?|starts?|throughout)\b",RegexOptions.IgnoreCase))return 0;
      var offset=ExplicitOffset(region+" Server: "+endPart,region)??ExplicitOffset(article,region);if(!offset.HasValue)return 0;
      try {
        var publication=DateTimeOffset.FromUnixTimeSeconds(published);string endDate=date.Groups["date"].Value;var end=OfficialDates.Published(endDate,offset.Value,publication).ToUniversalTime();
        var first=Regex.Match(range.Substring(0,separator.Index),DateToken,RegexOptions.IgnoreCase);
        if(first.Success) {
          var start=OfficialDates.Published(first.Value,offset.Value,publication).ToUniversalTime();
          if(!Regex.IsMatch(first.Value,@"\b\d{4}\b") && publication.Month<=2 && start.Month>=11 && start>end)start=start.AddYears(-1);
          if(end<=start || end-start>TimeSpan.FromDays(180))return 0;
        }
        if(end<publication.AddDays(-14) || end>publication.AddDays(180))return 0;
        return end.ToUnixTimeSeconds();
      }catch{return 0;}
    }
    static long Beginning(string block,string whole,bool asia,long published,long deadline) {
      if(deadline<=0)return 0;
      string region=asia?"Asia":RegionEurope;
      var regions=Regex.Matches(block,@"(?:Asia|"+RegionEurope+@")\s*Server\s*:\s*",RegexOptions.IgnoreCase);
      if(regions.Count>0) {
        int selected=-1;for(int i=0;i<regions.Count;i++)if(Regex.IsMatch(regions[i].Value,"^"+region+@"\s*Server",RegexOptions.IgnoreCase)){selected=i;break;}
        if(selected<0)return 0;int from=regions[selected].Index+regions[selected].Length,to=selected+1<regions.Count?regions[selected+1].Index:block.Length;block=block.Substring(from,to-from);
      }
      var divider=Regex.Match(block,@"[–—]|\s-\s");if(!divider.Success)return 0;
      var date=Regex.Match(block.Substring(0,divider.Index),DateToken,RegexOptions.IgnoreCase);if(!date.Success)return 0;
      var offset=ExplicitOffset(region+" Server: "+block.Substring(divider.Index+divider.Length),region)??ExplicitOffset(whole,region);if(!offset.HasValue)return 0;
      try {
        var publication=DateTimeOffset.FromUnixTimeSeconds(published);var start=OfficialDates.Published(date.Value,offset.Value,publication).ToUniversalTime();var end=DateTimeOffset.FromUnixTimeSeconds(deadline);
        if(!Regex.IsMatch(date.Value,@"\b\d{4}\b") && publication.Month<=2 && start.Month>=11 && start>end)start=start.AddYears(-1);
        return start<end && end-start<=TimeSpan.FromDays(180)?start.ToUnixTimeSeconds():0;
      }catch{return 0;}
    }
    static string TimeBlock(string body) {
      var label=Regex.Match(body,@"^[ \t]*(?:[·•■][ \t]*)?(?:▼\s*//[ \t]*)?Event\s+(?:Time|Period|Duration)\s*:?[ \t]*(?<inline>[^\r\n]*)",Options);
      if(!label.Success)return null;
      string rest=body.Substring(label.Index+label.Length);
      var next=Regex.Match(rest,@"^[ \t]*(?:[·•■]|▼\s*//)[ \t]*(?:Event\s+(?:Content|Details|Rewards)|Goods|Shop|Participation|※)",Options);
      if(next.Success)rest=rest.Substring(0,next.Index);
      return label.Groups["inline"].Value+"\n"+rest;
    }
    static GameEvent Entry(string header,string body,string whole,string cid,long published) {
      if(!Eligible(header) || !Regex.IsMatch(cid??"",@"^\d+$") || published<=0 || Regex.IsMatch(body,@"complete[^\r\n]*(?:every day|daily|weekly)|(?:выполняйте|выполните)[^\r\n]*(?:каждый день|ежедневно|еженедельно)",RegexOptions.IgnoreCase))return null;
      string time=TimeBlock(body);if(time==null)return null;
      long europe=Deadline(time,whole,false,published),asia=Deadline(time,whole,true,published);
      if(europe==0 && asia==0)return null;
      return new GameEvent{Name=Name(header),Source=NewsClient.NewsUrl+"/"+cid,Published=published,EuropeEndUnix=europe,AsiaEndUnix=asia,EuropeStartUnix=Beginning(time,whole,false,published,europe),AsiaStartUnix=Beginning(time,whole,true,published,asia)};
    }
    public static List<GameEvent> Parse(string html,string title,string cid,long published) {
      var result=new List<GameEvent>();string text=NewsClient.StructuredText(html);
      if(BannerClient.IsOverviewTitle(title)) {
        var section=Regex.Match(text,@"^[ \t]*■[ \t]*New Events[ \t]*\r?$(?<body>.*?)(?=^[ \t]*■(?![ \t]*(?:Event Time\b|Event Rewards\b|Event Details\b|Participation\b))|^[ \t]*▼\s*//\s*(?:Optimizations|Bug Fixes)|\z)",Options|RegexOptions.Singleline);
        if(!section.Success)throw new FormatException("Event section unavailable.");
        string content=section.Groups["body"].Value;
        var headers=Regex.Matches(content,@"^[ \t]*\d+[.)][ \t]*(?<title>[^\r\n]*\[[^\]\r\n]+\][^\r\n]*)\r?$",RegexOptions.Multiline);
        if(headers.Count==0 && !string.IsNullOrWhiteSpace(content))throw new FormatException("Event headings unavailable.");
        for(int i=0;i<headers.Count;i++) {
          int from=headers[i].Index+headers[i].Length,to=i+1<headers.Count?headers[i+1].Index:content.Length;
          var entry=Entry(headers[i].Groups["title"].Value,content.Substring(from,to-from),text,cid,published);if(entry!=null){entry.FromOverview=true;result.Add(entry);}
        }
      }else if(Eligible(title)) {
        if(TimeBlock(text)==null){if(ContentUpdateOnly(text))return result;throw new FormatException("Event time unavailable.");}
        var entry=Entry(title,text,text,cid,published);if(entry!=null)result.Add(entry);
      }
      return result;
    }
    internal static List<string> DeclaredNames(string html,string title) {
      if(ContentUpdateOnly(NewsClient.StructuredText(html)))return new List<string>();
      if(!BannerClient.IsOverviewTitle(title))return Eligible(title)?new List<string>{Name(title)}:new List<string>();
      string text=NewsClient.StructuredText(html);
      return Regex.Matches(text,@"^[ \t]*\d+[.)][ \t]*(?<title>[^\r\n]*\[[^\]\r\n]+\][^\r\n]*)\r?$",RegexOptions.Multiline).Cast<Match>().Select(m=>m.Groups["title"].Value).Where(Eligible).Select(Name).Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
    static bool ContentUpdateOnly(string text){return TimeBlock(text)==null && Regex.IsMatch(text,@"^[ \t]*(?:[·•■]\s*|▼\s*//\s*)?Event Content Update Time\s*:",Options);}
    public static List<GameEvent> Latest(IEnumerable<GameEvent> entries) {
      var result=new List<GameEvent>();
      foreach(var group in entries.Where(e=>e!=null && CalendarData.Official(e.Source) && !string.IsNullOrWhiteSpace(e.Name)).GroupBy(e=>CalendarData.Normalize(e.Name))) {
        var occurrences=new List<List<GameEvent>>();
        foreach(var entry in group.OrderBy(e=>e.Published).ThenBy(e=>e.Source,StringComparer.Ordinal)) {
          var occurrence=occurrences.FirstOrDefault(items=>items.Any(old=>SameOccurrence(old,entry)));
          if(occurrence==null){occurrence=new List<GameEvent>();occurrences.Add(occurrence);}occurrence.Add(entry);
        }
        foreach(var occurrence in occurrences) {
          var selected=occurrence.OrderByDescending(e=>e.Published).ThenBy(e=>e.FromOverview).ThenByDescending(e=>e.Source,StringComparer.Ordinal).First();
          string identity=occurrence.Select(e=>e.Identity).FirstOrDefault(id=>!string.IsNullOrEmpty(id));
          long beginning=occurrence.Select(e=>e.EuropeStartUnix>0?e.EuropeStartUnix:e.AsiaStartUnix).Where(v=>v>0).DefaultIfEmpty(occurrence.Min(e=>e.Published)/86400*86400).Min();
          selected.Identity=identity??group.Key+"@"+beginning.ToString(CultureInfo.InvariantCulture);result.Add(selected);
        }
      }
      return result;
    }
    static bool SameOccurrence(GameEvent first,GameEvent second) {
      if(first.Source==second.Source)return true;
      long start=first.EuropeStartUnix>0?first.EuropeStartUnix:first.AsiaStartUnix,other=second.EuropeStartUnix>0?second.EuropeStartUnix:second.AsiaStartUnix;
      if(start>0 && other>0)return Math.Abs((double)start-other)<=86400*2;
      long end=first.EuropeEndUnix>0?first.EuropeEndUnix:first.AsiaEndUnix,last=second.EuropeEndUnix>0?second.EuropeEndUnix:second.AsiaEndUnix;
      return Math.Abs((double)first.Published-second.Published)<=86400*45 && Math.Abs((double)end-last)<=86400*21;
    }
  }
  partial class BannerClient {
    static IEnumerable<GameEvent> CachedEvents(BannerState previous,string cid) {
      return previous==null || previous.GameEvents==null?Enumerable.Empty<GameEvent>():previous.GameEvents.Where(e=>e!=null && e.Source==NewsClient.NewsUrl+"/"+cid);
    }
    internal async Task AddGameEventArticles(List<Dictionary<string,object>> archive,BannerState previous,List<GameEvent> events,Action failed) {
      var candidates=archive.Where(a=>GameEventDates.Eligible(Json.Str(a,"title"))).OrderByDescending(a=>Json.Num(a,"displayTime")).Take(12).ToList();
      foreach(var article in candidates) {
        string cid=Json.Str(article,"cid");
        if(!Regex.IsMatch(cid??"",@"^\d+$"))continue;
        try {
          object value=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);
          events.AddRange(GameEventDates.Parse(Json.Str(value,"data","data"),FetchedTitle(value,article),cid,Json.Num(article,"displayTime")));
        }catch{events.AddRange(CachedEvents(previous,cid));failed();}
      }
    }
  }
}
