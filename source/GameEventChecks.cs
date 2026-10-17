using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckGameEventCalendar(Action<bool,string> check,string folder) {
      string html=Json.Str(Json.Decode(File.ReadAllText(Path.Combine(folder,"version-9560.json"))),"data","data");
      var parsed=GameEventDates.Parse(html,"[Sanctuary of Ink] Version Update Notes","9560",1792026000);
      check(parsed.Count==4 && parsed.Any(e=>e.Name=="All the Inky Hues") && parsed.Any(e=>e.Name=="Carly's Verdant Anthology") && parsed.Any(e=>e.Name=="Arena of Games") && parsed.Any(e=>e.Name=="New Era for the Myriad Souls"),"The actual official version notes provide four gameplay-event deadlines with explicit dates");
      check(!parsed.Any(e=>e.Name.Contains("Stoves") || e.Name.Contains("Restoration") || e.Name.Contains("Contingency")),"Unknown version-end dates, sign-in rewards and daily task campaigns are excluded");
      var camera=parsed.Single(e=>e.Name=="Carly's Verdant Anthology");
      check(camera.End(false)==new DateTimeOffset(2026,11,5,9,0,0,TimeSpan.Zero) && camera.End(true)==new DateTimeOffset(2026,11,4,20,0,0,TimeSpan.Zero),"The camera event retains distinct official Europe and Asia end instants");
      string offsets="<p>Asia Server Time Zone: UTC+8</p><p>Americas / Europe Server Time Zone: UTC-5</p>";
      Func<string,List<GameEvent>> simple=time=>GameEventDates.Parse("<p>▼// Event Time</p><p>"+time+"</p><p>▼// Event Rewards</p><p>Shop closes Dec. 10, 2026 at 12:00.</p>"+offsets,"[Test] Challenge Event Details","12345",1792026000);
      check(simple("Oct. 15, 2026 at 12:00 – Before the next version update and maintenance").Count==0,"An unspecified next-maintenance deadline never borrows a shop date");
      check(simple("Oct. 15, 2026 at 12:00 – Before the next version update and maintenance\nOther milestone: Nov. 5, 2026 at 04:00").Count==0,"An undated deadline cannot consume an unrelated timestamp on a later line");
      check(simple("Nov. 5, 2026 at 12:00 – Oct. 20, 2026 at 04:00").Count==0,"An event end before its explicitly announced start is rejected");
      check(simple("Oct. 15, 2026 at 12:00 – Nov. 5, 2026 at 04:00 (server time)").Single().End(false)==camera.End(false),"A dedicated event notice parses the gameplay deadline rather than a later shop closure");
      check(GameEventDates.Parse("<p>Event Time: Oct. 15, 2026 at 12:00 – Nov. 5, 2026 at 04:00 (server time)</p>","[Test] Challenge Event","12345",1792026000).Count==0,"Server-time dates without an explicit regional offset are not guessed");
      check(simple("Oct. 15, 2026 at 12:00 – Feb. 31, 2027 at 04:00").Count==0 && simple("Oct. 15, 2026 at 12:00 – Nov. 5, 2099 at 04:00").Count==0,"Invalid and implausibly distant event dates are rejected");
      check(!GameEventDates.Eligible("[Login] Sign-In Event") && !GameEventDates.Eligible("[Community] Web Event Details") && !GameEventDates.Eligible("[Banner] Chartered Headhunting") && !GameEventDates.Eligible("[Daily] Limited-Time Event"),"Community promotions, daily rewards and banners do not become large gameplay events");
      string regional="<p>Event Time:</p><p>Asia Server: Oct. 15, 2026 at 12:00 – Nov. 5, 2026 at 04:00</p><p>Americas / Europe Server: Oct. 14, 2026 at 23:00 – Nov. 4, 2026 at 18:00</p>"+offsets;
      var different=GameEventDates.Parse(regional,"[Regional] Challenge Event","12345",1792026000).Single();
      check(different.End(false)==new DateTimeOffset(2026,11,4,23,0,0,TimeSpan.Zero) && different.End(true)==new DateTimeOffset(2026,11,4,20,0,0,TimeSpan.Zero),"Region-specific date blocks cannot leak one server's deadline into the other");
      string maintenance="<p>Event Time</p><p>Asia Server: May 14, 2026 at 12:00 – Before version update and maintenance on June 5, 2026 at 06:00</p><p>Americas / Europe Server: May 14, 2026 at 12:00 – Before version update and maintenance on June 4, 2026 at 17:00</p>"+offsets;
      var fest=GameEventDates.Parse(maintenance,"[Party Together] - [Gift Box Castell] Fest Event Details","2668",1778731200).Single();
      check(fest.End(false)==new DateTimeOffset(2026,6,4,22,0,0,TimeSpan.Zero) && fest.End(true)==fest.End(false),"An explicitly dated maintenance-bound event end is accepted across different server calendar dates");
      var newYear=GameEventDates.Parse("<p>Event Time: Dec. 31 at 12:00 – Jan. 5 at 04:00</p>"+offsets,"[New year] Challenge Event","12345",new DateTimeOffset(2026,12,30,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds()).Single();
      check(newYear.End(false)==new DateTimeOffset(2027,1,5,9,0,0,TimeSpan.Zero),"A published year-end range without repeated years resolves into the next calendar year");
      var corrected=new GameEvent{Name=camera.Name.ToUpperInvariant(),Source=NewsClient.NewsUrl+"/12345",Published=camera.Published+1,EuropeEndUnix=camera.EuropeEndUnix+3600,AsiaEndUnix=camera.AsiaEndUnix+3600};
      check(GameEventDates.Latest(new[]{camera,corrected}).Count==1 && GameEventDates.Latest(new[]{camera,corrected}).Single().EuropeEndUnix==corrected.EuropeEndUnix,"A newer official correction replaces the same event without duplicate calendar entries");
      var state=new BannerState{Debuts=new List<Debut>(),GameEvents=new List<GameEvent>(parsed)};
      Storage.Write("event-roundtrip.json",state);var roundtrip=Storage.Read<BannerState>("event-roundtrip.json");
      check(roundtrip.GameEvents.Count==4 && roundtrip.GameEvents.Single(e=>e.Name==camera.Name).EuropeEndUnix==camera.EuropeEndUnix,"Confirmed event dates survive an isolated cache save and restart-shaped read");
      state.GameEvents.Add(new GameEvent{Name="Untrusted",Source="https://example.com/12345",EuropeEndUnix=camera.EuropeEndUnix});state.GameEvents.Add(new GameEvent{Name="Unknown",Source=NewsClient.NewsUrl+"/12345"});
      check(CalendarData.Entries(null,state,false).Count==4,"Unverified sources and unknown deadlines cannot enter the calendar from cached data");
      var previousZone=Clock.ZoneOverride;var oldBanners=bannerState;var oldSchedule=schedule;string oldRegion=config.BannerRegion;var oldNow=calendarNow;
      try {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");Clock.ZoneOverride=()=>zone;
        check(CalendarData.OnDay(CalendarData.Entries(null,state,true),new DateTime(2026,11,4)).Any(e=>e.Title.Contains(camera.Name)) && !CalendarData.OnDay(CalendarData.Entries(null,state,true),new DateTime(2026,11,5)).Any(e=>e.Title.Contains(camera.Name)),"Calendar end markers follow the local day for the selected game server");
        var utc=TimeZoneInfo.Utc;Clock.ZoneOverride=()=>utc;var entries=CalendarData.Entries(null,state,false);
        check(entries.All(e=>!e.End.HasValue && e.Title.StartsWith("Event ends · ")) && CalendarData.NearestDay(entries,new DateTimeOffset(2026,11,5,0,0,0,TimeSpan.Zero))==new DateTime(2026,11,5),"Only completion days are marked and existing nearest-event navigation finds them");
        bannerState=state;schedule=null;config.BannerRegion="Europe";calendarNow=()=>new DateTimeOffset(2026,11,1,0,0,0,TimeSpan.Zero);ShowCalendar();SelectCalendarDay(new DateTime(2026,11,5));calendarDialog.UpdateLayout();
        var panel=(ListBox)calendarDialog.FindName("CalendarEvents");
        check(panel.Items.OfType<CalendarCardModel>().Count()==2 && panel.Items.OfType<CalendarCardModel>().All(m=>m.Category=="Event ends"),"The actual calendar shows both official event endings on the same selected day");
        SaveImage(calendarDialog,Path.Combine(folder,"calendar-game-events.png"));calendarDialog.Close();
      }finally {if(calendarDialog!=null)calendarDialog.Close();Clock.ZoneOverride=previousZone;bannerState=oldBanners;schedule=oldSchedule;config.BannerRegion=oldRegion;calendarNow=oldNow;}
      var previous=new BannerState{GameEvents=new List<GameEvent>{new GameEvent{Name="Cached gameplay",Source=NewsClient.NewsUrl+"/12345",Published=1792026000,EuropeEndUnix=camera.EuropeEndUnix}}};
      var article=new Dictionary<string,object>{{"cid","12345"},{"title","[Cached gameplay] Challenge Event Details"},{"displayTime",1792026000L}};
      using(var client=new HttpClient(new EventArticleFixture(null))) {
        bool failed=false;var retained=new List<GameEvent>();await new BannerClient(client).AddGameEventArticles(new List<Dictionary<string,object>>{article},previous,retained,()=>failed=true);
        check(failed && retained.Count==1 && retained[0].EuropeEndUnix==camera.EuropeEndUnix,"An article download failure retains previously confirmed event dates");
      }
      using(var client=new HttpClient(new EventArticleFixture("<p>Event Time: After version update – Before the next version update and maintenance</p>"+offsets))) {
        bool failed=false;var updated=new List<GameEvent>();await new BannerClient(client).AddGameEventArticles(new List<Dictionary<string,object>>{article},previous,updated,()=>failed=true);
        check(!failed && updated.Count==0,"A successfully revised notice with no exact deadline does not retain its obsolete cached date");
      }
    }
  }
  sealed class EventArticleFixture:HttpMessageHandler {
    readonly string body;public EventArticleFixture(string value){body=value;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
      if(body==null)throw new HttpRequestException("Offline event fixture");
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Json.Encode(new{code=0,data=new{data=body}}),Encoding.UTF8,"application/json")});
    }
  }
}
