using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  static partial class GameEventDates {
    const string CalendarRange=@"[–—]|\s-\s|;\s*ends?\b";
    const string CalendarDateAtStart=@"^\s*(?:(?:Opens?|Starts?|From|Until)\s+|Before[^\r\n]{0,140}maintenance\s+on\s+)?(?<date>"+DateToken+")";
    const string CalendarField=@"(?:Event\s+(?:Time|Period|Duration|Availability)|Availability|Season\s+Update|(?:Event\s+Content\s+)?Update\s+Time)";
    const string NumberedHeader=@"^[ \t]*\d+[.)][ \t]*(?<title>[^\r\n]*\[[^\]\r\n]+\][^\r\n]*)";
    const string CalendarRegion=@"(?:Asia|"+RegionEurope+@")\s*Server\s*:";
    static Match Bracket(string text){return Regex.Match(text??"", @"\[(?<name>[^\[\]\r\n]+)\]");}
    static bool MatchCalendar(string text,string pattern){return Regex.IsMatch(text??"",pattern,Options);}

    static GameEventCategory? CalendarCategory(string title) {
      if(MatchCalendar(title,@"Web\s+Event|Discord|Share\s*&\s*Win|Battle\s*Pass"))return null;
      if(MatchCalendar(title,@"Arsenal\s+Issue|RE[-\s]?Factor\s+Issue|\bIssue\]\s+LTO\s+Details"))return GameEventCategory.Weapon;
      if(MatchCalendar(title,@"(?:RE[-\s]?Factor|Rerun|Re-run)[^\r\n]*Headhunting"))return GameEventCategory.Rerun;
      if(MatchCalendar(title,@"(?:Sign|Log)[- ]?In\s+Event"))return GameEventCategory.SignIn;
      if(MatchCalendar(title,@"Sanity\s+Supply|(?:снабжен|поставк)\w*\s+рассудк"))return GameEventCategory.Supply;
      if(MatchCalendar(title,@"Operator\s+Trial\s+Event|Combat\s+Drills"))return GameEventCategory.Trial;
      if(MatchCalendar(title,@"Season\s+Update"))return GameEventCategory.Season;
      if(MatchCalendar(title,@"Permanent\s+(?:Challenge|Puzzle)|Spatial\s+Set\s+Update|Challenge\s+Gameplay"))return GameEventCategory.Gameplay;
      if(Eligible(title))return GameEventCategory.Event;
      return null;
    }
    internal static bool CalendarEligible(string title) {
      title=ArticleLanguage.Title(title);
      return Bracket(title).Success && (CalendarCategory(title).HasValue || BannerClient.IsDebutTitle(title));
    }
    static string CalendarSection(string text,string heading) {
      var section=Regex.Match(text,@"^[ \t]*■[ \t]*(?:"+heading+@")[ \t]*\r?$(?<body>.*?)(?=^[ \t]*■|^[ \t]*▼\s*//\s*(?:Optimizations|Bug Fixes)|\z)",Options|RegexOptions.Singleline);
      return section.Success?section.Groups["body"].Value:null;
    }
    static string FieldValue(string text,Match field) {
      var value=field.Groups["value"].Value.Trim();
      var rest=text.Substring(field.Index+field.Length).Split('\n');
      foreach(string raw in rest) {
        string line=raw.Trim();
        if(line.Length==0)continue;
        if(!MatchCalendar(line,@"^(?:"+CalendarRegion+@"|"+DateToken+@"|After\s+\[)"))break;
        value+="\n"+line;
      }
      return value.Trim();
    }
    static MatchCollection CalendarFields(string text) {
      return Regex.Matches(text,@"^[ \t]*(?:[·•■][ \t]*|▼[ \t]*//[ \t]*)?(?<label>"+CalendarField+@"|\[[^\]\r\n]+\]\s+Availability)[ \t]*(?::[ \t]*(?<value>[^\r\n]*)|(?=\r?$))",Options);
    }
    static long CalendarMoment(string text,TimeSpan? offset,DateTimeOffset publication) {
      if(!offset.HasValue)return 0;
      var date=Regex.Match(text,CalendarDateAtStart,Options);
      if(!date.Success)return 0;
      try {
        var at=OfficialDates.Published(date.Groups["date"].Value,offset.Value,publication);
        return at>=publication.AddDays(-14) && at<=publication.AddDays(180)?at.ToUnixTimeSeconds():0;
      }catch(ArgumentException){return 0;}
      catch(FormatException){return 0;}
    }
    static string ServerRange(string time,bool asia) {
      var regions=Regex.Matches(time,CalendarRegion,Options);
      if(regions.Count==0)return time;
      string region=asia?"Asia":RegionEurope;
      for(int i=0;i<regions.Count;i++)if(MatchCalendar(regions[i].Value,"^"+region+@"\s*Server")) {
        int from=regions[i].Index+regions[i].Length;
        int to=i+1<regions.Count?regions[i+1].Index:time.Length;
        return time.Substring(from,to-from).Trim();
      }
      return null;
    }
    static void CalendarBounds(string time,string text,bool asia,DateTimeOffset publication,Schedule schedule,out long start,out long end,out bool maintenanceEnd) {
      start=end=0;
      maintenanceEnd=false;
      var range=ServerRange(time,asia);
      if(string.IsNullOrWhiteSpace(range))return;
      string region=asia?"Asia":RegionEurope;
      var offset=ExplicitOffset(region+" Server: "+range,region)??ExplicitOffset(text,region);
      var divider=Regex.Match(range,CalendarRange,Options);
      string first=divider.Success?range.Substring(0,divider.Index):range;
      start=CalendarMoment(first,offset,publication);
      var release=Regex.Match(first,@"^(?:Opens?\s+)?After\s+\[([^\]]+)\]\s+version\s+(?:update|release)",Options);
      if(start==0 && schedule!=null && release.Success && CalendarData.Normalize(release.Groups[1].Value)==CalendarData.Normalize(schedule.Version))start=schedule.End.ToUnixTimeSeconds();
      if(!divider.Success)return;
      string last=range.Substring(divider.Index+divider.Length);
      end=CalendarMoment(last,offset,publication);
      maintenanceEnd=end==0 && MatchCalendar(last,@"^\s*(?:Before\s+(?:the\s+)?(?:next\s+)?version\s+update\s+and\s+maintenance|до\s+начала\s+технических\s+работ\s+по\s+обновлению\s+версии)\s*[.!]?\s*$");
      if(start>0 && end>0 && (end<=start || end-start>86400L*180))start=end=0;
    }
    static void AddCalendarWindow(List<GameEvent> records,string name,GameEventCategory category,string value,string text,string cid,long published,Schedule schedule,bool overview) {
      if(string.IsNullOrWhiteSpace(name) || name.Length>160 || string.IsNullOrWhiteSpace(value))return;
      var publication=DateTimeOffset.FromUnixTimeSeconds(published);
      long es,ee,asStart,ae;
      bool em,am;
      CalendarBounds(value,text,false,publication,schedule,out es,out ee,out em);
      CalendarBounds(value,text,true,publication,schedule,out asStart,out ae,out am);
      if(es==0 && ee==0 && asStart==0 && ae==0)return;
      records.Add(new GameEvent {
        Name=name,Category=category,Source=NewsClient.NewsUrl+"/"+cid,Published=published,FromOverview=overview,
        EuropeStartUnix=es,EuropeEndUnix=ee,AsiaStartUnix=asStart,AsiaEndUnix=ae,EuropeEndAtMaintenance=em,AsiaEndAtMaintenance=am
      });
    }
    static void ReadCalendarWindow(List<GameEvent> records,string title,string body,string text,string cid,long published,Schedule schedule,bool overview,bool gameplay,GameEventCategory? categoryHint=null) {
      string name=Name(title);
      var category=categoryHint??CalendarCategory(title);
      bool debut=BannerClient.IsDebutTitle(title);
      if(!category.HasValue && !debut)return;
      var fields=CalendarFields(body);
      string availability=null;
      foreach(Match field in fields) {
        string label=field.Groups["label"].Value,value=FieldValue(body,field);
        if(label.StartsWith("[",StringComparison.Ordinal)) {
          AddCalendarWindow(records,Name(label),GameEventCategory.Exchange,value,text,cid,published,schedule,overview);
        }else if(MatchCalendar(label,@"Update\s+Time")) {
          AddCalendarWindow(records,name,gameplay && !MatchCalendar(label,@"Content")?GameEventCategory.Gameplay:GameEventCategory.ContentUpdate,value,text,cid,published,schedule,overview);
        }else if(MatchCalendar(label,@"^Season\s+Update")) {
          var season=Regex.Matches(body.Substring(0,field.Index),@"^[ \t]*[○◦]\s*\[(?<name>[^\]\r\n]+)\]",Options).Cast<Match>().LastOrDefault();
          if(season!=null)AddCalendarWindow(records,season.Groups["name"].Value,GameEventCategory.Season,value,text,cid,published,schedule,overview);
        }else {
          if(availability==null)availability=value;
          if(category.HasValue) {
            var timed=Regex.Matches(body.Substring(0,field.Index),@"^[ \t]*\[(?<name>[^\]\r\n]+)\][^\r\n]*limited[- ]time[^\r\n]*event",Options).Cast<Match>().LastOrDefault();
            if(gameplay && timed!=null && !MatchCalendar(label,@"^Availability"))AddCalendarWindow(records,timed.Groups["name"].Value,GameEventCategory.Event,value,text,cid,published,schedule,overview);
            else AddCalendarWindow(records,gameplay && Bracket(title).Success?Bracket(title).Groups["name"].Value:name,category.Value,value,text,cid,published,schedule,overview);
          }
        }
      }
      if(debut && availability!=null) {
        var featured=BannerClient.FeaturedName(body);
        if(featured==null) {
          var trial=Regex.Match(body,@"(?:trying\s+out|trial\s+of)\s+the\s+6[★*]\s+operator\s*\[([^\]]+)\]",Options);
          if(trial.Success)featured=trial.Groups[1].Value;
        }
        AddCalendarWindow(records,featured,GameEventCategory.Debut,availability,text,cid,published,schedule,overview);
        if(MatchCalendar(body,@"\[Combat Drills\]|Operator Trial Event|for trying out the\s+6[★*]\s+operator"))
          AddCalendarWindow(records,featured,GameEventCategory.Trial,availability,text,cid,published,schedule,overview);
      }
      if(gameplay)foreach(Match stage in Regex.Matches(body,@"\b(?:become|be)\s+available\s+on\s+(?<date>"+DateToken+")",Options)) {
        AddCalendarWindow(records,name,GameEventCategory.ContentUpdate,stage.Groups["date"].Value,text,cid,published,schedule,overview);
      }
      if(!overview && category.HasValue && category.Value==GameEventCategory.Event && fields.Count==0)
        throw new FormatException("Event time unavailable.");
    }
    internal static List<GameEvent> ParseCalendar(string html,string title,string cid,long published,Schedule schedule,string archiveTitle=null) {
      var result=new List<GameEvent>();
      if(!Regex.IsMatch(cid??"",@"^\d+$") || published<=0)return result;
      string text=NewsClient.StructuredText(html);
      if(!BannerClient.IsOverviewTitle(title)) {
        ReadCalendarWindow(result,title,text,text,cid,published,schedule,false,false,CalendarCategory(archiveTitle));
        return result;
      }
      string events=CalendarSection(text,"New Events");
      if(events==null)throw new FormatException("Event section unavailable.");
      var sections=new[]{
        events,
        CalendarSection(text,@"New Headhunting(?: Events)?(?: and (?:Issues|Arsenal))?"),
        CalendarSection(text,@"Event & Gameplay Updates")
      };
      for(int section=0;section<sections.Length;section++) {
        string body=sections[section];
        if(string.IsNullOrWhiteSpace(body))continue;
        var headings=Regex.Matches(body,NumberedHeader,Options);
        if(headings.Count==0 && section==0)throw new FormatException("Event headings unavailable.");
        for(int i=0;i<headings.Count;i++) {
          int from=headings[i].Index+headings[i].Length,to=i+1<headings.Count?headings[i+1].Index:body.Length;
          ReadCalendarWindow(result,headings[i].Groups["title"].Value,body.Substring(from,to-from),text,cid,published,schedule,true,section==2);
        }
      }
      string areas=CalendarSection(text,"New Areas");
      if(areas!=null && schedule!=null)foreach(Match area in Regex.Matches(areas,@"^\s*\[([^\]\r\n]+)\]\s*$",Options))
        result.Add(new GameEvent{Name=area.Groups[1].Value,Category=GameEventCategory.Area,Source=NewsClient.NewsUrl+"/"+cid,Published=published,FromOverview=true,EuropeStartUnix=schedule.End.ToUnixTimeSeconds(),AsiaStartUnix=schedule.End.ToUnixTimeSeconds()});
      return result;
    }
  }
}
