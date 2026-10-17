using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EndfieldWidget {
  public class BannerNotice {
    public bool Overview {get;set;}
    public long StartHint {get;set;}
    public string Cid {get;set;}
    public string Name {get;set;}
    public string Banner {get;set;}
    public string ReleaseVersion {get;set;}
    public string LocalStart {get;set;}
    public string AsiaLocalStart {get;set;}
    public bool EndChecked {get;set;}
    public string LocalEnd {get;set;}
    public string AsiaLocalEnd {get;set;}
    public long Published {get;set;}
  }
  public class BannerArt {
    public string Name {get;set;}
    public string Key {get;set;}
    public string Portrait {get;set;}
  }
  public class Debut {
    public string Name {get;set;}
    public string Banner {get;set;}
    public string Source {get;set;}
    public string Key {get;set;}
    public string Portrait {get;set;}
    public DateTimeOffset EuropeStart {get;set;}
    public DateTimeOffset AsiaStart {get;set;}
    public long EuropeEndUnix {get;set;}
    public long AsiaEndUnix {get;set;}
  }
  public class BannerState {
    public List<GameEvent> GameEvents {get;set;}
    public bool EventLookupFailed {get;set;}
    public string ArtError {get;set;}
    public List<Debut> Debuts {get;set;}
    public List<BannerNotice> Notices {get;set;}
    public DateTimeOffset Checked {get;set;}
    public static BannerState Seed() {
      if(Publications.Locale!="ru-ru" && Publications.Locale!="en-us")return new BannerState{Notices=new List<BannerNotice>(),Debuts=new List<Debut>()};
      bool russian=Publications.Locale=="ru-ru";
      return new BannerState {Notices=new List<BannerNotice>(),Debuts=new List<Debut> {
        new Debut {Name=russian?"Тифея":"Typhoeus",Banner=russian?"Зимняя охота":"Winter's Hunt",Source=NewsClient.NewsUrl+"/6172",Key="typhoeus",Portrait="https://web-static.hg-cdn.com/endfield/official-v4/_next/static/media/typhoea.87cfb4cd.png",EuropeStart=new DateTimeOffset(2026,9,2,4,0,0,TimeSpan.Zero),AsiaStart=new DateTimeOffset(2026,9,2,4,0,0,TimeSpan.Zero)},
        new Debut {Name=russian?"Си":"Si",Banner=russian?"Преображение":"Rejuvenation",Source=NewsClient.NewsUrl+"/4489",Key="si",Portrait="https://web-static.hg-cdn.com/endfield/official-v4/_next/static/media/jsspsi.19ec05d9.png",EuropeStart=Schedule.Seed().End,AsiaStart=Schedule.Seed().End}
      }};
    }
  }

  partial class BannerClient {
    const string Api="https://web-news.gryphline.com/api/bulletin";
    static string Query {get{return Publications.Query;}}
    readonly HttpClient http;
    readonly PublicationContext publication;
    public BannerClient(HttpClient client) {http=client;publication=Publications.Current;}
    public string CacheFile(string name){return Publications.Cache(name,publication.Code);}
    internal static string ArtKey(string key){return key=="jsspsi"?"si":key=="minghe"?"argent-flow":key=="typhoea"?"typhoeus":key;}
    internal static string OfficialArtKey(string key){return key=="si"?"jsspsi":key=="argent-flow"?"minghe":key=="typhoeus"?"typhoea":key;}
    async Task<T> InPublication<T>(Func<Task<T>> operation){using(Publications.Enter(publication))return await operation().ConfigureAwait(false);}
    static string FetchedTitle(object response,Dictionary<string,object> article){string title=Json.Str(response,"data","title");return string.IsNullOrEmpty(title)?Json.Str(article,"title"):title;}
    public static bool IsDebutTitle(string title) {
      title=ArticleLanguage.Title(title);
      return Regex.IsMatch(title??"",@"\]\s*Chartered Headhunting\s*(?:Details)?$",RegexOptions.IgnoreCase)
        && !Regex.IsMatch(title??"",@"RE[-\s]?Factor|rerun|re-run|returning",RegexOptions.IgnoreCase);
    }
    public static Debut Select(BannerState state,DateTimeOffset now,bool asia) {
      if(state==null || state.Debuts==null)return null;
      return state.Debuts.Where(d=>d!=null && (asia?d.AsiaStart:d.EuropeStart)<=now)
        .OrderByDescending(d=>asia?d.AsiaStart:d.EuropeStart).FirstOrDefault();
    }
    public static List<BannerNotice> FirstDebuts(IEnumerable<BannerNotice> notices) {

      // Each operator can change the background once. A later banner with the same
      // operator cannot replace the newer operator, even if it uses the same banner type.
      return notices.Where(n=>n!=null && !string.IsNullOrWhiteSpace(n.Name)).GroupBy(n=>n.Name,StringComparer.OrdinalIgnoreCase)
        .Select(g=>FirstNotice(g)).ToList();
    }
    public static Debut Next(BannerState state,DateTimeOffset now,bool asia) {
      if(state==null || state.Debuts==null)return null;
      return state.Debuts.Where(d=>d!=null && !string.IsNullOrWhiteSpace(d.Name))
        .GroupBy(d=>d.Name,StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderBy(d=>asia?d.AsiaStart:d.EuropeStart).First())
        .Where(d=>(asia?d.AsiaStart:d.EuropeStart)>now && d.Source!=null && Publications.Official(d.Source))
        .OrderBy(d=>asia?d.AsiaStart:d.EuropeStart).FirstOrDefault();
    }
    public static DateTimeOffset NextRefresh(BannerState state,DateTimeOffset now,bool asia) {
      if(state.EventLookupFailed)return now.AddMinutes(5);
      return now.AddMinutes(!string.IsNullOrEmpty(state.ArtError) || Next(state,now,asia)==null?30:60);
    }
    Task<object> Read(string url){return SharedRead(url);}
    async Task<object> ReadNetwork(string url) {
      using(var response=await http.GetAsync(url).ConfigureAwait(false)) {
        int status=(int)response.StatusCode;
        if(!response.IsSuccessStatusCode)throw new BulletinException(status==429?"rate-limit":status>=500?"server":"source",status);
        object value=Json.Decode(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        if(Json.Num(value,"code")!=0)throw new BulletinException("source",status);
        var requested=Regex.Match(url,@"/api/bulletin/(?<cid>\d+)\?");
        if(requested.Success && Json.At(value,"data","cid")!=null && Json.Str(value,"data","cid").TrimStart('0')!=requested.Groups["cid"].Value.TrimStart('0'))throw new FormatException("The official announcement ID does not match the requested publication.");
        if(!Publications.Native && Publications.Locale!="en-us" && Regex.IsMatch(url,@"/api/bulletin/\d+\?")) {
          string localizedUrl=url.Replace("lang=en-us","lang="+Publications.Locale);
          using(var translated=await http.GetAsync(localizedUrl).ConfigureAwait(false)) {
            if(!translated.IsSuccessStatusCode)throw new BulletinException("translation",(int)translated.StatusCode);
            var localized=Json.Decode(await translated.Content.ReadAsStringAsync().ConfigureAwait(false));
            if(Json.Num(localized,"code")!=0)throw new BulletinException("translation",200);
            value=LocalizedArticles.Project(value,localized);
          }
        }
        return value;
      }
    }
    public async Task<List<Dictionary<string,object>>> Archive() {
      return await InPublication(()=>CachedArchive()).ConfigureAwait(false);
    }
    public static BannerNotice ParseNotice(string html,string title,string cid,long published) {
      if(!IsDebutTitle(title))return null;
      title=ArticleLanguage.Title(title);string text=NewsClient.StructuredText(html),name=FeaturedName(text);
      if(name==null)throw new FormatException("Не вдалося визначити дебютного оперативника.");
      int availabilityIndex=text.IndexOf("Availability",StringComparison.OrdinalIgnoreCase);
      if(availabilityIndex<0)throw new FormatException("Не вдалося прочитати початок банера.");
      string timeBlock=text.Substring(availabilityIndex,Math.Min(2200,text.Length-availabilityIndex));
      int participationIndex=timeBlock.IndexOf("Participation Criteria",StringComparison.OrdinalIgnoreCase);
      if(participationIndex>=0)timeBlock=timeBlock.Substring(0,participationIndex);
      timeBlock=Regex.Replace(timeBlock,@"\s+"," ");
      var availability=Regex.Match(timeBlock,@"(?:Americas\s*/\s*Europe|Europe)\s*(?:Server)?\s*:\s*(?<start>[^–—]+?)\s*[–—]",RegexOptions.IgnoreCase);
      if(!availability.Success)availability=Regex.Match(timeBlock,@"Availability\s*:\s*(?<start>[^–—]+?)\s*[–—]",RegexOptions.IgnoreCase);
      if(!availability.Success)throw new FormatException("Не вдалося прочитати початок банера.");
      string start=availability.Groups["start"].Value.Trim();
      var asiaStart=Regex.Match(timeBlock,@"Asia\s*(?:Server)?\s*:\s*(?<start>[^–—]+?)\s*[–—]",RegexOptions.IgnoreCase);
      var release=Regex.Match(start,@"After\s*\[([^\]]+)\]\s*version\s*(?:update|release)",RegexOptions.IgnoreCase);
      var banner=Regex.Match(title,@"\[([^\]]+)\]");
      return new BannerNotice {Cid=cid,Name=name,Banner=banner.Success?banner.Groups[1].Value:title,Published=published,ReleaseVersion=release.Success?release.Groups[1].Value:null,LocalStart=release.Success?null:start,AsiaLocalStart=release.Success?null:(asiaStart.Success?asiaStart.Groups["start"].Value.Trim():start),EndChecked=true,LocalEnd=AvailabilityEnd(timeBlock,false),AsiaLocalEnd=AvailabilityEnd(timeBlock,true)};
    }
    internal static string FeaturedName(string text) {
      var match=Regex.Match(text,@"(?:Rate[- ]UP\s+6[★*]\s+operator\s*\[([^\]]+)\]|6[★*]\s+operator\s*\[([^\]]+)\]\s+is\s+Rate[- ]UP)",RegexOptions.IgnoreCase);
      if(match.Success)return (match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value).Trim();
      match=Regex.Match(text,@"оперативник[а-яё]*\s*6[★*]\s*\[([^\]]+)\][^\r\n.!]{0,80}(?:высок|повыш)",RegexOptions.IgnoreCase);
      if(match.Success)return match.Groups[1].Value.Trim();
      match=Regex.Match(text,@"(?:6[★*]\s+оперативник[а-яё]*|оперативник[а-яё]*\s*6[★*])\s+с\s+(?:высоким|повышенным)\s+шансом\s*(?:выпадения\s*)?[—–-]?\s*\[([^\]]+)\]",RegexOptions.IgnoreCase);
      if(match.Success)return match.Groups[1].Value.Trim();
      match=Regex.Match(text,@"(?:повышен\s+шанс\s+получить|высокий\s+шанс\s+получить)\s*6[★*]\s+оперативник[а-яё]*\s+(?:\[([^\]]+)\]|([^\r\n.!:,]+))",RegexOptions.IgnoreCase);
      if(match.Success)return (match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value).Trim();
      match=Regex.Match(text,@"6[★*]\s+оперативник[а-яё]*\s+с\s+(?:высоким|повышенным)\s+шансом\s+(?!получения|появления|представленный|указанный)(?:\[([^\]]+)\]|([^\r\n.!:,]+))",RegexOptions.IgnoreCase);
      if(match.Success)return (match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value).Trim();
      match=Regex.Match(text,@"(?:(?:шести|6[- ]?)зв[её]здн[а-яё]*\s+оперативник[а-яё]*|оперативник[а-яё]*\s*6[★*])\s+(?:\[([^\]]+)\]|([^\r\n.!]+?))\s+(?:появляется|с\s+(?:высоким|повышенным)\s+шансом)",RegexOptions.IgnoreCase);
      if(match.Success)return (match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value).Trim();
      return null;
    }
    async Task<BannerNotice> GetNotice(Dictionary<string,object> article) {
      string cid=Json.Str(article,"cid");
      object value=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);
      return ParseNotice(Json.Str(value,"data","data"),FetchedTitle(value,article),cid,Json.Num(article,"displayTime"));
    }
    static List<BannerArt> SeedCatalogue() {
      var seed=BannerState.Seed();var items=seed.Debuts.Select(d=>new BannerArt {Name=d.Name,Key=d.Key,Portrait=d.Portrait}).ToList();
      items.Add(new BannerArt {Name="Серебристый Поток",Key="argent-flow",Portrait="https://web-static.hg-cdn.com/endfield/official-v4/_next/static/media/minghe.372272bd.png"});return items;
    }
    static BannerArt MatchArt(IEnumerable<BannerArt> catalogue,string name){return catalogue.FirstOrDefault(a=>a.Name.Equals(name,StringComparison.OrdinalIgnoreCase) || a.Key.Equals(Regex.Replace(name,@"\s+","-"),StringComparison.OrdinalIgnoreCase));}
    public static List<BannerArt> ParseCatalogue(string script) {
      var map=Regex.Match(script,@"\bportrait\s*:\s*(?<map>[\w$]+)\[[^\]]+\.key\]");
      if(!map.Success)return new List<BannerArt>();
      var table=Regex.Match(script,@"(?<![\w$])"+Regex.Escape(map.Groups["map"].Value)+@"\s*=\s*\{(?<body>[^}]+)\}");
      if(!table.Success)return new List<BannerArt>();
      var urls=new Dictionary<string,string>();
      foreach(Match m in Regex.Matches(table.Groups["body"].Value,@"(?<key>[A-Za-z0-9_-]+):""(?<url>https://[^""]+)"""))urls[m.Groups["key"].Value]=m.Groups["url"].Value;
      var results=new List<BannerArt>();
      foreach(Match m in Regex.Matches(script,@"\{""key"":""(?<key>[^""]+)"",""codename"":""(?<name>[^""]+)""")) {
        string url,key=m.Groups["key"].Value;
        if(urls.TryGetValue(key,out url) && IsOfficialImage(url))results.Add(new BannerArt {Key=key,Name=m.Groups["name"].Value,Portrait=url});
      }
      return results;
    }
    public static bool IsOfficialImage(string url) {
      Uri uri;return Uri.TryCreate(url,UriKind.Absolute,out uri) && uri.Scheme=="https" && uri.Host=="web-static.hg-cdn.com";
    }
    public static List<BannerArt> ParseLocalizedCatalogue(string html) {
      var result=new List<BannerArt>();
      foreach(Match match in Regex.Matches(html,@"<div[^>]*class=""[^""]*OperatorItem_image[^""]*""[^>]*data-key=""(?<key>[A-Za-z0-9_-]+)""[^>]*style=""[^""]*background-image:url\((?<url>https://[^)]+)\)[^""]*""[^>]*>(?:(?!OperatorItem_image).)*?<span[^>]*class=""[^""]*OperatorItem_nameText[^""]*""[^>]*>(?<name>.*?)</span>",RegexOptions.Singleline|RegexOptions.IgnoreCase)) {
        string url=System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value),name=NewsClient.PlainText(match.Groups["name"].Value).Trim();
        if(IsOfficialImage(url) && name.Length>0 && name.Length<=120)result.Add(new BannerArt{Key=match.Groups["key"].Value,Name=name,Portrait=url});
      }
      return result.GroupBy(a=>a.Key,StringComparer.Ordinal).Select(g=>g.First()).ToList();
    }
    async Task<List<BannerArt>> DiscoverCatalogue() {
      string html=await http.GetStringAsync("https://endfield.gryphline.com/"+Publications.Locale+"/operator").ConfigureAwait(false);
      var localized=ParseLocalizedCatalogue(html);if(localized.Count>0)return localized;
      var urls=Regex.Matches(html,@"<script[^>]+src=""([^""]+)""",RegexOptions.IgnoreCase).Cast<Match>()
        .Select(m=>m.Groups[1].Value).Where(u=>IsOfficialImage(u) && Regex.IsMatch(u,@"/chunks/[^/]+\.js$") && !Regex.IsMatch(u,@"webpack|polyfills|main-app|app-pages")).Distinct().Take(20);
      foreach(string url in urls) {
        string script=await http.GetStringAsync(url).ConfigureAwait(false);
        if(!script.Contains("codename"))continue;
        var catalog=ParseCatalogue(script);if(catalog.Count>0)return catalog;
      }
      throw new FormatException("Офіційний каталог портретів має новий формат.");
    }
    static string ImageFilename(string url) {
      using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-","").Substring(0,24)+".png";
    }
    public static string ImagePath(Debut debut) {return Path.Combine(Storage.DirectoryPath,"art",ImageFilename(debut.Portrait));}
    public static bool HasEmbedded(Debut debut) {return debut!=null && Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("Art."+debut.Key+"-portrait");}
    public static Stream OpenPortrait(Debut debut) {
      if(HasEmbedded(debut))return Assembly.GetExecutingAssembly().GetManifestResourceStream("Art."+debut.Key+"-portrait");
      return File.OpenRead(ImagePath(debut));
    }
    async Task EnsurePortrait(Debut debut) {
      if(HasEmbedded(debut))return;
      if(!IsOfficialImage(debut.Portrait))throw new FormatException("Немає перевіреного офіційного портрета.");
      string path=ImagePath(debut);if(PortraitData.ValidFile(path))return;
      await DownloadPortrait(debut,path).ConfigureAwait(false);
    }
    public async Task<BannerState> Fetch(BannerState previous,Schedule currentSchedule,bool asia) {return await InPublication(()=>FetchCore(previous,currentSchedule,asia)).ConfigureAwait(false);}
    public async Task<BannerState> FetchCore(BannerState previous,Schedule currentSchedule,bool asia) {
      var archive=await Archive().ConfigureAwait(false);
      var notices=new List<BannerNotice>();
      foreach(var article in archive.Where(a=>IsDebutTitle(Json.Str(a,"title")))) {
        string cid=Json.Str(article,"cid");
        var cached=previous!=null && previous.Notices!=null ? previous.Notices.FirstOrDefault(n=>!n.Overview && n.Cid==cid):null;
        // Refresh recent announcements, which can still receive time corrections.
        bool recent=Json.Num(article,"displayTime")>DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeSeconds()
          || (previous!=null && previous.Debuts!=null && previous.Debuts.Any(d=>d.Source==NewsClient.NewsUrl+"/"+cid && (asia?d.AsiaStart:d.EuropeStart)>DateTimeOffset.UtcNow));
        notices.Add(cached!=null && cached.EndChecked && !recent?cached:await GetNotice(article).ConfigureAwait(false));
      }
      var gameEvents=new List<GameEvent>();bool eventLookupFailed=false;
      await AddOverviews(archive,previous,notices,gameEvents,()=>eventLookupFailed=true).ConfigureAwait(false);
      await AddGameEventArticles(archive,previous,gameEvents,()=>eventLookupFailed=true).ConfigureAwait(false);
      notices=notices.Where(n=>n!=null).ToList();if(notices.Count==0)throw new FormatException("Немає перевірених дебютних банерів.");
      var releases=new Dictionary<string,DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
      if(currentSchedule!=null && !string.IsNullOrWhiteSpace(currentSchedule.Version))releases[currentSchedule.Version]=currentSchedule.End;
      var catalog=CachedData.Portraits();if(catalog==null || catalog.Count==0)catalog=SeedCatalogue();
      foreach(var item in catalog)item.Key=ArtKey(item.Key);
      var state=new BannerState {Notices=notices,Debuts=new List<Debut>(),GameEvents=GameEventDates.Latest(gameEvents),EventLookupFailed=eventLookupFailed,Checked=DateTimeOffset.UtcNow};
      // Older notices establish whether an operator has already debuted. Only the
      // most recent debuts need a schedule and image for background rotation.
      foreach(var notice in FirstDebuts(notices).OrderByDescending(n=>n.Published).Take(4)) {
        DateTimeOffset europe,asian;
        if(!string.IsNullOrEmpty(notice.ReleaseVersion)) {
          DateTimeOffset release;
          if(!releases.TryGetValue(notice.ReleaseVersion,out release)) {
            bool matched=false;release=DateTimeOffset.MinValue;
            foreach(var article in archive.Where(a=>(IsOverviewTitle(Json.Str(a,"title")) || ArticleLanguage.MaintenanceTitle(Json.Str(a,"title"))) && (Publications.Native?ArticleLanguage.Title(Json.Str(a,"title")).StartsWith("["+notice.ReleaseVersion+"]",StringComparison.OrdinalIgnoreCase):Json.Num(a,"displayTime")<=notice.Published+86400 && Json.Num(a,"displayTime")>=notice.Published-86400*45))) {
              string cid=Json.Str(article,"cid");object value=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);Schedule candidate;
              try{candidate=NewsClient.ParseSchedule(Json.Str(value,"data","data"),FetchedTitle(value,article),NewsClient.NewsUrl+"/"+cid,DateTimeOffset.FromUnixTimeSeconds(Json.Num(article,"displayTime")));}catch(FormatException){continue;}
              if(!string.Equals(candidate.Version,notice.ReleaseVersion,StringComparison.OrdinalIgnoreCase))continue;
              release=candidate.End;matched=true;break;
            }
            if(!matched)throw new FormatException("Не знайдено офіційного часу старту банера.");
            releases[notice.ReleaseVersion]=release;
          }
          europe=asian=release;
        } else {
          var publication=DateTimeOffset.FromUnixTimeSeconds(notice.Published);
          europe=OfficialDates.Published(notice.LocalStart,TimeSpan.FromHours(-5),publication).ToUniversalTime();
          asian=OfficialDates.Published(string.IsNullOrEmpty(notice.AsiaLocalStart)?notice.LocalStart:notice.AsiaLocalStart,TimeSpan.FromHours(8),publication).ToUniversalTime();
        }
        var art=MatchArt(catalog,notice.Name);
        int endYear=DateTimeOffset.FromUnixTimeSeconds(notice.Published).Year;
        state.Debuts.Add(new Debut {Name=notice.Name,Banner=notice.Banner,Source=NewsClient.NewsUrl+"/"+notice.Cid,EuropeStart=europe,AsiaStart=asian,EuropeEndUnix=ResolveEnd(notice.LocalEnd,europe,false,endYear),AsiaEndUnix=ResolveEnd(notice.AsiaLocalEnd,asian,true,endYear),Key=art==null?null:art.Key,Portrait=art==null?null:art.Portrait});
      }
      DateTimeOffset now=DateTimeOffset.UtcNow;
      var relevant=new List<Debut>();var selected=Select(state,now,asia);if(selected!=null)relevant.Add(selected);
      var next=state.Debuts.Where(d=>(asia?d.AsiaStart:d.EuropeStart)>now).OrderBy(d=>asia?d.AsiaStart:d.EuropeStart).FirstOrDefault();if(next!=null)relevant.Add(next);
      if(relevant.Count==0)throw new FormatException("Поточний дебютний оперативник ще не визначений.");
      if(relevant.Any(d=>string.IsNullOrEmpty(d.Portrait))) {
        try {
          catalog=await DiscoverCatalogue().ConfigureAwait(false);foreach(var item in catalog){item.Name=Publications.DisplayText(item.Name);item.Key=ArtKey(item.Key);}Storage.Write(Publications.Cache("portraits"),catalog);
          foreach(var debut in state.Debuts) {var art=MatchArt(catalog,debut.Name);if(art!=null){debut.Key=art.Key;debut.Portrait=art.Portrait;}}
        } catch {state.ArtError=L.T("Portrait unavailable · previous artwork kept");}
      }
      foreach(var debut in relevant)try{await EnsurePortrait(debut).ConfigureAwait(false);}catch{state.ArtError=L.T("Portrait unavailable · previous artwork kept");}
      return state;
    }
  }
}
