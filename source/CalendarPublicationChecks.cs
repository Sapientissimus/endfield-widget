using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndfieldWidget {
  partial class Widget {
    static async Task CheckExpandedPublication(Action<bool,string> check,string folder) {
      var article=Json.Decode(File.ReadAllText(Path.Combine(folder,"localization","en-us-9560.json")));
      var published=DateTimeOffset.FromUnixTimeSeconds(1792026000);
      string[] canonicalTiming;
      using(Publications.NativeFixture("en-us"))
      using(var fixture=new CalendarBulletinFixture())
      using(var http=new HttpClient(fixture)) {
        fixture.Add("9560",Json.Str(article,"data","title"),published,Json.Str(article,"data","data"));
        var feed=await new BannerClient(http).FetchCalendar(null);
        canonicalTiming=CalendarTiming(feed.Events);
        check(Json.Encode(feed.Sources.Single()).Contains("https://web-static.hg-cdn.com/upload/image/20261014/5f866ee19d5419dd3628218ab2176b6a.jpg"),"The calendar keeps the official overview illustration with its article metadata");
        check(feed.Sources.Single().IsVersionOverview,"The version overview supplies the background candidate independently of its event cards");
        var names=feed.Events.Select(e=>e.Name).ToList();
        check(names.Contains("Ancestral Spring Flows Anew") && names.Contains("Tender Sprig Issue") && names.Contains("Sanity Supply") && names.Contains("Back to the Rustics") && names.Contains("Secret Sanctuary") && names.Contains("Season of Fervor"),"Official version notes supply reruns, weapon issues, resource events, starts, exchanges and seasons");
        var guide=feed.Events.Single(e=>e.Name=="All the Inky Hues");
        check(guide.EuropeStartUnix==CalendarUtc(10,15,4) && guide.AsiaStartUnix==guide.EuropeStartUnix,"A release-bound guide starts at the official end of the version's maintenance");
        var supply=feed.Events.Single(e=>e.Category==GameEventCategory.Supply);
        check(supply.EuropeStartUnix==CalendarUtc(11,19,9) && supply.EuropeEndUnix==CalendarUtc(11,25,22) && supply.AsiaStartUnix==CalendarUtc(11,18,20) && supply.AsiaEndUnix==CalendarUtc(11,25,20),"Sanity Supply uses both exact regional windows published in the article");
        var narrative=feed.Events.Single(e=>e.Name=="Back to the Rustics");
        var debut=feed.Events.Single(e=>e.Name=="Argent Flow" && e.Category==GameEventCategory.Debut);
        check(narrative.EuropeStartUnix==CalendarUtc(11,5,17) && narrative.EuropeEndUnix==0 && debut.EuropeEndUnix==0,"A published start survives without a guessed end before the next maintenance");
        check(feed.Events.Any(e=>e.Name=="Arena of Games" && e.Category==GameEventCategory.ContentUpdate && e.EuropeStartUnix==CalendarUtc(11,11,9)) &&
          feed.Events.Any(e=>e.Category==GameEventCategory.Area) && feed.Events.Any(e=>e.Name=="Delver of the Cryptic" && e.Category==GameEventCategory.ContentUpdate && e.EuropeStartUnix==CalendarUtc(11,19,9)),"Version notes retain separately dated content stages and the area opening");
        var before=feed.Sources.Single().Maintenance;
        var undated=new Schedule{Version="Future without a time",Source=NewsClient.NewsUrl+"/90001",Start=DateTimeOffset.MinValue,End=DateTimeOffset.MinValue};
        check(narrative.WithMaintenanceEnd(new[]{before,undated}).EuropeEndUnix==0,"Past maintenance and date-only announcements cannot fill an unknown end");
        var next=new Schedule{Version="Next",Source=NewsClient.NewsUrl+"/90002",Start=DateTimeOffset.FromUnixTimeSeconds(CalendarUtc(12,2,20)),End=DateTimeOffset.FromUnixTimeSeconds(CalendarUtc(12,3,2))};
        var refreshed=CalendarFeedState.FromSources(feed.Sources.Concat(new[]{new CalendarSourceState{Source=next.Source,Maintenance=next}}),DateTimeOffset.UtcNow,2,false);
        check(refreshed.Events.Single(e=>e.Name==narrative.Name).EuropeEndUnix==CalendarUtc(12,2,20) &&
          refreshed.Events.Single(e=>e.Name==debut.Name && e.Category==GameEventCategory.Debut).EuropeEndUnix==CalendarUtc(12,2,20) &&
          refreshed.Events.Single(e=>e.Category==GameEventCategory.Supply).EuropeEndUnix==supply.EuropeEndUnix,"Confirmed next maintenance resolves linked ends without changing the separate Sanity Supply deadline");
        fixture.Add("90003","[Late Signal Issue] LTO Details",published,"<p>Availability: Nov. 5, 2026 at 12:00 (server time); ends after 3 Chartered Headhunting banners.</p><p>Asia Server Time Zone: UTC+8</p><p>Americas / Europe Server Time Zone: UTC-5</p>");
        var dedicated=(await new BannerClient(http).FetchCalendar(feed)).Events.Single(e=>e.Name=="Late Signal Issue");
        check(dedicated.Category==GameEventCategory.Weapon && dedicated.EuropeStartUnix==CalendarUtc(11,5,17) && dedicated.EuropeEndUnix==0,"A standalone official-style LTO weapon notice contributes its exact opening without guessing the third banner's end");
      }
      using(Publications.Enter(Publications.ForLanguage("ru"))) {
        var localized=Json.Decode(File.ReadAllText(Path.Combine(folder,"localization","ru-ru-9560.json")));
        var projected=LocalizedArticles.Project(article,localized);
        check(CalendarArtwork.IsOfficial(Json.Str(projected,"data","calendarArtwork")),"Article projection retains official artwork metadata when localized prose is rebuilt");
        string title=Json.Str(projected,"data","title"),html=Json.Str(projected,"data","data");
        var schedule=NewsClient.ParseSchedule(html,title,NewsClient.NewsUrl+"/9560",published);
        var events=GameEventDates.ParseCalendar(html,title,"9560",published.ToUnixTimeSeconds(),schedule);
        check(events.Any(e=>e.Category==GameEventCategory.Supply && e.EuropeEndUnix==CalendarUtc(11,25,22)) &&
          events.Any(e=>e.Name=="Серебристый Поток" && e.Category==GameEventCategory.Debut),"Production Russian projection preserves the resource schedule and localized debut identity");
        var names=events.Select(e=>e.Name).ToList();
        check(names.Contains("Чрезвычайный контракт: экспериментальная операция „Индиго“") && names.Contains("Доступность тайного святилища") &&
          names.Contains("Сезон фантазий") && names.Contains("Сезон рвения") && names.Contains("Улин — картина Си") &&
          names.All(n=>!Regex.IsMatch(n,@"[A-Za-z]")),"All version-calendar records retain their official Russian names, including nested quotes and unquoted headings: "+string.Join(", ",names.Where(n=>Regex.IsMatch(n,@"[A-Za-z]"))));
        check(canonicalTiming.SequenceEqual(CalendarTiming(events)),"Localization leaves every regional calendar start, end and maintenance relation unchanged");
      }
      const string official="https://web-static.hg-cdn.com/upload/calendar.png";
      check(CalendarArtwork.Find("https://untrusted.example/cover.jpg","<script><img src='https://web-static.hg-cdn.com/hidden.jpg'></script><img src='https://web-static.hg-cdn.com.evil.example/trick.jpg'><img src='"+official+"'>")==official &&
        CalendarArtwork.Find("http://web-static.hg-cdn.com/cover.jpg","<img src='https://web-static.hg-cdn.com:444/cover.jpg'>")==null,"Artwork selection accepts only published HTTPS images on the exact official host and port");
      var jpeg=new JpegBitmapEncoder();
      jpeg.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1400,8,96,96,PixelFormats.Bgr24,null,new byte[1400*8*3],1400*3)));
      using(var file=new MemoryStream()) {
        jpeg.Save(file);file.Position=0;
        var picture=CalendarArtwork.Decode(file) as BitmapSource;
        check(picture!=null && picture.IsFrozen && picture.PixelWidth==1200,"A JPEG cover is decoded once at a bounded display size and can be reused by WPF");
        using(var handler=new CalendarArtworkFixture(file.ToArray()))using(var http=new HttpClient(handler)) {
          check(await CalendarArtwork.Download(http,official)!=null,"The artwork loader renders official image bytes through the normal download path");
          handler.Status=HttpStatusCode.Redirect;
          check(await CalendarArtwork.Download(http,official)==null,"An image redirect leaves the dark fallback intact");
          handler.Status=HttpStatusCode.OK;handler.Length=3000001;
          check(await CalendarArtwork.Download(http,official)==null,"Oversized artwork is rejected before decoding");
        }
      }
    }
    static string[] CalendarTiming(IEnumerable<GameEvent> entries) {
      return entries.Select(e=>string.Join("|",e.Category,e.EuropeStartUnix,e.EuropeEndUnix,e.AsiaStartUnix,e.AsiaEndUnix,e.EuropeEndAtMaintenance,e.AsiaEndAtMaintenance))
        .OrderBy(key=>key,StringComparer.Ordinal).ToArray();
    }
    static long CalendarUtc(int month,int day,int hour){return new DateTimeOffset(2026,month,day,hour,0,0,TimeSpan.Zero).ToUnixTimeSeconds();}
  }
  sealed class CalendarArtworkFixture:HttpMessageHandler {
    readonly byte[] bytes;
    internal HttpStatusCode Status=HttpStatusCode.OK;
    internal long? Length;
    internal CalendarArtworkFixture(byte[] value){bytes=value;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
      var response=new HttpResponseMessage(Status){Content=new ByteArrayContent(bytes)};
      response.Content.Headers.ContentType=new MediaTypeHeaderValue("image/jpeg");
      if(Length.HasValue)response.Content.Headers.ContentLength=Length;
      return Task.FromResult(response);
    }
  }
}
