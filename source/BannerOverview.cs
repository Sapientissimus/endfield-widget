using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class BannerClient {
    public static bool IsOverviewTitle(string title){return Regex.IsMatch(ArticleLanguage.Title(title),@"^\[[^\]]+\]\s+Version Update Notes$",RegexOptions.IgnoreCase);}
    static BannerNotice FirstNotice(IEnumerable<BannerNotice> notices) {
      var ordered=notices.OrderBy(n=>n.Published).ThenBy(n=>n.Cid,StringComparer.Ordinal).ToList();
      var first=ordered[0];if(!first.Overview)return first;
      // A dedicated notice for the same first banner refines an overview. A later
      // return after that debut cannot replace the operator's original debut.
      return ordered.Where(n=>!n.Overview && string.Equals(n.Banner,first.Banner,StringComparison.OrdinalIgnoreCase)
        && ((first.StartHint>0 && n.Published<=first.StartHint)
          || (!string.IsNullOrEmpty(first.ReleaseVersion) && string.Equals(n.ReleaseVersion,first.ReleaseVersion,StringComparison.OrdinalIgnoreCase))))
        .OrderByDescending(n=>n.Published).FirstOrDefault()??first;
    }
    public static List<BannerNotice> ParseOverview(string html,string title,string cid,long published) {
      var found=new List<BannerNotice>();if(!IsOverviewTitle(title))return found;
      string text=NewsClient.StructuredText(html);
      var operatorSection=Regex.Match(text,@"New Operators\s*(?<body>.*?)(?=\s*■|\z)",RegexOptions.Singleline|RegexOptions.IgnoreCase);
      var newOperators=new HashSet<string>(Regex.Matches(operatorSection.Groups["body"].Value,@"\[([^\]]+)\]").Cast<Match>().Select(m=>m.Groups[1].Value.Trim()),StringComparer.OrdinalIgnoreCase);
      var headers=Regex.Matches(text,@"^[ \t]*(?:\d+[.)][ \t]*)?\[(?<banner>[^\]\r\n]+)\][ \t]+Chartered[ \t]+Headhunting(?:[ \t]+Details)?[ \t]*\r?$",RegexOptions.Multiline|RegexOptions.IgnoreCase);
      if(headers.Count==0 && newOperators.Count>0 && text.IndexOf("Chartered Headhunting",StringComparison.OrdinalIgnoreCase)>=0)throw new FormatException("Не вдалося прочитати графік дебютів у описі версії.");
      Schedule release=null;
      int year=DateTimeOffset.FromUnixTimeSeconds(published).Year;
      try{release=NewsClient.ParseSchedule(html,title,NewsClient.NewsUrl+"/"+cid,year);}catch(FormatException){}
      foreach(Match header in headers) {
        int contentStart=header.Index+header.Length;
        var end=Regex.Match(text.Substring(contentStart),@"^[ \t]*(?:\d+[.)][ \t]+|■[ \t]+|▼)",RegexOptions.Multiline);
        int length=end.Success?contentStart+end.Index-header.Index:text.Length-header.Index;
        string block=text.Substring(header.Index,length);
        var notice=ParseNotice(block,"["+header.Groups["banner"].Value+"] Chartered Headhunting",cid,published);
        if(newOperators.Count>0 && !newOperators.Contains(notice.Name))continue;
        notice.Overview=true;
        if(!string.IsNullOrEmpty(notice.ReleaseVersion)) {
          if(release!=null && string.Equals(release.Version,notice.ReleaseVersion,StringComparison.OrdinalIgnoreCase))notice.StartHint=release.End.ToUnixTimeSeconds();
        } else notice.StartHint=NewsClient.ParseDate(notice.LocalStart,TimeSpan.FromHours(-5),year).ToUnixTimeSeconds();
        found.Add(notice);
      }
      return found;
    }
    async Task AddOverviews(List<Dictionary<string,object>> archive,BannerState previous,List<BannerNotice> notices,List<GameEvent> events,Action eventFailure) {
      var overviewArticles=archive.Where(a=>IsOverviewTitle(Json.Str(a,"title"))).OrderByDescending(a=>Json.Num(a,"displayTime")).Take(3).ToList();
      var refreshed=new HashSet<string>(overviewArticles.Select(a=>Json.Str(a,"cid")),StringComparer.Ordinal);
      if(previous!=null && previous.Notices!=null)notices.AddRange(previous.Notices.Where(n=>n.Overview && !refreshed.Contains(n.Cid)));
      foreach(var article in overviewArticles) {
        string cid=Json.Str(article,"cid");
        var cached=previous!=null && previous.Notices!=null?previous.Notices.Where(n=>n.Overview && n.Cid==cid).ToList():new List<BannerNotice>();
        bool upcoming=previous!=null && previous.Debuts!=null && previous.Debuts.Any(d=>d.Source==NewsClient.NewsUrl+"/"+cid && (d.EuropeStart>DateTimeOffset.UtcNow || d.AsiaStart>DateTimeOffset.UtcNow));
        if(cached.Count>0 && cached.All(n=>n.EndChecked) && !upcoming && Json.Num(article,"displayTime")<DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds()) {
          notices.AddRange(cached);
          try{var fresh=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);events.AddRange(GameEventDates.Parse(Json.Str(fresh,"data","data"),FetchedTitle(fresh,article),cid,Json.Num(article,"displayTime")));}
          catch{events.AddRange(CachedEvents(previous,cid));eventFailure();}
          continue;
        }
        object value=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);
        notices.AddRange(ParseOverview(Json.Str(value,"data","data"),FetchedTitle(value,article),cid,Json.Num(article,"displayTime")));
        try{events.AddRange(GameEventDates.Parse(Json.Str(value,"data","data"),FetchedTitle(value,article),cid,Json.Num(article,"displayTime")));}catch{events.AddRange(CachedEvents(previous,cid));eventFailure();}
      }
    }
  }
}
