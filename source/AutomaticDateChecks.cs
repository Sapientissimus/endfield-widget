using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckAutomaticDates(Action<bool,string> check) {
      using(var feed=new FutureBulletinFixture())using(var httpClient=new HttpClient(feed)) {
        var client=new BannerClient(httpClient);var archive=await client.Archive();
        check(archive.Count==1005 && archive.Any(a=>Json.Str(a,"cid")==feed.Cid) && feed.PagesRead==51,"An archive beyond 1000 announcements discovers an unknown future event on a later page");
        bool failed=false;var events=new List<GameEvent>();
        await client.AddGameEventArticles(archive,null,events,()=>failed=true);
        var expected=new DateTimeOffset(feed.EventEnd,TimeSpan.FromHours(-5)).ToUniversalTime();
        check(!failed && events.Count==1 && events[0].Name==feed.Name && events[0].Source==NewsClient.NewsUrl+"/"+feed.Cid && events[0].End(false)==expected && events[0].End(true)==new DateTimeOffset(feed.EventEnd,TimeSpan.FromHours(8)).ToUniversalTime(),"A newly discovered name, article ID and deadline years after the build are read without a bundled date table");
        var previous=new BannerState{GameEvents=events};var entries=CalendarData.Entries(null,previous,false);
        check(entries.Count==1 && entries[0].Title.EndsWith(feed.Name) && entries[0].At==expected,"An automatically discovered future deadline reaches the existing calendar");
        feed.EventEnd=feed.EventEnd.AddHours(3);failed=false;events=new List<GameEvent>();
        await client.AddGameEventArticles(archive,previous,events,()=>failed=true);
        check(!failed && events.Count==1 && events[0].End(false)==expected.AddHours(3),"A later refresh automatically replaces a corrected official deadline");
        previous.GameEvents=events;feed.Unavailable=true;failed=false;events=new List<GameEvent>();
        await client.AddGameEventArticles(archive,previous,events,()=>failed=true);
        check(failed && events.Count==1 && events[0].End(false)==expected.AddHours(3),"A temporary event-source failure keeps the last confirmed date available");
        feed.Unavailable=false;feed.EventEnd=feed.EventEnd.AddHours(2);failed=false;events=new List<GameEvent>();
        await client.AddGameEventArticles(archive,previous,events,()=>failed=true);
        check(!failed && events.Count==1 && events[0].End(false)==expected.AddHours(5),"The next refresh after source recovery picks up its correction without manual input");
        previous.GameEvents=events;feed.Unavailable=true;var partial=await client.Fetch(previous,Schedule.Seed(),false);var tick=DateTimeOffset.UtcNow;
        check(partial.EventLookupFailed && partial.GameEvents.Count==1 && BannerClient.NextRefresh(partial,tick,false)==tick.AddMinutes(5),"The full automatic update preserves a failed event source and schedules its next attempt in five minutes");
        feed.Unavailable=false;var recovered=await client.Fetch(partial,Schedule.Seed(),false);
        check(!recovered.EventLookupFailed && recovered.GameEvents.Count==1 && recovered.GameEvents[0].End(false)==expected.AddHours(5) && BannerClient.NextRefresh(recovered,tick,false)>tick.AddMinutes(5),"A successful full update restores the normal background interval without user action");
        previous.GameEvents=events;feed.UnknownEnd=true;failed=false;events=new List<GameEvent>();
        await client.AddGameEventArticles(archive,previous,events,()=>failed=true);
        check(!failed && events.Count==0,"An official revision that withdraws the exact deadline removes the outdated date automatically");
        feed.AnnounceNext();archive=await client.Archive();failed=false;events=new List<GameEvent>();
        await client.AddGameEventArticles(archive,previous,events,()=>failed=true);
        check(!failed && events.Count==1 && events[0].Name==feed.Name && events[0].Source!=previous.GameEvents[0].Source,"A subsequent unknown announcement is discovered from the feed without updating application code");
      }
    }
  }
  sealed class FutureBulletinFixture:HttpMessageHandler {
    readonly long firstId=1000000L+(uint)Guid.NewGuid().GetHashCode();
    readonly DateTimeOffset publication=DateTimeOffset.UtcNow.AddYears(3);
    int generation;
    public int PagesRead;
    public bool Unavailable,UnknownEnd;
    public DateTime EventEnd;
    public string Cid {get{return (firstId+1004+generation*10000).ToString(CultureInfo.InvariantCulture);}}
    public string Name {get{return "Future "+firstId.ToString(CultureInfo.InvariantCulture)+" "+generation.ToString(CultureInfo.InvariantCulture);}}
    public FutureBulletinFixture(){EventEnd=DateTime.SpecifyKind(publication.UtcDateTime.Date,DateTimeKind.Unspecified).AddDays(18).AddHours(4);}
    public void AnnounceNext(){generation++;UnknownEnd=false;EventEnd=EventEnd.AddDays(10);}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
      if(request.RequestUri.AbsolutePath.EndsWith("/api/bulletin",StringComparison.Ordinal)) {
        var match=Regex.Match(request.RequestUri.Query,@"[?&]page=(\d+)");int page=match.Success?int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture):1;PagesRead++;
        var articles=new List<object>();
        for(int index=(page-1)*20;index<Math.Min(page*20,1005);index++)articles.Add(new{cid=index==1004?Cid:(firstId+index).ToString(CultureInfo.InvariantCulture),title=index==1004?"["+Name+"] Challenge Event Details":index==1003?"[Automatic Current] Chartered Headhunting":"Information "+index,displayTime=index==1003?DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds():publication.ToUnixTimeSeconds()});
        return Response(HttpStatusCode.OK,Json.Encode(new{code=0,data=new{total=1005,list=articles}}));
      }
      if(request.RequestUri.AbsolutePath.EndsWith("/"+(firstId+1003).ToString(CultureInfo.InvariantCulture),StringComparison.Ordinal)) {
        var start=DateTime.UtcNow.Date.AddDays(-14);var end=DateTime.UtcNow.Date.AddDays(30);
        string html="<p>This banner features the Rate-UP 6\u2605 operator [Si].</p><p>Availability: "+start.ToString("MMM. d, yyyy 'at' HH:mm",CultureInfo.InvariantCulture)+" \u2013 "+end.ToString("MMM. d, yyyy 'at' HH:mm",CultureInfo.InvariantCulture)+" (server time)</p>";
        return Response(HttpStatusCode.OK,Json.Encode(new{code=0,data=new{data=html}}));
      }
      if(request.RequestUri.AbsolutePath.EndsWith("/"+Cid,StringComparison.Ordinal)) {
        if(Unavailable)return Response(HttpStatusCode.ServiceUnavailable,"unavailable");
        var start=DateTime.SpecifyKind(publication.UtcDateTime.Date,DateTimeKind.Unspecified).AddDays(1).AddHours(12);
        string end=UnknownEnd?"Before the next version update and maintenance":EventEnd.ToString("MMM. d, yyyy 'at' HH:mm",CultureInfo.InvariantCulture);
        string html="<p>Event Time: "+start.ToString("MMM. d, yyyy 'at' HH:mm",CultureInfo.InvariantCulture)+" \u2013 "+end+" (server time)</p><p>Asia Server Time Zone: UTC+8</p><p>Americas / Europe Server Time Zone: UTC-5</p>";
        return Response(HttpStatusCode.OK,Json.Encode(new{code=0,data=new{data=html}}));
      }
      return Response(HttpStatusCode.NotFound,"unexpected fixture request");
    }
    static Task<HttpResponseMessage> Response(HttpStatusCode status,string body){return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")});}
  }
}
