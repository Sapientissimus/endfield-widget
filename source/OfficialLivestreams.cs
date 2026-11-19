using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace EndfieldWidget {
  sealed class OfficialLivestreams {
    internal const string ChannelId="UCowPaVRBzg8CE6K4CB6LJfw";
    internal const string Source="https://www.youtube.com/channel/"+ChannelId+"/streams";
    internal const string FeedUrl="https://www.youtube.com/feeds/videos.xml?channel_id="+ChannelId;
    internal static bool IsWatchUrl(string url){return url!=null && Regex.IsMatch(url,@"^https://www\.youtube\.com/watch\?v=[A-Za-z0-9_-]{11}$",RegexOptions.CultureInvariant);}
    static string VideoId(string identity) {
      var match=Regex.Match(identity??"", @"^livestream:(?<id>[A-Za-z0-9_-]{11})$",RegexOptions.CultureInvariant);
      return match.Success?match.Groups["id"].Value:null;
    }
    internal static bool IsThumbnail(string url,string identity) {
      string id=VideoId(identity);
      return id!=null && url!=null && Regex.IsMatch(url,@"^https://i\.ytimg\.com/vi/"+Regex.Escape(id)+@"/(?:default|mqdefault|hqdefault|sddefault|maxresdefault|hq720)\.jpg$",RegexOptions.CultureInvariant);
    }
    static string Thumbnail(object metadata,string id) {
      var thumbnails=Json.Items(Json.At(metadata,"videoDetails","thumbnail","thumbnails"))
        .Concat(Json.Items(Json.At(metadata,"microformat","playerMicroformatRenderer","thumbnail","thumbnails")));
      foreach(var image in thumbnails.OrderByDescending(item=>Json.Num(item,"width"))) {
        Uri uri;
        if(!Uri.TryCreate(Json.Str(image,"url"),UriKind.Absolute,out uri) || uri.UserInfo.Length>0 || !uri.IsDefaultPort)continue;
        string url=uri.GetLeftPart(UriPartial.Path);
        if(IsThumbnail(url,"livestream:"+id))return url;
      }
      return null;
    }
    internal static GameEvent CardArtwork(IEnumerable<CalendarSourceState> sources,CalendarEntry entry) {
      if(sources==null || entry==null || CardUrl(entry)==null)return null;
      const string prefix="game:",suffix=":start";
      string identity=entry.Id.Substring(prefix.Length,entry.Id.Length-prefix.Length-suffix.Length);
      return sources.Where(s=>s!=null && s.Source==Source).SelectMany(s=>s.Events??new List<GameEvent>())
        .FirstOrDefault(e=>e!=null && e.Source==Source && e.Category==GameEventCategory.Livestream && e.Identity==identity &&
          (e.EuropeStartUnix==entry.At.ToUnixTimeSeconds() || e.AsiaStartUnix==entry.At.ToUnixTimeSeconds()) && IsThumbnail(e.ArtworkUrl,e.Identity));
    }
    internal static string CardUrl(CalendarEntry entry) {
      if(entry==null || entry.Source!=Source)return null;
      var identity=Regex.Match(entry.Id??"", @"^game:livestream:(?<id>[A-Za-z0-9_-]{11}):start$",RegexOptions.CultureInvariant);
      return identity.Success?"https://www.youtube.com/watch?v="+identity.Groups["id"].Value:null;
    }
    const int MaximumFeed=262144,MaximumPage=3000000,MaximumVideos=3;
    readonly HttpClient transport;
    readonly object gate=new object();
    Task<CalendarSourceState> pending;
    CalendarSourceState saved;
    DateTimeOffset next;
    internal OfficialLivestreams(HttpClient http){transport=http;}
    internal static bool PreviewTitle(string title) {
      return Regex.IsMatch(title??"", @"(?:version|release|dev\s*log).{0,100}(?:special\s+program|preview\s+(?:program|live)|live[- ]?stream)|(?:special\s+program|live[- ]?stream).{0,100}(?:version|preview)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
    internal Task<CalendarSourceState> Fetch(CalendarSourceState previous,DateTimeOffset now) {
      lock(gate) {
        if(pending!=null && !pending.IsCompleted)return pending;
        if(saved!=null && now>=saved.LastAttempt && now<next)return Task.FromResult(saved);
        pending=Refresh(previous??saved,now);
        return pending;
      }
    }
    async Task<CalendarSourceState> Refresh(CalendarSourceState previous,DateTimeOffset now) {
      var state=new CalendarSourceState{Source=Source,LastAttempt=now,LastSuccess=previous==null?DateTimeOffset.MinValue:previous.LastSuccess,Events=previous==null?new List<GameEvent>():previous.Events};
      try {
        string rss=await Read(FeedUrl,MaximumFeed).ConfigureAwait(false);
        var entries=ReadFeed(rss);
        var result=new List<GameEvent>();
        foreach(var entry in entries) {
          string page=await Read("https://www.youtube.com/watch?v="+entry.Id,MaximumPage).ConfigureAwait(false);
          var record=ReadVideo(page,entry,now);
          if(record!=null)result.Add(record);
        }
        state.Events=result;state.Published=result.Select(e=>e.Published).DefaultIfEmpty(0).Max();state.LastSuccess=now;
      }catch(OperationCanceledException){state.Problem="timeout";}
      catch(HttpRequestException){state.Problem="network";}
      catch(IOException){state.Problem="network";}
      catch(FormatException){state.Problem="format";}
      catch(XmlException){state.Problem="format";}
      catch(ObjectDisposedException){state.Problem="timeout";}
      lock(gate){saved=state;next=now.AddMinutes(state.Problem==null?60:5);}
      return state;
    }
    async Task<string> Read(string url,int maximum) {
      using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8)))
      using(var response=await transport.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)) {
        if(!response.IsSuccessStatusCode)throw new HttpRequestException("Official stream source unavailable.");
        if(response.RequestMessage!=null && response.RequestMessage.RequestUri.AbsoluteUri!=url)throw new FormatException("Unexpected stream redirect.");
        if(response.Content==null || response.Content.Headers.ContentLength>maximum)throw new FormatException("Stream source exceeds the size limit.");
        using(timeout.Token.Register(()=>response.Dispose()))
        using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using(var output=new MemoryStream()) {
          byte[] buffer=new byte[16384];
          for(;;) {
            int count=await input.ReadAsync(buffer,0,Math.Min(buffer.Length,maximum-(int)output.Length+1),timeout.Token).ConfigureAwait(false);
            if(count==0)break;
            if(output.Length+count>maximum)throw new FormatException("Stream source exceeds the size limit.");
            output.Write(buffer,0,count);
          }
          return Encoding.UTF8.GetString(output.ToArray());
        }
      }
    }
    internal sealed class Video {
      internal string Id,Title;
      internal DateTimeOffset Published;
    }
    internal static List<Video> ReadFeed(string rss) {
      var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumFeed};
      var document=new XmlDocument{XmlResolver=null};
      using(var reader=XmlReader.Create(new StringReader(rss),settings))document.Load(reader);
      var ns=new XmlNamespaceManager(document.NameTable);
      ns.AddNamespace("a","http://www.w3.org/2005/Atom");ns.AddNamespace("yt","http://www.youtube.com/xml/schemas/2015");
      var channel=document.SelectSingleNode("/a:feed/yt:channelId",ns);
      var author=document.SelectSingleNode("/a:feed/a:author/a:uri",ns);
      bool abbreviated=channel!=null && channel.InnerText==ChannelId.Substring(2) && author!=null && author.InnerText=="https://www.youtube.com/channel/"+ChannelId;
      if(channel==null || channel.InnerText!=ChannelId && !abbreviated)throw new FormatException("The stream channel does not match.");
      var videos=new List<Video>();
      foreach(XmlNode entry in document.SelectNodes("/a:feed/a:entry",ns)) {
        var owner=entry.SelectSingleNode("yt:channelId",ns);var id=entry.SelectSingleNode("yt:videoId",ns);
        var title=entry.SelectSingleNode("a:title",ns);var published=entry.SelectSingleNode("a:published",ns);
        DateTimeOffset at;
        if(owner==null || owner.InnerText!=ChannelId || id==null || !Regex.IsMatch(id.InnerText,@"^[A-Za-z0-9_-]{11}$") ||
          title==null || title.InnerText.Length>160 || !PreviewTitle(title.InnerText) ||
          published==null || !DateTimeOffset.TryParse(published.InnerText,CultureInfo.InvariantCulture,DateTimeStyles.None,out at))continue;
        if(videos.Any(v=>v.Id==id.InnerText))continue;
        videos.Add(new Video{Id=id.InnerText,Title=title.InnerText,Published=at});
        if(videos.Count==MaximumVideos)break;
      }
      return videos;
    }
    internal static GameEvent ReadVideo(string html,Video video,DateTimeOffset now) {
      string json=PlayerJson(html);
      object value;
      try{value=Json.Decode(json);}catch(ArgumentException){throw new FormatException("Invalid stream metadata.");}
      if(Json.Str(value,"videoDetails","channelId")!=ChannelId || Json.Str(value,"videoDetails","videoId")!=video.Id)return null;
      string title=Json.Str(value,"videoDetails","title");
      string timestamp=Json.Str(value,"microformat","playerMicroformatRenderer","liveBroadcastDetails","startTimestamp");
      if(!PreviewTitle(title) || title.Length>160 || !Regex.IsMatch(timestamp,@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$"))return null;
      DateTimeOffset start;
      if(!DateTimeOffset.TryParse(timestamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out start) ||
        start<video.Published.AddDays(-1) || start>video.Published.AddDays(180) || start<now.AddDays(-90) || start>now.AddDays(180))return null;
      long unix=start.ToUnixTimeSeconds();
      return new GameEvent{Name=title,Source=Source,Identity="livestream:"+video.Id,ArtworkUrl=Thumbnail(value,video.Id),Published=video.Published.ToUnixTimeSeconds(),Category=GameEventCategory.Livestream,EuropeStartUnix=unix,AsiaStartUnix=unix};
    }
    static string PlayerJson(string html) {
      var marker=Regex.Match(html,@"(?:var\s+)?ytInitialPlayerResponse\s*=\s*\{");
      int from=marker.Success?html.IndexOf('{',marker.Index):html.StartsWith("{",StringComparison.Ordinal)?0:-1;
      if(from<0)throw new FormatException("Stream metadata unavailable.");
      bool quoted=false,escaped=false;int depth=0;
      for(int i=from;i<html.Length;i++) {
        char c=html[i];
        if(quoted){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='"')quoted=false;continue;}
        if(c=='"')quoted=true;
        else if(c=='{')depth++;
        else if(c=='}' && --depth==0)return html.Substring(from,i-from+1);
      }
      throw new FormatException("Incomplete stream metadata.");
    }
  }
  partial class BannerClient {
    internal OfficialLivestreams CalendarLivestreams;
    static readonly HttpClient streamHttp=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
    Task<CalendarSourceState> FetchStreams(CalendarFeedState previous,DateTimeOffset now) {
      if(Storage.Isolated && CalendarLivestreams==null)return Task.FromResult<CalendarSourceState>(null);
      if(CalendarLivestreams==null)CalendarLivestreams=new OfficialLivestreams(streamHttp);
      return CalendarLivestreams.Fetch((previous.Sources??new List<CalendarSourceState>()).FirstOrDefault(s=>s.Source==OfficialLivestreams.Source),now);
    }
  }
}
