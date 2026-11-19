using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckOfficialLivestreams(Action<bool,string> check,string folder) {
      const string channel="UCowPaVRBzg8CE6K4CB6LJfw";
      const string title="Arknights: Endfield New Version &amp; 2.0 Reveal Special Program";
      string rss="<feed xmlns='http://www.w3.org/2005/Atom' xmlns:yt='http://www.youtube.com/xml/schemas/2015'><yt:channelId>"+channel+"</yt:channelId><entry><yt:videoId>7YZ4VIUwn5c</yt:videoId><yt:channelId>"+channel+"</yt:channelId><title>"+title+"</title><published>2026-11-14T10:10:00+00:00</published></entry></feed>";
      string page="{\"videoDetails\":{\"videoId\":\"7YZ4VIUwn5c\",\"channelId\":\""+channel+"\",\"title\":\"Arknights: Endfield New Version & 2.0 Reveal Special Program\"},\"microformat\":{\"playerMicroformatRenderer\":{\"liveBroadcastDetails\":{\"startTimestamp\":\"2026-11-21T11:30:00+00:00\"}}}}";
      page=page.Replace("\"title\":\"Arknights:","\"thumbnail\":{\"thumbnails\":[{\"url\":\"https://i.ytimg.com/vi/7YZ4VIUwn5c/maxresdefault.jpg\",\"width\":1280,\"height\":720}]},\"title\":\"Arknights:");
      using(var fixture=new LivestreamFixture(rss,page))using(var http=new HttpClient(fixture)) {
        var before=new DateTimeOffset(2026,11,19,0,0,0,TimeSpan.Zero);
        var client=new BannerClient(http){CalendarLivestreams=new OfficialLivestreams(http)};
        var feed=await client.FetchCalendar(null);
        check(Json.Encode(feed.Events.Single()).Contains("https://i.ytimg.com/vi/7YZ4VIUwn5c/maxresdefault.jpg"),"The verified program keeps its matching official video thumbnail metadata");
        check(feed.Events.Any(e=>e.Name=="Arknights: Endfield New Version & 2.0 Reveal Special Program" &&
          e.EuropeStartUnix==1795260600 && e.AsiaStartUnix==1795260600 && e.EuropeEndUnix==0 && e.AsiaEndUnix==0),"The official upcoming version stream is discovered automatically with its exact global start");
        var eu=CalendarData.Entries(null,null,false,feed).Single();
        var asia=CalendarData.Entries(null,null,true,feed).Single();
        check(eu.Kind==CalendarKind.Milestone && eu.At==asia.At && eu.Name=="Arknights: Endfield New Version & 2.0 Reveal Special Program" &&
          eu.Source==OfficialLivestreams.Source && eu.End==null,"The live program appears as the same single global milestone in both server calendars without translating its title");
        var originalOpen=calendarOpenOverride;string selected=calendarSelectedId,language=L.Code;
        try {
          string launched=null;calendarOpenOverride=url=>launched=url;
          var card=new CalendarCardModel(eu,before,new CalendarSourceCommand(OpenCalendarSource));
          card.OpenSource.Execute(card.SourceTarget);
          check(card.OpenSource.CanExecute(card.SourceTarget) && launched=="https://www.youtube.com/watch?v=7YZ4VIUwn5c","The real stream announcement command passes the direct verified video through the normal URL gate to the injected browser launcher");
          launched=null;
          Open("https://www.youtube.com/watch?v=7YZ4VIUwn5c&redirect=https://example.com",calendarOpenOverride);
          Open("https://www.youtube.com.evil.example/watch?v=7YZ4VIUwn5c",calendarOpenOverride);
          var invalid=new CalendarEntry{Source=OfficialLivestreams.Source,Id="game:livestream:invalid:start"};
          new CalendarSourceCommand(OpenCalendarSource).Execute(invalid);
          check(launched==null,"The real browser gate rejects additional video parameters, lookalike hosts and malformed cached stream identities");
          L.Set("ru");
          check(card.Name=="Трансляция новой версии" && (await client.FetchCalendarDetail(eu)).Description=="Arknights: Endfield New Version & 2.0 Reveal Special Program","The Russian stream card uses a localized event label while its details preserve the official original title");
          check(new CalendarSnapshot(new[]{eu},CalendarData.AllKinds,"трансляция").Entries.Count==1 &&
            new CalendarSnapshot(new[]{eu},CalendarData.AllKinds,"2.0 Reveal").Entries.Count==1,"Calendar search finds both the localized stream label and its original program title");
        }finally{calendarOpenOverride=originalOpen;calendarSelectedId=selected;L.Set(language);}
        var record=OfficialLivestreams.ReadFeed(rss).Single();
        const string thumbnail="https://i.ytimg.com/vi/7YZ4VIUwn5c/maxresdefault.jpg";
        var imageEvent=feed.Events.Single();
        check(OfficialLivestreams.IsThumbnail(thumbnail,imageEvent.Identity) && !CalendarArtwork.IsOfficial(thumbnail) &&
          !OfficialLivestreams.IsThumbnail(thumbnail,"livestream:OtherVideo1") &&
          !OfficialLivestreams.IsThumbnail(thumbnail.Replace("i.ytimg.com","i.ytimg.com.evil.example"),imageEvent.Identity),"Thumbnail authorization stays tied to the verified video and never widens the general artwork host policy");
        check(OfficialLivestreams.ReadVideo(page.Replace(thumbnail,thumbnail.Replace("7YZ4VIUwn5c","OtherVideo1")),record,before).ArtworkUrl==null &&
          OfficialLivestreams.ReadVideo(page.Replace(thumbnail,"https://i.ytimg.com/vi/7YZ4VIUwn5c/unexpected.jpg"),record,before).ArtworkUrl==null,"Mismatched video paths and unknown thumbnail filenames leave the program without artwork");
        await CheckLivestreamArtwork(check,folder,feed);
        string abbreviated=rss.Replace("<yt:channelId>"+channel+"</yt:channelId><entry>","<yt:channelId>"+channel.Substring(2)+"</yt:channelId><author><uri>https://www.youtube.com/channel/"+channel+"</uri></author><entry>");
        check(OfficialLivestreams.ReadFeed(abbreviated).Single().Id==record.Id,"The live RSS format with an abbreviated root ID still requires the exact official author and entry channel");
        check(OfficialLivestreams.ReadVideo(page.Replace("2026-11-21T11:30:00+00:00","2026-11-21"),record,before)==null &&
          OfficialLivestreams.ReadVideo(page.Replace("2026-11-21T11:30:00+00:00","2026-11-21T11:30:00"),record,before)==null &&
          OfficialLivestreams.ReadVideo(page.Replace("2026-11-21T11:30:00+00:00","2026-02-31T11:30:00+00:00"),record,before)==null,"Date-only, offset-free and impossible stream times never become guessed calendar starts");
        check(OfficialLivestreams.ReadVideo(page.Replace(channel,"untrusted-channel"),record,before)==null &&
          OfficialLivestreams.ReadVideo(page.Replace("7YZ4VIUwn5c","OtherVideo1"),record,before)==null,"Watch metadata must match both the discovered video and the exact official channel");
        check(!OfficialLivestreams.PreviewTitle("Fan Content and Streaming Guidelines") && !OfficialLivestreams.PreviewTitle("Version Update & Maintenance Preview"),"Streaming guidelines and maintenance previews cannot be confused with a version program");
        var service=new OfficialLivestreams(http);
        var first=await service.Fetch(null,before);
        int requests=fixture.Requests;
        var cached=await service.Fetch(first,before.AddMinutes(10));
        check(ReferenceEquals(first,cached) && fixture.Requests==requests,"Repeated calendar refreshes reuse the stream result within the hourly cache");
        fixture.Status=HttpStatusCode.ServiceUnavailable;
        var offline=await service.Fetch(first,before.AddHours(2));requests=fixture.Requests;
        var retry=await service.Fetch(offline,before.AddHours(2).AddMinutes(2));
        check(offline.Problem=="network" && offline.Events.Single().Identity==first.Events.Single().Identity && ReferenceEquals(offline,retry) && requests==fixture.Requests,"An unavailable YouTube source retains the confirmed start and waits before retrying");
        fixture.Status=HttpStatusCode.OK;fixture.Length=3000001;
        var oversized=await new OfficialLivestreams(http).Fetch(null,before);
        check(oversized.Problem=="format" && oversized.Events.Count==0,"Oversized stream metadata is rejected by the bounded reader");
        fixture.Length=null;
        var duplicate=rss.Replace("</feed>",rss.Substring(rss.IndexOf("<entry>"),rss.IndexOf("</entry>")+8-rss.IndexOf("<entry>"))+"</feed>");
        check(OfficialLivestreams.ReadFeed(duplicate).Count==1 && GameEventDates.Latest(first.Events.Concat(first.Events)).Count==1,"Duplicate feed entries and repeated snapshots produce one stream occurrence");
        fixture.Rss=rss.Replace(channel,"untrusted-channel");requests=fixture.Requests;
        var foreign=await new OfficialLivestreams(http).Fetch(null,before);
        check(foreign.Problem=="format" && foreign.Events.Count==0 && fixture.Requests==requests+1,"A foreign RSS channel is rejected before any watch-page request");
        string entries=string.Concat(Enumerable.Range(0,20).Select(i=>"<entry><yt:videoId>stream"+i.ToString("00000")+"</yt:videoId><yt:channelId>"+channel+"</yt:channelId><title>Version Preview Livestream</title><published>2026-11-14T10:10:00Z</published></entry>"));
        fixture.Rss="<feed xmlns='http://www.w3.org/2005/Atom' xmlns:yt='http://www.youtube.com/xml/schemas/2015'><yt:channelId>"+channel+"</yt:channelId>"+entries+"</feed>";
        requests=fixture.Requests;await new OfficialLivestreams(http).Fetch(null,before);
        check(fixture.Requests-requests==4,"A refresh fetches at most one RSS document and three candidate watch pages");
        CalendarFeedStore.Save(feed);
        var restored=CalendarFeedStore.Load(null);
        check(restored.Events.Any(e=>e.Category==GameEventCategory.Livestream && e.Identity=="livestream:7YZ4VIUwn5c") &&
          CalendarData.SourceUrl(CalendarData.Entries(null,null,false,restored).Single())=="https://www.youtube.com/watch?v=7YZ4VIUwn5c","The confirmed program and its direct action survive a calendar cache save and restart-shaped read");
        check(restored.Events.Single().ArtworkUrl==thumbnail && OfficialLivestreams.CardArtwork(restored.Sources,CalendarData.Entries(null,null,false,restored).Single())!=null,"The persisted stream thumbnail remains bound to its verified occurrence after restart");
        var art=restored.Events.Single();art.ArtworkUrl=thumbnail.Replace("7YZ4VIUwn5c","OtherVideo1");
        check(!CalendarFeedStore.Valid(restored),"A cached thumbnail cannot switch to an unrelated YouTube video");
      }
      if(File.Exists(Path.Combine(folder,"live-stream-check")))using(var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false})) {
        var live=await new OfficialLivestreams(http).Fetch(null,DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(folder,"live-stream-result.json"),Json.Encode(live));
        check(live.Problem==null && live.Events.Any(e=>e.EuropeStartUnix==1795260600 && e.AsiaStartUnix==1795260600),"The current official RSS and watch metadata confirm the announced November 21 start");
      }
    }
  }
  sealed class LivestreamFixture:HttpMessageHandler {
    internal string Rss,Page;
    internal int Requests;
    internal HttpStatusCode Status=HttpStatusCode.OK;
    internal long? Length;
    internal LivestreamFixture(string rss,string page){Rss=rss;Page=page;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
      Requests++;
      string text=request.RequestUri.AbsolutePath=="/feeds/videos.xml"?Rss:
        request.RequestUri.AbsolutePath=="/watch"?Page:"{\"code\":0,\"data\":{\"total\":0,\"list\":[]}}";
      var response=new HttpResponseMessage(Status){Content=new StringContent(text,Encoding.UTF8)};
      if(Length.HasValue)response.Content.Headers.ContentLength=Length;
      return Task.FromResult(response);
    }
  }
}
