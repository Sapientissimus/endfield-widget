using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace EndfieldWidget {
  static class Json {
    public static string Encode(object value) { return new JavaScriptSerializer { MaxJsonLength = 16000000 }.Serialize(value); }
    public static object Decode(string text) { return new JavaScriptSerializer { MaxJsonLength = 16000000 }.DeserializeObject(text); }
    public static object At(object value, params string[] keys) {
      foreach (string key in keys) { var dict = value as Dictionary<string, object>; if (dict == null || !dict.TryGetValue(key, out value)) return null; }
      return value;
    }
    public static string Str(object value, params string[] keys) { return Convert.ToString(At(value, keys), CultureInfo.InvariantCulture); }
    public static long Num(object value, params string[] keys) { long n; return long.TryParse(Str(value, keys), out n) ? n : -1; }
    public static IEnumerable<object> Items(object value) { var list = value as IEnumerable; if (list == null || value is string || value is IDictionary) yield break; foreach (object item in list) yield return item; }
  }

  public class Schedule {
    public bool IsUpcomingOrInProgress(DateTimeOffset now) {
      return !string.IsNullOrWhiteSpace(Version) && Start!=DateTimeOffset.MinValue && End>Start && now<End;
    }
    public string Version { get; set; }
    public string Source { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public DateTimeOffset Checked { get; set; }
    public long PublishedUnix {get;set;}
    public bool Bundled { get; set; }
    public static Schedule Seed() {
      return new Schedule { Version = Publications.Locale=="ru-ru"?"Нарисованный приют":Publications.Locale=="en-us"?"Sanctuary of Ink":"", Source = Publications.NewsUrl+"/0744", Start = new DateTimeOffset(2026,10,14,22,0,0,TimeSpan.Zero), End = new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero), Checked = new DateTimeOffset(2026,10,14,0,0,0,TimeSpan.Zero), Bundled=true };
    }
  }

  public class Sanity {
    public int Current { get; set; }
    public int Maximum { get; set; }
    public long FullAt { get; set; }
    public long ServerTime { get; set; }
    public DateTimeOffset Checked { get; set; }
    public string Nickname { get; set; }
    public string RoleId { get; set; }
    public int Estimate(DateTimeOffset now) {
      // A reading is retained during network failures. Estimates expire after 15 minutes.
      if (Current >= Maximum || now < Checked || (now-Checked).TotalMinutes > 15 || FullAt <= 0) return Current;
      int missing = Math.Max(0, Maximum-Current);
      double observedRemaining = (double)FullAt-ServerTime;
      if (observedRemaining <= 0 || missing <= 0) return Current;
      double secondsPerPoint = observedRemaining/(double)missing;
      return (int)Math.Min(Maximum,Current+Math.Floor((now-Checked).TotalSeconds/secondsPerPoint));
    }
    public DateTimeOffset? LocalFullAt() {
      if (FullAt <= 0 || ServerTime <= 0) return null;
      double seconds=(double)FullAt-ServerTime;
      if(seconds>TimeSpan.MaxValue.TotalSeconds || seconds<TimeSpan.MinValue.TotalSeconds)return null;
      try{return Checked.AddSeconds(seconds);}catch(ArgumentOutOfRangeException){return null;}
    }
  }

  public partial class Config {
    public string InterfaceLanguage {get;set;}
    public Config() { PauseInFullscreen=true;ShowPlayButton=true;ShowClock=true;ShowNextDebut=true;ShowUpdates=true;PortraitOpacity=1;ShowWatermark=true;SanityThreshold=90;SanityLeadMinutes=30;MaintenanceLeadMinutes=30;SanityNoticeMode="percent";StudioDefaults(); }
    public string ProtectedToken { get; set; }
    public string RoleId { get; set; }
    public bool Topmost { get; set; }
    public bool PositionLocked {get;set;}
    public double Left { get; set; }
    public double Top { get; set; }
    public bool HasPosition { get; set; }
    public string BannerRegion {get;set;}
    public double Scale {get;set;}
    public double CompactScale {get;set;}
    public void NormalizeScales() {
      Scale=Placement.Finite(Scale) && Scale>0?Placement.Clamp(Scale,.75,1.30):1;
      CompactScale=Placement.Finite(CompactScale) && CompactScale>0?Placement.Clamp(CompactScale,.75,1.30):Scale;
    }
    public double BackgroundOpacity {get;set;}
    public double PortraitOpacity {get;set;}
    public bool ShowWatermark {get;set;}
    public bool Compact {get;set;}
    public bool ClickThrough {get;set;}
    public bool ShowPlayButton {get;set;}
    public bool ShowClock {get;set;}
    public bool ShowNextDebut {get;set;}
    public bool ShowUpdates {get;set;}
    public string SanityNoticeMode {get;set;}
    public int SanityThreshold {get;set;}
    public int SanityLeadMinutes {get;set;}
    public int MaintenanceLeadMinutes {get;set;}
    public void NormalizePreferences() {
      PortraitOpacity=Placement.Finite(PortraitOpacity)?Placement.Clamp(PortraitOpacity,0,1):1;
      if(SanityNoticeMode!="minutes")SanityNoticeMode="percent";
      SanityThreshold=Math.Max(50,Math.Min(100,SanityThreshold<=0?90:SanityThreshold));
      SanityLeadMinutes=Math.Max(5,Math.Min(240,SanityLeadMinutes<=0?30:SanityLeadMinutes));
      MaintenanceLeadMinutes=Math.Max(5,Math.Min(180,MaintenanceLeadMinutes<=0?30:MaintenanceLeadMinutes));
    }
    public bool NotifySanity {get;set;}
    public bool NotifyMaintenance {get;set;}
    public bool PauseInFullscreen {get;set;}
    public string MonitorId {get;set;}
    public double MonitorX {get;set;}
    public double MonitorY {get;set;}
  }

  static partial class Storage {
    public static bool Isolated;
    public static string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EndfieldWidget");
    public static T Read<T>(string name) where T : class {
      if(name=="settings.json" && typeof(T)==typeof(Config))return ReadSettings() as T;
      try { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(Path.Combine(DirectoryPath,name),Encoding.UTF8)); } catch { return null; }
    }
    public static void Write(string name, object value) {
      lock(writeLock) {
        Directory.CreateDirectory(DirectoryPath);
        if(name=="settings.json" && value is Config){WriteSettings((Config)value);return;}
        AtomicWrite(Path.Combine(DirectoryPath,name),Json.Encode(value));
      }
    }
    public static string Protect(string token) {
      return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token),null,DataProtectionScope.CurrentUser));
    }
    public static string Unprotect(string text) {
      try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text),null,DataProtectionScope.CurrentUser)); } catch { return ""; }
    }
  }

  static class Clock {
    // Read the Windows local zone; fixtures may supply a zone without changing Windows.
    public static Func<TimeZoneInfo> ZoneOverride;
    public static DateTimeOffset Local(DateTimeOffset time) { return TimeZoneInfo.ConvertTime(time,ZoneOverride==null?TimeZoneInfo.Local:ZoneOverride()); }
    public static void RefreshSystemZone(){TimeZoneInfo.ClearCachedData();}
    public static string Date(DateTimeOffset time) { return Local(time).ToString(L.Code=="uk"?"dd.MM.yyyy":L.Culture.DateTimeFormat.ShortDatePattern,L.Culture); }
    public static string Time(DateTimeOffset time) { return Local(time).ToString("HH:mm",CultureInfo.InvariantCulture); }
    public static string Remaining(TimeSpan span) {
      return Duration(span,false);
    }
    public static string After(TimeSpan span){return Duration(span,true);}
    public static string Unit(int value,string one,string few,string many) {
      return L.Unit(value,one,few,many);
    }
    static string Duration(TimeSpan span,bool accusative) {
      if(span<TimeSpan.Zero)span=TimeSpan.Zero;
      Func<int,string,string,string,string> part=(n,one,few,many)=>n+" "+Unit(n,one,few,many);
      if(span.TotalDays>=1)return part((int)span.TotalDays,L.T("day"),L.T("duration.day.few"),L.T("duration.day.many"))+(span.Hours>0?" "+part(span.Hours,accusative?L.T("duration.hour.accusative"):L.T("duration.hour.nominative"),L.T("duration.hour.few"),L.T("duration.hour.many")):"");
      if(span.TotalHours>=1)return part((int)span.TotalHours,accusative?L.T("duration.hour.accusative"):L.T("duration.hour.nominative"),L.T("duration.hour.few"),L.T("duration.hour.many"))+(span.Minutes>0?" "+part(span.Minutes,accusative?L.T("duration.minute.accusative"):L.T("duration.minute.nominative"),L.T("duration.minute.few"),L.T("duration.minute.many")):"");
      return part(Math.Max(1,(int)Math.Ceiling(span.TotalMinutes)),accusative?L.T("duration.minute.accusative"):L.T("duration.minute.nominative"),L.T("duration.minute.few"),L.T("duration.minute.many"));
    }
  }

  class NewsClient {
    public static string NewsUrl {get{return Publications.NewsUrl;}}
    readonly BannerClient publications;
    public NewsClient(HttpClient client):this(client,null){}
    public NewsClient(HttpClient client,BannerClient shared) {publications=shared??new BannerClient(client);}
    public static IEnumerable<string> FlightChunks(string html) {
      foreach (Match m in Regex.Matches(html, @"self\.__next_f\.push\(\[\d+,""((?:\\.|[^""\\])*)""\]\)")) {
        string decoded; try { decoded = (string)Json.Decode("\""+m.Groups[1].Value+"\""); } catch { continue; }
        yield return decoded;
      }
    }
    public static List<Dictionary<string,object>> Articles(string html) {
      var found = new Dictionary<string,Dictionary<string,object>>();
      foreach (string chunk in FlightChunks(html)) {
        foreach (Match m in Regex.Matches(chunk,@"\{""cid""\s*:\s*""[^""]+""[^{}]*\}")) {
          try {
            var article=Json.Decode(m.Value) as Dictionary<string,object>;
            if (article != null && Json.Str(article,"title").Length>0) found[Json.Str(article,"cid")]=article;
          } catch { }
        }
      }
      return found.Values.OrderByDescending(a=>Json.Num(a,"displayTime")).ToList();
    }
    public static string PlainText(string html) {
      // Only rendered HTML is read, excluding scripts (which repeat escaped copies of the article).
      html=Regex.Replace(html,@"<script\b[^>]*>.*?</script>","",RegexOptions.Singleline|RegexOptions.IgnoreCase);
      html=Regex.Replace(html,@"</(?:p|div|li|h[1-6])>|<br\s*/?>","\n",RegexOptions.IgnoreCase);
      html=Regex.Replace(html,@"<[^>]*>"," ");
      html=WebUtility.HtmlDecode(html).Replace('\u00a0',' ');
      return Regex.Replace(html,@"[\t ]+"," ");
    }
    public static string StructuredText(string html){return ArticleLanguage.Text(PlainText(html),true);}
    public static DateTimeOffset ParseDate(string text, TimeSpan offset, int fallbackYear) {
      text=ArticleLanguage.Dates(text);
      var m=Regex.Match(text,OfficialDates.Token,RegexOptions.IgnoreCase);
      if (!m.Success) throw new FormatException(L.T("The announcement uses an unfamiliar date format."));
      string monthName=m.Groups["month"].Value.Substring(0,3);
      int month=DateTime.ParseExact(monthName,"MMM",CultureInfo.InvariantCulture).Month;
      int year=m.Groups["year"].Success ? int.Parse(m.Groups["year"].Value) : fallbackYear;
      return new DateTimeOffset(year,month,int.Parse(m.Groups["day"].Value),int.Parse(m.Groups["hour"].Value),int.Parse(m.Groups["minute"].Value),0,offset);
    }
    public static Schedule ParseSchedule(string html,string title,string url,int fallbackYear){return ParseScheduleCore(html,title,url,fallbackYear,null);}
    public static Schedule ParseSchedule(string html,string title,string url,DateTimeOffset published){return ParseScheduleCore(html,title,url,published.Year,published);}
    static Schedule ParseScheduleCore(string html,string title,string url,int fallbackYear,DateTimeOffset? published) {
      string text=StructuredText(html);title=ArticleLanguage.Title(title);
      // Prefer Europe/Americas. Fall back only to explicitly dated Asia schedules.
      var m=Regex.Match(text,@"(?:Americas\s*/\s*Europe|Europe\s*/\s*Americas|Europe|Asia)\s+Server\s*:\s*(?<start>[^\n]+?)\s*[–—]\s*(?<end>[^\n]+?)\s*\(UTC\s*(?<offset>[+-]\d{1,2})(?::(?<mins>\d{2}))?\)",RegexOptions.IgnoreCase);
      if (!m.Success) throw new FormatException(L.T("The exact maintenance schedule has not been published yet."));
      // If both regions exist, select Europe rather than the first (Asia) match.
      foreach (Match candidate in Regex.Matches(text,@"(?:Americas\s*/\s*Europe|Europe\s*/\s*Americas|Europe)\s+Server\s*:\s*(?<start>[^\n]+?)\s*[–—]\s*(?<end>[^\n]+?)\s*\(UTC\s*(?<offset>[+-]\d{1,2})(?::(?<mins>\d{2}))?\)",RegexOptions.IgnoreCase)) { m=candidate; break; }
      int hours=int.Parse(m.Groups["offset"].Value), minutes=m.Groups["mins"].Success ? int.Parse(m.Groups["mins"].Value):0;
      if (m.Groups["offset"].Value.StartsWith("-")) minutes=-minutes;
      TimeSpan offset=new TimeSpan(hours,minutes,0);
      DateTimeOffset start=published.HasValue?OfficialDates.Published(m.Groups["start"].Value,offset,published.Value):ParseDate(m.Groups["start"].Value,offset,fallbackYear);
      DateTimeOffset end=OfficialDates.End(m.Groups["end"].Value,offset,start);
      if (end<=start || (end-start).TotalDays>3) throw new FormatException(L.T("Could not verify the maintenance duration."));
      var versionMatch=Regex.Match(title,@"\[([^\]]+)\]");
      return new Schedule { Version=versionMatch.Success ? versionMatch.Groups[1].Value:title, Source=url, Start=start.ToUniversalTime(), End=end.ToUniversalTime(), Checked=DateTimeOffset.UtcNow };
    }
    public async Task<Schedule> Fetch() {return await publications.FetchSchedule().ConfigureAwait(false); }
  }

  class GameRole {
    public string RoleId, ServerId, UserId, Name;
    public override string ToString() { return Name+" · "+RoleId; }
  }

  class SkportClient {
    readonly HttpClient http;
    string cred="", salt="", userId="";
    string sessionAccountToken="";
    long serverTimeOffset;
    DateTimeOffset authenticated=DateTimeOffset.MinValue;
    public List<GameRole> CachedRoles {get;private set;}
    public SkportClient(HttpClient client) { http=client; }
    static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant(); }
    public static string Sign(string path,string content,string timestamp,string token) {
      string headers="{\"platform\":\"3\",\"timestamp\":\""+timestamp+"\",\"dId\":\"\",\"vName\":\"1.0.0\"}";
      using(var hmac=new HMACSHA256(Encoding.UTF8.GetBytes(token)))
      using(var md5=MD5.Create()) return Hex(md5.ComputeHash(Encoding.UTF8.GetBytes(Hex(hmac.ComputeHash(Encoding.UTF8.GetBytes(path+content+timestamp+headers))))));
    }
    public static string NormalizeToken(string text) {
      text=(text??"").Trim();
      if(text.StartsWith("{")) {
        try{text=Json.Str(Json.Decode(text),"data","content").Trim();}catch{return "";}
      } else if(text.Length>=2 && text.StartsWith("\"") && text.EndsWith("\"")) {
        try{text=(Json.Decode(text) as string)??"";}catch{return "";}
      }
      var m=Regex.Match(text,@"(?:^|[;\s])ACCOUNT_TOKEN=([^;\s]+)",RegexOptions.IgnoreCase);
      if (m.Success) text=m.Groups[1].Value;
      try{return Uri.UnescapeDataString(text).Trim('\uFEFF','\u200B',' ','\r','\n','\t');}catch{return "";}
    }
    static SkportStage Stage(string path) {
      if(path.EndsWith("/grant",StringComparison.Ordinal))return SkportStage.Login;
      if(path.EndsWith("generate_cred_by_code",StringComparison.Ordinal))return SkportStage.Session;
      if(path.EndsWith("/auth/refresh",StringComparison.Ordinal))return SkportStage.Refresh;
      if(path.EndsWith("/v2/user",StringComparison.Ordinal))return SkportStage.Profile;
      if(path.EndsWith("/binding",StringComparison.Ordinal))return SkportStage.Characters;
      return SkportStage.Sanity;
    }
    static long ResponseCode(object result) {
      object value=Json.At(result,"code")??Json.At(result,"status");long code;
      return value!=null && long.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),out code)?code:-1;
    }
    bool AdjustTime(object result) {
      long timestamp=Json.Num(result,"timestamp");if(timestamp>100000000000)timestamp/=1000;
      if(timestamp<1000000000 || timestamp>4102444800)return false;
      serverTimeOffset=timestamp-DateTimeOffset.UtcNow.ToUnixTimeSeconds();return true;
    }
    async Task RefreshSession() {
      object refreshed=await Request("https://zonai.skport.com","/web/v1/auth/refresh","",null,false,null).ConfigureAwait(false);
      string next=Json.Str(refreshed,"data","token");
      if(next.Length==0)throw new SkportException(SkportStage.Refresh,SkportProblem.Response);
      salt=next;AdjustTime(refreshed);
    }
    async Task<object> Request(string host,string path,string query,object body,bool signed,string role) {
      string payload=body==null ? "":Json.Encode(body);
      var stage=Stage(path);bool retriedTime=false,retriedSession=false;
      for(;;) {
      bool refreshSession=false;
      using(var request=new HttpRequestMessage(body==null ? HttpMethod.Get:HttpMethod.Post,host+path+(query.Length>0 ? "?"+query:""))) {
        request.Headers.TryAddWithoutValidation("Accept","application/json");
        request.Headers.TryAddWithoutValidation("platform","3");
        request.Headers.TryAddWithoutValidation("vName","1.0.0");
        request.Headers.TryAddWithoutValidation("sk-language","en_US");
        request.Headers.TryAddWithoutValidation("x-language","en-us");
        bool gameOrigin=signed || stage==SkportStage.Refresh;
        request.Headers.TryAddWithoutValidation("Origin",gameOrigin?"https://game.skport.com":"https://www.skport.com");
        request.Headers.Referrer=new Uri(gameOrigin?"https://game.skport.com/":"https://www.skport.com/");
        if(host=="https://zonai.skport.com" && cred.Length>0)request.Headers.TryAddWithoutValidation("cred",cred);
        if (body!=null) request.Content=new StringContent(payload,Encoding.UTF8,"application/json");
        if(host=="https://zonai.skport.com")request.Headers.TryAddWithoutValidation("timestamp",(DateTimeOffset.UtcNow.ToUnixTimeSeconds()+serverTimeOffset).ToString(CultureInfo.InvariantCulture));
        if (signed) {
          string ts=request.Headers.GetValues("timestamp").Single();
          request.Headers.TryAddWithoutValidation("sign",Sign(path,body==null?query:payload,ts,salt));
          request.Headers.TryAddWithoutValidation("dId","");
          if (!string.IsNullOrEmpty(role)) request.Headers.TryAddWithoutValidation("sk-game-role",role);
        }
        HttpResponseMessage response;
        try{response=await http.SendAsync(request).ConfigureAwait(false);}
        catch(TaskCanceledException){throw new SkportException(stage,SkportProblem.Timeout);}
        catch(HttpRequestException){throw new SkportException(stage,SkportProblem.Network);}
        using(response) {
          object result;
          try {result=Json.Decode(await response.Content.ReadAsStringAsync().ConfigureAwait(false));}
          catch {throw new SkportException(stage,SkportProblem.Response,(int)response.StatusCode);}
          long code=ResponseCode(result);
          if(response.IsSuccessStatusCode && code==0){if(host=="https://zonai.skport.com")AdjustTime(result);return result;}
          if(signed && code==10003 && !retriedTime && AdjustTime(result)){retriedTime=true;continue;}
          if(signed && code==10000 && !retriedSession){retriedSession=true;refreshSession=true;}
          else throw new SkportException(stage,code<0?SkportProblem.Response:SkportProblem.Rejected,(int)response.StatusCode,code);
        }
      }
      if(refreshSession)await RefreshSession().ConfigureAwait(false);
      }
    }
    async Task Authenticate(string accountToken) {
      if(cred.Length>0 && sessionAccountToken==accountToken && (DateTimeOffset.UtcNow-authenticated).TotalMinutes<25)return;
      cred="";salt="";userId="";sessionAccountToken="";authenticated=DateTimeOffset.MinValue;
      object grant=await Request("https://as.gryphline.com","/user/oauth2/v2/grant","",new {token=accountToken,appCode="6eb76d4e13aa36e6",type=0},false,null).ConfigureAwait(false);
      string code=Json.Str(grant,"data","code");
      if(code.Length==0)throw new SkportException(SkportStage.Login,SkportProblem.Response);
      object exchange=await Request("https://zonai.skport.com","/web/v1/user/auth/generate_cred_by_code","",new {code=code,kind=1},false,null).ConfigureAwait(false);
      cred=Json.Str(exchange,"data","cred"); salt=Json.Str(exchange,"data","token"); userId=Json.Str(exchange,"data","userId");
      if(cred.Length==0)throw new SkportException(SkportStage.Session,SkportProblem.Response);
      if(salt.Length==0)await RefreshSession().ConfigureAwait(false);
      if (userId.Length==0) {
        object user=await Request("https://zonai.skport.com","/web/v2/user","",null,true,null).ConfigureAwait(false);
        userId=Json.Str(user,"data","user","basicUser","id");
        if(userId.Length==0)throw new SkportException(SkportStage.Profile,SkportProblem.Response);
      }
      authenticated=DateTimeOffset.UtcNow;sessionAccountToken=accountToken;
    }
    public static List<GameRole> ParseRoles(object result,string fallbackUserId) {
      var roles=new List<GameRole>();
      foreach(var game in Json.Items(Json.At(result,"data","list"))) {
        if (Json.Str(game,"appCode")!="endfield") continue;
        foreach(var binding in Json.Items(Json.At(game,"bindingList"))) {
          string uid=Json.Str(binding,"uid");
          foreach(var item in Json.Items(Json.At(binding,"roles"))) {
            if (Json.At(item,"isBanned") is bool && (bool)Json.At(item,"isBanned")) continue;
            var role=new GameRole {RoleId=Json.Str(item,"roleId"),ServerId=Json.Str(item,"serverId"),UserId=fallbackUserId.Length>0?fallbackUserId:uid,Name=Json.Str(item,"nickname")};
            if (role.RoleId.Length>0 && role.ServerId.Length>0) roles.Add(role);
          }
        }
      }
      return roles;
    }
    public async Task<List<GameRole>> Roles(string accountToken) {
      await Authenticate(accountToken).ConfigureAwait(false);
      object result=await Request("https://zonai.skport.com","/api/v1/game/player/binding","",null,true,null).ConfigureAwait(false);
      var roles=ParseRoles(result,userId);
      if(roles.Count==0)throw new SkportException(SkportStage.Characters,SkportProblem.MissingCharacters);
      CachedRoles=roles;
      return roles;
    }
    public static Sanity ParseSanity(object result, GameRole role, DateTimeOffset now) {
      object dungeon=Json.At(result,"data","detail","dungeon");
      long current=Json.Num(dungeon,"curStamina"), maximum=Json.Num(dungeon,"maxStamina"), full=Json.Num(dungeon,"maxTs");
      if (current<0 || maximum<=0 || maximum>100000 || current>100000) throw new InvalidOperationException(L.T("The SKPORT response has no valid Sanity reading."));
      long timestamp=Json.Num(result,"timestamp");
      if (timestamp>100000000000) timestamp/=1000;
      if (full>100000000000) full/=1000;
      if (timestamp<=0) timestamp=now.ToUnixTimeSeconds();
      return new Sanity {Current=(int)current,Maximum=(int)maximum,FullAt=full,ServerTime=timestamp,Checked=now,RoleId=role.RoleId,Nickname=Json.Str(result,"data","detail","base","name")};
    }
    public async Task<Sanity> Fetch(string accountToken,string preferredRole) {
      var roles=await Roles(accountToken).ConfigureAwait(false);
      var role=roles.FirstOrDefault(r=>r.RoleId==preferredRole)??roles[0];
      string query="roleId="+Uri.EscapeDataString(role.RoleId)+"&serverId="+Uri.EscapeDataString(role.ServerId)+"&userId="+Uri.EscapeDataString(role.UserId);
      object detail=await Request("https://zonai.skport.com","/api/v1/game/endfield/card/detail",query,null,true,"3_"+role.RoleId+"_"+role.ServerId).ConfigureAwait(false);
      return ParseSanity(detail,role,DateTimeOffset.UtcNow);
    }
  }

  partial class Widget {
    readonly Window window;
    readonly Config config;
    readonly HttpClient http;
    NewsClient news;
    BannerClient banners;
    BannerState bannerState;
    string currentArtKey="", bannerError="";
    bool bannersVerified,bannerLookupFailed;
    OperatorTheme currentTheme;
    SkportClient skport;
    Schedule schedule;
    Sanity sanity;
    Forms.NotifyIcon tray;
    DispatcherTimer timer;
    string accountToken="", sanityError="", newsError="";
    bool refreshBusy, closing, preview;
    DesktopSurface desktopSurface;
    DateTimeOffset nextNews=DateTimeOffset.MinValue,nextSanity=DateTimeOffset.MinValue,nextBanner=DateTimeOffset.MinValue;
    List<GameRole> availableRoles;
    readonly Dictionary<string,object> namedElements=new Dictionary<string,object>(StringComparer.Ordinal);
    T Find<T>(string name) where T : class {
      object element;if(!namedElements.TryGetValue(name,out element)){element=window.FindName(name);if(element!=null)namedElements.Add(name,element);}
      return element as T;
    }
    void Text(string name,string value) {var target=Find<TextBlock>(name);if(target.Text!=value){target.Text=value;if(IsCardText(name))cardLayoutDirty=true;}}
    public Widget(bool isPreview) {
      preview=isPreview;
      bannersVerified=preview;
      config=preview ? new Config():(Storage.Read<Config>("settings.json")??new Config());
      config.InterfaceLanguage=L.Normalize(config.InterfaceLanguage);L.Set(config.InterfaceLanguage);
      if (!preview) {accountToken=Storage.Unprotect(config.ProtectedToken);awaitingFreshSanity=!string.IsNullOrEmpty(accountToken);}
      schedule=preview?Schedule.Seed():CachedData.Schedule();
      if(!preview)sanity=CachedData.Sanity(config.RoleId);
      bannerState=preview?BannerState.Seed():CachedData.Banners();
      calendarFeed=preview?null:CalendarFeedStore.Load(bannerState);
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) window=(Window)XamlReader.Load(stream);
      window.Resources.MergedDictionaries.Add(L.Resources);window.Language=XmlLanguage.GetLanguage(L.Culture.Name);
      currentTheme=OperatorTheme.FromAccent(280,0.45,255);currentTheme.Apply(window.Resources);
      http=NewHttp();banners=new BannerClient(http);news=new NewsClient(http,banners);skport=new SkportClient(http);
      window.Topmost=false;
      InitializeExtras();
      InitializeGameLauncher();
      if(!preview)InstallDesktopSurface();
      Find<Button>("CloseButton").Click+=(s,e)=>window.Hide();
      Find<Button>("SettingsButton").Click+=(s,e)=>Settings();
      Find<Button>("RefreshButton").Click+=async(s,e)=>await Refresh(true);
      Find<TextBlock>("VersionLabel").Cursor=System.Windows.Input.Cursors.Hand;
      Find<TextBlock>("VersionLabel").MouseLeftButtonUp+=(s,e)=>Open(schedule.Source);
      Find<TextBlock>("VersionLabel").ToolTip=L.T("Update details");
      Find<TextBlock>("BannerLabel").Cursor=System.Windows.Input.Cursors.Hand;
      Find<TextBlock>("BannerLabel").MouseLeftButtonUp+=(s,e)=>{var debut=BannerClient.Select(bannerState,DateTimeOffset.UtcNow,config.BannerRegion=="Asia");if(debut!=null)Open(debut.Source);};
      window.Closing+=(s,e)=>{ if (!closing) { e.Cancel=true; window.Hide(); } };
      if (!preview) SetupTray();
      Paint();InitializeAppearanceHistory();
    }
    public static HttpClient NewHttp() {
      ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
      var handler=new HttpClientHandler {AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate,UseCookies=false};
      var client=new HttpClient(handler) {Timeout=TimeSpan.FromSeconds(20)};
      client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) EndfieldWidget/1.0");
      return client;
    }
    void SetupTray() {
      tray=new Forms.NotifyIcon {Icon=System.Drawing.SystemIcons.Application,Text="Endfield",Visible=true};
      tray.ContextMenuStrip=CreateTrayMenu();
      tray.DoubleClick+=(s,e)=>ShowWidget();
      tray.MouseMove+=(s,e)=>UpdateTrayStatus(DateTimeOffset.UtcNow);
      UpdateTrayStatus(DateTimeOffset.UtcNow);
    }
    void InstallDesktopSurface(){desktopSurface=new DesktopSurface(window,()=>config.PositionLocked,UserMoved);desktopSurface.SetClickThrough(config.ClickThrough);desktopSurface.SnapPosition=SnapPosition;desktopSurface.BindDrag(Find<Grid>("DragHandle"));desktopSurface.BindDrag(Find<Grid>("CompactDragHandle"));desktopSurface.BindDrag(Find<Grid>("MicroDragHandle"));}
    void ShowWidget(){if(!window.IsVisible)Paint();if(desktopSurface!=null)desktopSurface.Show();else window.Show();UpdateMenu();AdjustBackgroundTimer();}
    void SavePosition() {
      config.RememberMode();TrackAppearanceChange();
      if(!closing && !applyingAppearance && appearanceHistory!=null && appearanceHistory.InGesture)QueueSettingsCommit();else CommitSettings();
      AdjustBackgroundTimer();
    }
    void Exit() {
      closing=true; SavePosition(); DisposeExtras(); if(timer!=null)timer.Stop(); if(desktopSurface!=null)desktopSurface.Dispose();if(tray!=null){var menu=tray.ContextMenuStrip;tray.ContextMenuStrip=null;tray.Dispose();if(menu!=null)menu.Dispose();}DisposeTrayStatus(); http.Dispose(); window.Close(); Application.Current.Shutdown();
    }
    public void Run() {
      window.Show();InitializeBackgroundLoop();UpdateSurfaceState();window.Dispatcher.BeginInvoke(new Action(async()=>await Refresh(true)));
    }
    public void SmokeTest(string reportPath) {
      window.Left=-10000;window.Top=-10000;window.Show();
      window.Dispatcher.BeginInvoke(new Action(async()=>{
        try {
          await Refresh(true);
          if(newsError.Length>0)throw new Exception(newsError);
          if(bannerError.Length>0)throw new Exception(bannerError);
          if(Find<Image>("OperatorImage").Source==null)throw new Exception("Portrait is missing");
          var settings=SettingsWindow();settings.WindowStartupLocation=WindowStartupLocation.Manual;settings.Left=-10000;settings.Top=-10000;settings.Show();settings.UpdateLayout();
          if(settings.Owner!=null)throw new Exception("Settings must remain independent of desktop ownership");
          var original=BannerClient.Select(bannerState,DateTimeOffset.UtcNow,config.BannerRegion=="Asia");
          var alternate=BannerState.Seed().Debuts.First(d=>d.Name!=original.Name);
          var beforeAccent=currentTheme.Accent;ApplyDebut(alternate);settings.UpdateLayout();
          var settingsAccent=((SolidColorBrush)((Button)settings.FindName("ConnectAccountButton")).Background).Color;
          if(beforeAccent==currentTheme.Accent || settingsAccent!=currentTheme.Accent)throw new Exception("Shared automatic palette did not update settings");
          ApplyDebut(original);
          ((Expander)settings.FindName("TokenHelp")).IsExpanded=true;settings.UpdateLayout();
          var scroll=(ScrollViewer)settings.FindName("SettingsScroll");scroll.ScrollToBottom();settings.UpdateLayout();
          if(scroll.ScrollableHeight>0 && scroll.VerticalOffset<=0)throw new Exception("Settings help cannot be scrolled");
          if(string.IsNullOrEmpty(accountToken)) {
            ((Button)settings.FindName("ConnectAccountButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if(((TextBlock)settings.FindName("ConnectionMessage")).Visibility!=Visibility.Visible)throw new Exception("Empty account input has no visible feedback");
          }
          ((Button)settings.FindName("SettingsDoneButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
          if(settings.IsVisible)throw new Exception("Settings close action failed");
          if(!Find<Button>("PlayButton").IsEnabled)throw new Exception("Play button disabled unexpectedly");
          File.WriteAllText(reportPath,"PASS: Application startup, live news and debut banner discovery, portrait loading, shared palette updating both windows, settings help scrolling, account input feedback, opening and closing settings, enabled Steam play button (game not launched).\n",Encoding.UTF8);
        } catch(Exception ex) {File.WriteAllText(reportPath,"FAILED: "+ex.ToString(),Encoding.UTF8);Environment.ExitCode=1;}
        finally{Exit();}
      }));
    }
    public void Capture(string path, bool settings,string artName) {
      if(!string.IsNullOrEmpty(artName)) {
        var forced=BannerState.Seed().Debuts.FirstOrDefault(d=>d.Name.Equals(artName,StringComparison.OrdinalIgnoreCase) || d.Key.Equals(artName,StringComparison.OrdinalIgnoreCase));if(forced!=null)ApplyDebut(forced);
      }
      Window target=settings?SettingsWindow():window;
      target.WindowStartupLocation=WindowStartupLocation.Manual;target.Left=-10000;target.Top=-10000;
      target.Show();target.UpdateLayout();
      target.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
        var bitmap=new RenderTargetBitmap((int)target.ActualWidth,(int)target.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(target);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(path))encoder.Save(stream); closing=true;http.Dispose();target.Close();if(target!=window)window.Close();Application.Current.Shutdown();
      }));
    }
    async Task Refresh(bool force) {
      if(refreshBusy || recovery.Suspended || !recovery.Online)return;refreshBusy=true;Find<Button>("RefreshButton").IsEnabled=false;
      int epoch=recovery.Epoch;if(force)recovery.Pending=false;
      try {
        DateTimeOffset now=DateTimeOffset.UtcNow;
        var operations=new List<Task>();
        if(force || now>=nextNews)operations.Add(RefreshNews());
        if(force || now>=nextSanity)operations.Add(RefreshSanity());
        await Task.WhenAll(operations);
        if(CanApplyResponse(epoch) && (force || now>=nextBanner))await RefreshBanners();
      } finally {
        refreshBusy=false;
        if(!closing && !extrasDisposed) {
          Find<Button>("RefreshButton").IsEnabled=true;
          if(runtimeStarted)recovery.Finished(epoch,DateTimeOffset.UtcNow,!(retrySanity || retryNews || retryBanners));
          UpdateSurfaceState();CheckNotifications();AdjustBackgroundTimer();
        }
      }
    }
    async Task RefreshNews() {
      int epoch=recovery.Epoch;
      nextNews=DateTimeOffset.UtcNow.AddMinutes(30);
      try {var result=await news.Fetch();if(!CanApplyResponse(epoch))return;schedule=result;newsError="";liveNews=true;retryNews=false;try{Storage.Write(Publications.Cache("schedule"),schedule);}catch{}if(calendarDialog!=null)RenderCalendar();}
      catch(Exception ex) {if(!CanApplyResponse(epoch))return;liveNews=false;retryNews=IsTransientFailure(ex);newsError=ex is FormatException ? L.T("Schedule was not updated. Try again."):L.T("Announcements were not updated · check your connection"); nextNews=DateTimeOffset.UtcNow.AddMinutes(5);}
    }
    async Task RefreshSanity() {
      int epoch=recovery.Epoch;
      nextSanity=DateTimeOffset.UtcNow.AddMinutes(5);
      if(string.IsNullOrEmpty(accountToken)){awaitingFreshSanity=false;retrySanity=false;return;}
      try {
        var result=await skport.Fetch(accountToken,config.RoleId);if(!CanApplyResponse(epoch))return;sanity=result;sanityError="";liveSanity=true;awaitingFreshSanity=false;retrySanity=false;
        availableRoles=skport.CachedRoles;
        config.RoleId=sanity.RoleId;SynchronizeAccountSettings();
        try {Storage.Write("sanity.json",sanity);Storage.Write("settings.json",config);}catch{}
        UpdateTrayStatus(DateTimeOffset.UtcNow);
      } catch(Exception ex) {if(!CanApplyResponse(epoch))return;liveSanity=false;retrySanity=IsTransientFailure(ex);sanityError=SkportFailure.Describe(ex);if(!preview)SkportFailure.Record(ex);}
    }
    async Task RefreshBanners() {
      int epoch=recovery.Epoch;
      var calendarUpdate=ApplyCalendarFeed(banners.FetchCalendar(calendarFeed),epoch);
      nextBanner=DateTimeOffset.UtcNow.AddHours(1);
      try {var result=await banners.Fetch(bannerState,schedule,config.BannerRegion=="Asia");if(!CanApplyResponse(epoch))return;bannerState=result;bannersVerified=true;bannerLookupFailed=false;retryBanners=false;bannerError=bannerState.ArtError??"";nextBanner=BannerClient.NextRefresh(bannerState,DateTimeOffset.UtcNow,config.BannerRegion=="Asia");try{Storage.Write(Publications.Cache("banners"),bannerState);}catch{}if(calendarDialog!=null)RenderCalendar();}
      catch(Exception ex) {if(!CanApplyResponse(epoch))return;bannersVerified=false;bannerLookupFailed=true;retryBanners=IsTransientFailure(ex);bannerError=L.T("Artwork was not updated · previous operator kept");nextBanner=DateTimeOffset.UtcNow.AddMinutes(30);}
      await calendarUpdate;if(CanApplyResponse(epoch) && calendarFeed!=null && calendarFeed.Partial && nextBanner>DateTimeOffset.UtcNow.AddMinutes(5))nextBanner=DateTimeOffset.UtcNow.AddMinutes(5);
    }
    void ApplyDebut(Debut debut) {
      if(debut==null || string.IsNullOrEmpty(debut.Key))return;
      debut.Key=BannerClient.ArtKey(debut.Key);
      if(currentArtKey==debut.Key){Text("OperatorName",debut.Name.ToUpper(L.Culture));Text("BannerLabel",debut.Banner);return;}
      try {
        BitmapImage image;using(var stream=BannerClient.OpenPortrait(debut))image=PortraitData.Load(stream);
        currentTheme=OperatorTheme.FromPortrait(image);ApplyDesignPreset();
        ApplyBackgroundOpacity();
        var art=Find<Image>("OperatorImage");art.Source=image;
        currentArtKey=debut.Key;Text("OperatorName",debut.Name.ToUpper(L.Culture));Text("BannerLabel",debut.Banner);
        ApplyPortraitCrop();
        Find<TextBlock>("BannerLabel").ToolTip=L.T("Open banner");
      } catch {bannerError=L.T("Portrait unavailable · previous artwork kept");}
    }
    void PrepareOperatorArt(DateTimeOffset now) {
      ApplyDebut(BannerClient.Select(bannerState,now,config.BannerRegion=="Asia"));
      if(currentArtKey.Length==0)foreach(var fallback in (bannerState.Debuts??new List<Debut>()).Concat(BannerState.Seed().Debuts).Where(d=>(config.BannerRegion=="Asia"?d.AsiaStart:d.EuropeStart)<=now).OrderByDescending(d=>config.BannerRegion=="Asia"?d.AsiaStart:d.EuropeStart)){ApplyDebut(fallback);if(currentArtKey.Length>0)break;}
    }
    void Paint() {
      DateTimeOffset now=DateTimeOffset.UtcNow;PrepareOperatorArt(now);
      if(sanity==null || string.IsNullOrEmpty(accountToken)) {
        Text("SanityNumber","—");Text("SanityMaximum"," / —");Text("SanityPercent","—");Find<ProgressBar>("SanityBar").Value=0;
        Text("AccountLabel",string.IsNullOrEmpty(accountToken)?L.T("SKPORT not connected"):L.T("Connecting to SKPORT…"));
        Text("RecoveryLabel",string.IsNullOrEmpty(accountToken)?L.T("Connect your account in settings"):L.T("Waiting for character data"));
        Text("SanityStatus",FriendlyAccountError(sanityError));
      } else {
        bool stale=awaitingFreshSanity || now<sanity.Checked || (now-sanity.Checked).TotalMinutes>15 || sanityError.Length>0;
        int current=stale?sanity.Current:sanity.Estimate(now);
        Text("SanityNumber",(current!=sanity.Current?"≈":"")+current);Text("SanityMaximum"," / "+sanity.Maximum);
        Find<ProgressBar>("SanityBar").Value=Math.Min(100,current*100.0/sanity.Maximum);
        Text("SanityPercent",Math.Round(current*100.0/sanity.Maximum)+"%");
        Text("AccountLabel",sanity.Nickname);
        var full=sanity.LocalFullAt();
        Text("RecoveryLabel",stale?L.T("Last received reading"):current>=sanity.Maximum?L.T("Sanity is fully restored"):full.HasValue?L.T("Full Sanity recovery in ")+Clock.After(full.Value-now)+"\n"+Clock.Date(full.Value)+L.T(" at ")+Clock.Time(full.Value)+"":L.T("SKPORT provided no recovery time"));
        Text("SanityStatus",sanityError.Length>0?FriendlyAccountError(sanityError):stale?L.T("Last update: ")+Clock.Date(sanity.Checked)+" "+Clock.Time(sanity.Checked):"");
      }
      Find<TextBlock>("SanityStatus").ToolTip=Find<TextBlock>("SanityStatus").Text;
      Find<TextBlock>("SanityStatus").Visibility=Find<TextBlock>("SanityStatus").Text.Length>0?Visibility.Visible:Visibility.Collapsed;
      Find<TextBlock>("RecoveryLabel").ToolTip=Find<TextBlock>("RecoveryLabel").Text;
      Text("VersionLabel",schedule.Version);
      var start=Clock.Local(schedule.Start);var end=Clock.Local(schedule.End);
      Text("MaintenanceDate",Clock.Date(schedule.Start)+(start.Date!=end.Date?" → "+Clock.Date(schedule.End):""));
      Text("MaintenanceRange",Clock.Time(schedule.Start)+"–"+Clock.Time(schedule.End));
      Find<TextBlock>("MaintenanceDate").ToolTip=Clock.Date(schedule.Start)+(start.Date!=end.Date?" → "+Clock.Date(schedule.End):"");
      Find<TextBlock>("MaintenanceRange").ToolTip=L.T("Scheduled maintenance end");
      if(now<schedule.Start) {Text("ScheduleHeading",L.T("NEXT UPDATE"));Text("CountdownLabel",L.T("Starts in: ")+Clock.Remaining(schedule.Start-now));}
      else if(now<schedule.End) {Text("ScheduleHeading",L.T("MAINTENANCE"));Text("CountdownLabel",L.T("Scheduled end in: ")+Clock.Remaining(schedule.End-now));}
      else {Text("ScheduleHeading",L.T("LATEST UPDATE"));Text("CountdownLabel",newsError.Length>0?L.T("Dates are temporarily unavailable"):L.T("The next date has not been announced"));}
      Text("NewsStatus",newsError.Length>0?L.T("Schedule was not updated. Try again."):"");
      Find<TextBlock>("NewsStatus").Visibility=newsError.Length>0?Visibility.Visible:Visibility.Collapsed;
      PaintUpcoming(now);
      PaintStudio(now);
      SyncCompactText();
      if(cardLayoutDirty || updateBlockVisible!=HasVisibleUpdate(now))ApplyLayout(false);
      UpdateTrayStatus(now);
    }
    static string FriendlyAccountError(string detail) {
      if(string.IsNullOrEmpty(detail))return "";
      return detail;
    }
    static void Open(string url) {
      Uri uri; if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || uri.Scheme!="https")return;
      if(uri.Host!="endfield.gryphline.com" && uri.Host!="www.skport.com" && uri.Host!="game.skport.com" && !(uri.Host=="web-api.skport.com" && uri.AbsolutePath=="/cookie_store/account_token"))return;
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});
    }
    void Settings() {
      if(settingsDialog!=null && settingsDialog.IsVisible){settingsDialog.Activate();return;}
      // Finish menu capture before opening another window. A desktop-owned
      // widget must not enter a nested modal loop from either menu system.
      if(widgetMenu!=null)widgetMenu.IsOpen=false;
      if(tray!=null && tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Close();
      var dialog=SettingsWindow();settingsDialog=dialog;
      dialog.Closed+=(s,e)=>{if(settingsDialog==dialog)settingsDialog=null;};
      try{dialog.Show();}
      catch{if(settingsDialog==dialog)settingsDialog=null;dialog.Close();throw;}
    }

  }

  static class Program {
    [STAThread] public static void Main(string[] args) {
      bool automatic=args.Length==1 && args[0]=="--autostart";
      try {
        if(automatic)StartupLaunchRecord.Save("starting");
        if(args.Length==2 && args[0]=="--signal-test" && args[1].StartsWith("Local\\EndfieldWidget.Test.",StringComparison.Ordinal)){Environment.ExitCode=InstanceActivation.Request(args[1],1)?0:1;return;}
        if(args.Length>=2 && (args[0]=="--account-test" || args[0]=="--skport-probe")){AccountTests.Run(args[1],args[0]=="--skport-probe");return;}
        if(args.Length>=2 && args[0]=="--self-test") {SelfTest.Run(args[1],args.Length>2?args[2]:null);return;}
        bool smoke=args.Length>=3 && args[0]=="--smoke-test";
        bool desktopTest=args.Length>=3 && args[0]=="--desktop-test";
        bool desktopView=args.Length>=3 && args[0]=="--desktop-view";
        bool featureTest=args.Length>=3 && args[0]=="--feature-test";
        bool localizationTest=args.Length>=3 && args[0]=="--localization-test";
        bool releaseTest=args.Length>=3 && args[0]=="--release-test";
        bool featureView=args.Length>=3 && args[0]=="--feature-view";
        if(smoke || desktopTest || desktopView || featureTest || localizationTest || releaseTest || featureView){Storage.DirectoryPath=Path.GetFullPath(args[2]);Storage.Isolated=true;}
        bool settingsPreview=args.Length>=2 && args[0]=="--settings-preview";
        bool inputTest=args.Length>=2 && args[0]=="--input-test";
        bool preview=args.Length>=2 && (args[0]=="--preview" || settingsPreview || inputTest || desktopTest || desktopView || featureTest || localizationTest || releaseTest || featureView);
        bool owns;
        using(var mutex=new Mutex(true,smoke || desktopTest || desktopView || featureTest || localizationTest || releaseTest || featureView?"Local\\EndfieldWidget.Test."+Guid.NewGuid().ToString("N"):"Local\\EndfieldWidget",out owns)) {
          if(!owns && !preview){bool shown=InstanceActivation.Request(InstanceActivation.Name);if(automatic)StartupLaunchRecord.Save(shown?"existing-instance":"instance-unavailable");if(!shown && !automatic)Forms.MessageBox.Show(L.T("Could not show the widget. Open it from the tray icon."),"Endfield");return;}
          using(var activation=!preview && !smoke?new InstanceActivation(InstanceActivation.Name):null) {
          var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
          var widget=new Widget(preview);if(activation!=null){activation.Bind(widget.RequestShowFromLauncher);widget.EnableCrashRecovery(Array.IndexOf(args,"--recovered")>=0);}
          if(releaseTest)widget.ReleaseTest(args[1]);else if(localizationTest)widget.LocalizationTest(args[1]);else if(featureTest){if(args.Length>3 && args[3]=="calendar")widget.CalendarTest(args[1]);else if(args.Length>3 && args[3]=="tray")widget.TrayMenuTest(args[1]);else widget.FeatureTest(args[1]);}else if(featureView)widget.FeatureView(args[1],args.Length>3?args[3]:"");else if(desktopView)widget.DesktopView(args[1]);else if(desktopTest)widget.DesktopTest(args[1],args.Length>3?args[3]:null);else if(inputTest)widget.InputTest(args[1],args.Length>2?args[2]:null);else if(preview)widget.Capture(args[1],settingsPreview,args.Length>2?args[2]:null);else if(smoke)widget.SmokeTest(args[1]);else {widget.Run();if(automatic)widget.RecordAutomaticStartup();CrashRecovery.RuntimeStarted=true;}app.Run();
          }
        }
      } catch(Exception ex) {
        if(automatic)StartupLaunchRecord.Save("failed",false,false,ex);
        Environment.ExitCode=1;
        if(args.Length>=2 && (args[0]=="--preview" || args[0]=="--settings-preview" || args[0]=="--self-test" || args[0]=="--smoke-test" || args[0]=="--input-test" || args[0]=="--account-test" || args[0]=="--skport-probe" || args[0]=="--desktop-test" || args[0]=="--desktop-view" || args[0]=="--feature-test" || args[0]=="--localization-test" || args[0]=="--release-test" || args[0]=="--feature-view")) {
          File.WriteAllText(args[1]+".error.txt",ex.ToString(),Encoding.UTF8);
          return;
        }
        FailureRecord.Record(ex);
        if(CrashRecovery.RuntimeStarted)throw;
        try{Directory.CreateDirectory(Storage.DirectoryPath);File.WriteAllText(Path.Combine(Storage.DirectoryPath,"startup-error.log"),ex.GetType().FullName+"\n"+ex.StackTrace,Encoding.UTF8);}catch{}
        Forms.MessageBox.Show(L.T("Could not start the widget. Try the latest build."),"Endfield");
      }
    }
  }

  static class SelfTest {
    static void Check(bool condition,string label,List<string> report) {if(!condition)throw new Exception("FAILED: "+label);report.Add("PASS: "+label);}
    public static void Run(string reportPath,string fixtureDirectory) {
      L.Set("en");
      var report=new List<string>();
      var previousZone=Clock.ZoneOverride;
      try {
        var fixtureZone=TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");Clock.ZoneOverride=()=>fixtureZone;
        var summer=new DateTimeOffset(2026,10,14,22,0,0,TimeSpan.Zero);
        var winter=new DateTimeOffset(2026,12,14,22,0,0,TimeSpan.Zero);
        Check(Clock.Date(summer)=="10/15/2026" && Clock.Time(summer)=="01:00","Eastern European date changes at midnight / summer UTC+3",report);
        Check(Clock.Date(winter)=="12/15/2026" && Clock.Time(winter)=="00:00","Eastern European winter UTC+2",report);
        Check(Clock.Local(new DateTimeOffset(2026,10,25,0,59,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(3) && Clock.Local(new DateTimeOffset(2026,10,25,1,1,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(2),"Eastern European daylight-saving transition",report);
        string testToken="test-token";
        Check(Storage.Unprotect(Storage.Protect(testToken))==testToken,"Windows DPAPI round-trip",report);
        Check(SkportClient.NormalizeToken("ACCOUNT_TOKEN=a%2Fb; other=c")=="a/b","Token cookie decoding",report);
        Check(SkportClient.NormalizeToken("account_token=a%2Fb; other=c")=="a/b","Lowercase token cookie input accepted",report);
        Check(SkportClient.NormalizeToken("{\"code\":0,\"data\":{\"content\":\"test-token\"}}")=="test-token","Current SKPORT cookie-store JSON accepted",report);
        Check(SkportClient.NormalizeToken("\"test-token\"")=="test-token" && SkportClient.NormalizeToken("{invalid}")=="","Quoted token accepted and invalid JSON handled safely",report);
        var role=new GameRole {RoleId="test-role",ServerId="2",UserId="test-user"};
        var current=DateTimeOffset.FromUnixTimeSeconds(1792015200);
        object payload=Json.Decode("{\"code\":0,\"timestamp\":\"1792015200\",\"data\":{\"detail\":{\"base\":{\"name\":\"Fixture\"},\"dungeon\":{\"curStamina\":\"100\",\"maxStamina\":\"240\",\"maxTs\":\"1792075680\"}}}}");
        var sanity=SkportClient.ParseSanity(payload,role,current);
        Check(sanity.Current==100 && sanity.Maximum==240 && sanity.FullAt==1792075680,"Sanity response parsing (string numerics)",report);
        Check(sanity.Estimate(current.AddSeconds(864))==102,"Sanity estimated regeneration",report);
        Check(sanity.Estimate(current.AddMinutes(16))==100,"Stale reading does not pretend to be live",report);
        sanity.Current=260;Check(sanity.Estimate(current.AddMinutes(5))==260,"Sanity over capacity preserved",report);
        bool rejected=false;try{SkportClient.ParseSanity(Json.Decode("{\"data\":{\"detail\":{\"dungeon\":{\"curStamina\":1}}}}"),role,current);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Missing Sanity fields rejected",report);
        string signature=SkportClient.Sign("/api/v1/game/endfield/card/detail","roleId=42&serverId=2&userId=7","1792015200","test-salt");
        Check(signature=="44b69b0fd065b17c6e014c7efe03b898","Request signature matches independent Python calculation",report);
        object bindings=Json.Decode("{\"data\":{\"list\":[{\"appCode\":\"endfield\",\"bindingList\":[{\"uid\":\"game-uid\",\"roles\":[{\"roleId\":\"role-1\",\"serverId\":\"2\",\"nickname\":\"Fixture\"}]}]}]}}");
        var parsedRoles=SkportClient.ParseRoles(bindings,"skport-user-id");
        Check(parsedRoles.Count==1 && parsedRoles[0].UserId=="skport-user-id","SKPORT user ID is distinct from game UID",report);
        Check(!BannerClient.IsDebutTitle("[Resplendent Spectrum] RE-Factor Headhunting #1") && BannerClient.IsDebutTitle("[Rejuvenation] Chartered Headhunting"),"Rerun banners are excluded",report);
        var debutSeed=BannerState.Seed();
        Check(BannerClient.Select(debutSeed,Schedule.Seed().End.AddSeconds(-1),false).Key=="typhoeus" && BannerClient.Select(debutSeed,Schedule.Seed().End,false).Key=="si","Background switches at the debut start, not the announcement",report);
        Check(BannerClient.Select(debutSeed,new DateTimeOffset(2026,10,1,12,0,0,TimeSpan.Zero),false).Key=="typhoeus","Previous debut artwork stays during a rerun period",report);
        var repeats=new List<BannerNotice> {new BannerNotice {Name="First",Cid="1",Published=1},new BannerNotice {Name="Newer",Cid="2",Published=2},new BannerNotice {Name="First",Cid="3",Published=3}};
        Check(BannerClient.FirstDebuts(repeats).Single(n=>n.Name=="First").Cid=="1","Repeated operator is recognized even in a Chartered banner",report);
        Check(!BannerClient.IsOfficialImage("https://example.com/portrait.png") && BannerClient.IsOfficialImage("https://web-static.hg-cdn.com/portrait.png"),"Portrait downloads limited to the official image host",report);
        string regionalHtml="<p>[Example] Chartered Headhunting features the Rate-UP 6★ operator [Example].</p><p>Availability:</p><p>Americas / Europe Server: Nov. 4, 2026 at 12:00 – Nov. 20, 2026 at 11:59 (server time)</p><p>Asia Server: Nov. 5, 2026 at 12:00 – Nov. 21, 2026 at 11:59 (server time)</p>";
        var regional=BannerClient.ParseNotice(regionalHtml,"[Example] Chartered Headhunting","test",1793520000);
        Check(regional.LocalStart.StartsWith("Nov. 4") && regional.AsiaLocalStart.StartsWith("Nov. 5"),"Different regional banner dates retained",report);
        var regionState=new BannerState {Debuts=new List<Debut>{new Debut {Name="Old",EuropeStart=new DateTimeOffset(2026,11,1,0,0,0,TimeSpan.Zero),AsiaStart=new DateTimeOffset(2026,11,1,0,0,0,TimeSpan.Zero)},new Debut {Name="New",EuropeStart=new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero),AsiaStart=new DateTimeOffset(2026,11,5,4,0,0,TimeSpan.Zero)}}};
        var regionalNow=new DateTimeOffset(2026,11,5,10,0,0,TimeSpan.Zero);
        Check(BannerClient.Select(regionState,regionalNow,false).Name=="Old" && BannerClient.Select(regionState,regionalNow,true).Name=="New","Background respects the selected game server",report);
        var accentColors=new List<Color>();
        foreach(string artKey in new[]{"typhoeus","si","argent-flow"}) {
          BitmapFrame portrait;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Art."+artKey+"-portrait"))portrait=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
          var theme=OperatorTheme.FromPortrait(portrait);accentColors.Add(theme.Accent);
          Check(OperatorTheme.Contrast(theme.Accent,theme.Raised)>=4.5 && OperatorTheme.Contrast(theme.Text,theme.Card)>=4.5 && OperatorTheme.Contrast(theme.Muted,theme.Raised)>=4.5 && OperatorTheme.Contrast(theme.Subtle,theme.Raised)>=4.5,"Readable automatic palette: "+artKey+" (hue "+Math.Round(theme.Hue)+")",report);
        }
        Check(accentColors.Distinct().Count()==3,"Different portraits produce different widget palettes",report);
        var transparentPixels=new byte[]{255,0,0,0,255,0,0,0,0,255,0,255,0,255,0,255};
        var transparentFixture=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,transparentPixels,8);
        Check(Math.Abs(OperatorTheme.FromPortrait(transparentFixture).Hue-120)<1,"Transparent background colors are ignored",report);
        var neutralPixels=new byte[]{240,240,240,255,200,200,200,255,180,180,180,255,230,230,230,255};
        var neutralFixture=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,neutralPixels,8);
        Check(Math.Abs(OperatorTheme.FromPortrait(neutralFixture).Hue-215)<1,"Neutral portrait uses a stable fallback palette",report);
        if(!string.IsNullOrEmpty(fixtureDirectory)) {
          string newsHtml=File.ReadAllText(Path.Combine(fixtureDirectory,"news.html"),Encoding.UTF8);
          var articles=NewsClient.Articles(newsHtml);
          Check(articles.Any(a=>Json.Str(a,"cid")=="0744"),"Current official news feed parsed",report);
          string maintenance=File.ReadAllText(Path.Combine(fixtureDirectory,"maintenance.html"),Encoding.UTF8);
          var schedule=NewsClient.ParseSchedule(maintenance,"[Sanctuary of Ink] Version Pre-Download & Update Notice","https://endfield.gryphline.com/en-us/news/0744",2026);
          Check(schedule.Start==Schedule.Seed().Start && schedule.End==Schedule.Seed().End,"Current official Europe maintenance schedule parsed",report);
          Check(Clock.Time(schedule.Start)=="01:00" && Clock.Time(schedule.End)=="07:00","Official schedule converted to Eastern European",report);
          string bannerFixture=Path.Combine(fixtureDirectory,"banners");
          if(Directory.Exists(bannerFixture)) {
            int parsedCount=0;
            foreach(string file in Directory.GetFiles(bannerFixture,"*.json")) {
              object bannerValue=Json.Decode(File.ReadAllText(file,Encoding.UTF8));
              var notice=BannerClient.ParseNotice(Json.Str(bannerValue,"data","data"),Json.Str(bannerValue,"data","title"),Json.Str(bannerValue,"data","cid"),Json.Num(bannerValue,"data","displayTime"));
              if(notice!=null && !string.IsNullOrEmpty(notice.Name))parsedCount++;
            }
            Check(parsedCount>=11,"All current and historical debut notice formats parsed",report);
          }
          string portraitFixture=Path.Combine(fixtureDirectory,"8039-dc74c62d39866718.js");
          if(File.Exists(portraitFixture)) {
            var catalog=BannerClient.ParseCatalogue(File.ReadAllText(portraitFixture,Encoding.UTF8));
            Check(catalog.Any(a=>a.Name=="Si" && a.Portrait.EndsWith("jsspsi.19ec05d9.png")) && catalog.Any(a=>a.Name=="Typhoeus"),"Official portrait catalogue discovered from current website assets",report);
          }
          using(var client=Widget.NewHttp()) {
            var live=new NewsClient(client).Fetch().GetAwaiter().GetResult();
            Check(live.Source==schedule.Source && live.Start==schedule.Start && live.End==schedule.End,"Live official news download and discovery",report);
          }
        }
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) {
          var w=(Window)XamlReader.Load(stream);
          Check(w.FindName("SanityNumber")!=null && w.FindName("VersionLabel")!=null,"WPF view loads",report);
          Check(((Button)w.FindName("PlayButton")).IsEnabled,"Steam play button is enabled",report);w.Close();
        }
        report.Add("ALL CHECKS PASSED");
      } catch(Exception ex) {report.Add(ex.Message);Environment.ExitCode=1;}
      finally{Clock.ZoneOverride=previousZone;}
      File.WriteAllLines(reportPath,report,Encoding.UTF8);
    }
  }
}
