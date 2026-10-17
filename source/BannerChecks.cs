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
    async Task CheckBannerOverview(Action<bool,string> check,string folder) {
      string fixture=Path.Combine(folder,"version-9560.json");
      string html=File.Exists(fixture)?Json.Str(Json.Decode(File.ReadAllText(fixture,Encoding.UTF8)),"data","data"):OverviewFixture.DefaultHtml;
      const string title="[Sanctuary of Ink] Version Update Notes";
      const long published=1792026000;
      var notices=BannerClient.ParseOverview(html,title,"9560",published);
      check(notices.Count==2 && notices.Any(n=>n.Name=="Si") && notices.Any(n=>n.Name=="Argent Flow"),"Version update notes identify both new operators before separate banner articles exist");
      check(!notices.Any(n=>n.Name=="Tangtang" || n.Name=="Lunar Calling"),"Overview parsing ignores reruns, weapon issues and sign-in event dates");
      var argent=notices.Single(n=>n.Name=="Argent Flow");
      var expectedEurope=new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero);
      check(argent.StartHint==expectedEurope.ToUnixTimeSeconds() && argent.AsiaLocalStart==argent.LocalStart,"The generic server-time date is retained and converted with the Europe server offset");
      check(notices.Single(n=>n.Name=="Si").ReleaseVersion=="Sanctuary of Ink","The release-day banner remains tied to version maintenance rather than a later event date");
      check(BannerClient.ParseOverview(html,"[Example] Chartered Headhunting","9560",published).Count==0,"Only version-update documents are interpreted as multi-banner overviews");
      var refined=new BannerNotice {Name="Argent Flow",Banner=argent.Banner,Cid="1000",Published=new DateTimeOffset(2026,11,3,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds(),LocalStart="Nov. 4, 2026 at 23:00",AsiaLocalStart="Nov. 5, 2026 at 12:00"};
      check(BannerClient.FirstDebuts(new[]{argent,refined}).Single().Cid=="1000","A dedicated first-banner notice can refine the earlier version overview");
      var repeat=new BannerNotice {Name="Argent Flow",Banner=argent.Banner,Cid="1001",Published=new DateTimeOffset(2026,12,1,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds(),LocalStart="Dec. 5, 2026 at 12:00"};
      check(BannerClient.FirstDebuts(new[]{argent,repeat}).Single().Overview,"A later returning Chartered banner cannot replace the original overview debut");
      var originalSi=new BannerNotice {Name="Si",Banner="Rejuvenation",Cid="4489",Published=1791972000,ReleaseVersion="Sanctuary of Ink"};
      check(BannerClient.FirstDebuts(notices.Concat(new[]{originalSi})).Single(n=>n.Name=="Si").Cid=="4489","A known original detailed announcement retains priority over a later overview");
      string regional="<p>■ New Operators</p><p>6★ operator [Regional]</p><p>■ New Headhunting Events</p><p>1. [Regional debut] Chartered Headhunting</p><p>Availability:</p><p>Americas / Europe Server: Nov. 4, 2026 at 23:00 – Nov. 26, 2026 at 11:59 (server time)</p><p>Asia Server: Nov. 5, 2026 at 12:00 – Nov. 26, 2026 at 11:59 (server time)</p><p>Headhunting features the Rate-UP 6★ operator [Regional].</p><p>2. [Other issue] Arsenal Issue</p><p>Availability: Dec. 1, 2026 at 12:00 – Dec. 5, 2026 at 12:00 (server time)</p>";
      var regionalNotice=BannerClient.ParseOverview(regional,title,"2000",published).Single();
      check(regionalNotice.LocalStart.StartsWith("Nov. 4") && regionalNotice.AsiaLocalStart.StartsWith("Nov. 5"),"Explicit regional dates in an overview remain separate from adjacent issue dates");
      BannerState data;
      using(var fixtureHttp=new HttpClient(new OverviewFixture(html)))data=await new BannerClient(fixtureHttp).Fetch(BannerState.Seed(),Schedule.Seed(),false);
      var before=new DateTimeOffset(2026,10,15,12,0,0,TimeSpan.Zero);
      check(BannerClient.Next(data,before,false).Name=="Argent Flow" && BannerClient.Next(data,before,false).EuropeStart==expectedEurope,"The complete discovery pipeline finds Argent Flow from an overview-only archive");
      check(Clock.Date(expectedEurope)=="11/5/2026" && Clock.Time(expectedEurope)=="19:00" && Clock.Time(BannerClient.Next(data,before,true).AsiaStart)=="06:00","Both servers use Eastern European winter time on the announced November date");
      check(BannerClient.Select(data,expectedEurope.AddSeconds(-1),false).Name=="Si" && BannerClient.Select(data,expectedEurope,false).Name=="Argent Flow","Overview dates never switch the current portrait ahead of the actual debut boundary");
      var savedState=bannerState;bool savedVerified=bannersVerified,savedFailure=bannerLookupFailed,savedCompact=config.Compact;
      try {
        bannerState=data;bannersVerified=true;bannerLookupFailed=false;
        ApplyDebut(BannerClient.Select(data,before,false));PaintUpcoming(before);window.UpdateLayout();
        check(Find<TextBlock>("NextOperator").Text=="Argent Flow" && Find<TextBlock>("NextBannerDate").Text.Contains("11/5/2026") && currentArtKey=="si","The widget shows the announced next operator while retaining Si and her palette");
        PaintUpcoming(DateTimeOffset.UtcNow);
        config.Compact=false;ApplyLayout(true);SaveImage(window,Path.Combine(folder,"widget-argent-countdown.png"));
        config.Compact=true;ApplyLayout(true);SaveImage(window,Path.Combine(folder,"compact-argent-countdown.png"));
        bannersVerified=false;bannerLookupFailed=true;PaintUpcoming(before);
        check(Find<TextBlock>("NextOperator").Text=="Argent Flow","An update failure retains a previously confirmed next debut");
        bannerState=BannerState.Seed();PaintUpcoming(before);
        check(Find<TextBlock>("NextOperator").Text=="Could not update the date","An unsuccessful lookup does not falsely claim that no debut was announced");
      }finally{bannerState=savedState;bannersVerified=savedVerified;bannerLookupFailed=savedFailure;config.Compact=savedCompact;Paint();ApplyLayout(true);}
    }
  }
  sealed class OverviewFixture:HttpMessageHandler {
    readonly string body;
    public const string DefaultHtml="<p>■ New Operators</p><p>6★ operators [Si], [Argent Flow]</p><p>■ New Headhunting Events</p><p>1. [Rejuvenation] Chartered Headhunting</p><p>Availability: After [Sanctuary of Ink] version update – Nov. 5, 2026 at 11:59 (server time)</p><p>Headhunting features the Rate-UP 6★ operator [Si].</p><p>2. [Repeat] RE-Factor Headhunting #1</p><p>Availability: Oct. 29, 2026 at 12:00 – Nov. 19, 2026 at 11:59 (server time)</p><p>Headhunting features the Rate-UP 6★ operator [Tangtang].</p><p>3. [The Hearth Fire Beckons the Starry Flow] Chartered Headhunting</p><p>Availability: Nov. 5, 2026 at 12:00 (server time) – Before version update and maintenance</p><p>Headhunting features the Rate-UP 6★ operator [Argent Flow].</p><p>4. [Moon] Arsenal Issue</p><p>Availability: Nov. 7, 2026 at 12:00 – Nov. 30, 2026 at 12:00 (server time)</p><p>■ New Events</p><p>1. Sign-In Event</p><p>Availability: Oct. 20, 2026 at 12:00 – Nov. 30, 2026 at 12:00 (server time)</p>";
    public OverviewFixture(string html){body=html;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
      string result;
      if(request.RequestUri.AbsolutePath=="/api/bulletin")result=Json.Encode(new {code=0,data=new {total=1,list=new[]{new {cid="9560",title="[Sanctuary of Ink] Version Update Notes",displayTime=1792026000}}}});
      else if(request.RequestUri.AbsolutePath=="/api/bulletin/9560")result=Json.Encode(new {code=0,data=new {data=body}});
      else throw new InvalidOperationException("Unexpected fixture request: "+request.RequestUri.AbsolutePath);
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(result,Encoding.UTF8,"application/json")});
    }
  }
}
