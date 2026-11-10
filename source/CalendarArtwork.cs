using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndfieldWidget {
  static class CalendarArtwork {
    const int MaximumBytes=3000000;
    const int CacheCapacity=16;
    static readonly object gate=new object();
    static readonly SemaphoreSlim throttle=new SemaphoreSlim(2);
    static readonly Dictionary<string,Task<ImageSource>> cache=new Dictionary<string,Task<ImageSource>>(StringComparer.Ordinal);
    static readonly HttpClient http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(12)};

    internal static bool IsOfficial(string url) {
      Uri uri;
      return !string.IsNullOrEmpty(url) && url.Length<=2048 && Uri.TryCreate(url,UriKind.Absolute,out uri)
        && uri.Scheme==Uri.UriSchemeHttps && uri.Host=="web-static.hg-cdn.com" && uri.IsDefaultPort && uri.UserInfo.Length==0
        && Regex.IsMatch(uri.AbsolutePath,@"\.(?:png|jpe?g)$",RegexOptions.IgnoreCase);
    }

    internal static string Find(string cover,string html) {
      if(IsOfficial(cover))return cover;
      if(string.IsNullOrEmpty(html) || html.Length>2000000)return null;
      html=Regex.Replace(html,@"<(?:script|style|noscript)\b[^>]*>.*?</(?:script|style|noscript)>","",RegexOptions.Singleline|RegexOptions.IgnoreCase);
      foreach(Match image in Regex.Matches(html,@"<img\b[^>]*>",RegexOptions.IgnoreCase)) {
        var src=Regex.Match(image.Value,@"\s+src\s*=\s*(?:""(?<url>[^""]+)""|'(?<url>[^']+)')",RegexOptions.IgnoreCase);
        var url=WebUtility.HtmlDecode(src.Groups["url"].Value);
        if(IsOfficial(url))return url;
      }
      return null;
    }

    internal static CalendarSourceState CardArticle(IEnumerable<CalendarSourceState> sources,CalendarEntry entry) {
      if(sources==null || entry==null || !CalendarData.Official(entry.Source))return null;
      var candidates=sources.Where(s=>s!=null && CalendarData.Official(s.Source) && IsOfficial(s.ArtworkUrl)).ToList();
      var direct=candidates.FirstOrDefault(s=>s.Source==entry.Source);
      if(direct!=null && !direct.IsVersionOverview)return direct;
      string name=CalendarData.Normalize(entry.Name??entry.Title);
      long at=entry.At.ToUnixTimeSeconds(),start=entry.PeriodStart.HasValue?entry.PeriodStart.Value.ToUnixTimeSeconds():0;
      var related=candidates.Where(s=>s.Events!=null && s.Events.Any(e=>e!=null && e.Source==s.Source && CalendarData.Normalize(e.Name)==name &&
        (e.EuropeStartUnix==at || e.AsiaStartUnix==at || e.EuropeEndUnix==at || e.AsiaEndUnix==at ||
         start>0 && (e.EuropeStartUnix==start || e.AsiaStartUnix==start)))).OrderByDescending(s=>s.Published).ToList();
      return related.FirstOrDefault(s=>!s.IsVersionOverview)??direct??related.FirstOrDefault(s=>s.IsVersionOverview);
    }

    internal static Task<ImageSource> Get(string url) {
      if(!IsOfficial(url))return Task.FromResult<ImageSource>(null);
      lock(gate) {
        Task<ImageSource> pending;
        if(cache.TryGetValue(url,out pending))return pending;
        if(cache.Count>=CacheCapacity) {
          string available=cache.FirstOrDefault(item=>item.Value.IsCompleted).Key;
          if(available==null)return Task.FromResult<ImageSource>(null);
          cache.Remove(available);
        }
        // Reuse one request for all cards and the background using this URL.
        pending=Task.Run(()=>Download(http,url));cache[url]=pending;
        return pending;
      }
    }

    internal static async Task<ImageSource> Download(HttpClient transport,string url) {
      if(!IsOfficial(url))return null;
      await throttle.WaitAsync().ConfigureAwait(false);
      try {
        using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12)))
        using(var response=await transport.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false))
        using(timeout.Token.Register(()=>response.Dispose())) {
          if(!response.IsSuccessStatusCode || response.Content==null || response.Content.Headers.ContentLength>MaximumBytes)return null;
          var type=response.Content.Headers.ContentType;
          if(type==null || type.MediaType!="image/jpeg" && type.MediaType!="image/png")return null;
          using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
          using(var buffer=new MemoryStream()) {
            byte[] chunk=new byte[16384];
            while(true) {
              int read=await stream.ReadAsync(chunk,0,Math.Min(chunk.Length,MaximumBytes-(int)buffer.Length+1),timeout.Token).ConfigureAwait(false);
              if(read==0)break;
              if(buffer.Length+read>MaximumBytes)return null;
              buffer.Write(chunk,0,read);
            }
            buffer.Position=0;
            return Decode(buffer);
          }
        }
      }catch(IOException){return null;}
      catch(HttpRequestException){return null;}
      catch(OperationCanceledException){return null;}
      catch(NotSupportedException){return null;}
      catch(FormatException){return null;}
      catch(ArgumentException){return null;}
      catch(InvalidOperationException){return null;}
      catch(System.Runtime.InteropServices.COMException){return null;}
      finally{throttle.Release();}
    }

    internal static ImageSource Decode(Stream stream) {
      if(!stream.CanSeek || stream.Length>MaximumBytes)return null;
      var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
      if(!(decoder is JpegBitmapDecoder) && !(decoder is PngBitmapDecoder) || decoder.Frames.Count!=1)return null;
      var frame=decoder.Frames[0];int width=frame.PixelWidth,height=frame.PixelHeight;
      if(width<=0 || height<=0 || width>4096 || height>4096 || (long)width*height>16000000)return null;
      stream.Position=0;
      var image=new BitmapImage();
      image.BeginInit();
      image.CacheOption=BitmapCacheOption.OnLoad;
      double scale=Math.Min(1200.0/width,700.0/height);
      if(scale<1) {
        if(width/1200.0>=height/700.0)image.DecodePixelWidth=Math.Max(1,(int)Math.Floor(width*scale));
        else image.DecodePixelHeight=Math.Max(1,(int)Math.Floor(height*scale));
      }
      image.StreamSource=stream;
      image.EndInit();
      image.Freeze();
      return image;
    }
  }

  partial class Widget {
    Image calendarHeroArtworkTarget;
    string calendarHeroArtworkUrl;
    internal HttpClient calendarArtworkTransportOverride;

    Task<ImageSource> FetchCalendarImage(string url) {
      return calendarArtworkTransportOverride==null?CalendarArtwork.Get(url):CalendarArtwork.Download(calendarArtworkTransportOverride,url);
    }

    internal async void LoadCalendarArtwork(CalendarCardModel card) {
      if(Storage.Isolated && calendarArtworkTransportOverride==null || card==null)return;
      var article=CalendarArtwork.CardArticle(calendarFeed==null?null:calendarFeed.Sources,card.Entry);
      string url=article==null?null:article.ArtworkUrl;
      if(card.ArtworkRequestedUrl==url)return;
      card.ArtworkRequestedUrl=url;card.SetArtwork(null,null);
      if(url==null)return;
      var image=await FetchCalendarImage(url);
      if(card.ArtworkRequestedUrl==url)card.SetArtwork(image,article.ArtworkTitle??article.Source);
    }

    internal async void LoadCalendarHeroArtwork(Image target) {
      if(Storage.Isolated && calendarArtworkTransportOverride==null || target==null)return;
      var article=calendarFeed==null || calendarFeed.Sources==null?null:calendarFeed.Sources
        .Where(source=>source.IsVersionOverview)
        .OrderByDescending(source=>source.Published).FirstOrDefault();
      string url=article!=null && CalendarArtwork.IsOfficial(article.ArtworkUrl)?article.ArtworkUrl:null;
      if(calendarHeroArtworkTarget==target && calendarHeroArtworkUrl==url)return;
      calendarHeroArtworkTarget=target;calendarHeroArtworkUrl=url;target.Source=null;
      if(url==null)return;
      var image=await FetchCalendarImage(url);
      if(calendarHeroArtworkTarget==target && calendarHeroArtworkUrl==url && target.IsLoaded)target.Source=image;
    }

    void ReleaseCalendarArtwork() {
      calendarHeroArtworkTarget=null;
      calendarHeroArtworkUrl=null;
    }
  }
}
