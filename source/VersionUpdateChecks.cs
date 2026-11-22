using System;
using System.IO;
using System.Linq;
using System.Net.Http;
namespace EndfieldWidget {
  partial class Widget {
    void CheckDateOnlyVersionView(Action<bool,string> check,string folder) {
      var oldSchedule=schedule;var oldFeed=calendarFeed;var oldBanners=bannerState;var oldNow=calendarNow;var oldMask=calendarFilterMask;var oldQuery=calendarQuery;var oldLanguage=L.Code;var oldZone=Clock.ZoneOverride;
      try {
        L.Set("en");Clock.ZoneOverride=()=>TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        var now=new DateTimeOffset(2026,11,22,17,0,0,TimeSpan.Zero);calendarNow=()=>now;schedule=null;bannerState=null;calendarQuery="";calendarFilterMask=CalendarData.AllKinds;calendarRemembered=false;
        var article=Json.Decode(File.ReadAllText(Path.Combine(folder,"localization","en-us-7011.json")));
        var notice=VersionUpdates.Parse(Json.Str(article,"data","data"),Json.Str(article,"data","title"),null,NewsClient.NewsUrl+"/7011",now.ToUnixTimeSeconds());
        calendarFeed=CalendarFeedState.FromSources(new[]{new CalendarSourceState{Source=notice.Source,VersionUpdate=notice}},now,1,false);
        ShowCalendar();SelectCalendarDay(new DateTime(2026,11,25));calendarDialog.UpdateLayout();
        check(calendarDayModels.Count==1 && calendarAgendaModels.Count==1 && calendarDayModels[0].TimeText=="Exact time not announced" && calendarDayModels[0].Category=="Version update","The rendered month, selected-day cards and upcoming agenda show one preliminary version update");
        SaveImage(calendarDialog,Path.Combine(folder,"version-update-en.png"));
      }finally{if(calendarDialog!=null)calendarDialog.Close();schedule=oldSchedule;calendarFeed=oldFeed;bannerState=oldBanners;calendarNow=oldNow;calendarFilterMask=oldMask;calendarQuery=oldQuery;Clock.ZoneOverride=oldZone;L.Set(oldLanguage);calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;}
    }

    static async System.Threading.Tasks.Task CheckDateOnlyVersionUpdates(Action<bool,string> check,string folder) {
      using(Publications.NativeFixture("en-us"))
      using(var fixture=new CalendarBulletinFixture())using(var http=new HttpClient(fixture)) {
        var published=new DateTimeOffset(2026,11,22,2,30,0,TimeSpan.Zero);
        var actual=Json.Decode(File.ReadAllText(Path.Combine(folder,"localization","en-us-7011.json")));
        var actualNotice=VersionUpdates.Parse(Json.Str(actual,"data","data"),Json.Str(actual,"data","title"),null,NewsClient.NewsUrl+"/7011",published.ToUnixTimeSeconds());
        check(actualNotice!=null && actualNotice.Day==new DateTime(2026,11,25),"The captured official 7011 DEV Comm yields the announced November 25 update date");
        var localizedArticle=Json.Decode(File.ReadAllText(Path.Combine(folder,"localization","en-us-7011.json")).Replace("Meeting You in the World o'Shrooms","Встреча в мире грибов"));
        using(Publications.Enter(Publications.ForLanguage("ru"))) {
          var projected=LocalizedArticles.Project(actual,localizedArticle);
          var localNotice=VersionUpdates.Parse(Json.Str(projected,"data","data"),Json.Str(projected,"data","title"),Json.Str(projected,"data","canonicalTitle"),NewsClient.NewsUrl+"/7011",published.ToUnixTimeSeconds());
          check(localNotice!=null && localNotice.Day==actualNotice.Day && localNotice.Identity==actualNotice.Identity && localNotice.Name=="Встреча в мире грибов","The official DEV Comm survives Russian name projection with its canonical version identity and unchanged date");
        }


        const string name="Meeting You in the World o'Shrooms";
        fixture.Add("7011","Arknights: Endfield ["+name+"] Version DEV Comm",published,"<p>Dear Endministrator,</p><p>Arknights: Endfield new version ["+name+"] will officially launch on Nov. 25, 2026.</p><p>Planned improvements follow.</p>");
        var feed=await new BannerClient(http).FetchCalendar(null);
        var entries=CalendarData.Entries(null,null,false,feed);
        check(entries.Count==1 && entries[0].Name==name,"A DEV Comm with a date-only version launch enters the calendar");
        var entry=entries.Single();
        check(entry.DateOnly==new DateTime(2026,11,25) && !entry.End.HasValue && !entry.RangeEnd.HasValue && CalendarPresentation.Time(entry)=="Exact time not announced" && CalendarPresentation.Status(entry,published)=="","A preliminary update shows a calendar date without fabricated hours, end or countdown");
        var savedZone=Clock.ZoneOverride;
        try {
          foreach(var zone in new[]{"UTC","Eastern Standard Time","Tokyo Standard Time"}) {
            var value=TimeZoneInfo.FindSystemTimeZoneById(zone);Clock.ZoneOverride=()=>value;
            var snapshot=new CalendarSnapshot(entries,CalendarData.AllKinds,"");
            check(snapshot.OnDay(new DateTime(2026,11,25)).Count==1 && snapshot.NearestDay(published)==new DateTime(2026,11,25) && snapshot.Agenda(published,30).Count()==1,"Date-only update stays on November 25 across time zones: "+zone);
            var model=new CalendarCardModel(entry,published,new CalendarSourceCommand(e=>{}));
            check(model.Category=="Version update" && !model.PeriodText.Contains("00:00") && !model.TimeText.Contains("00:00"),"Card details do not expose an artificial midnight: "+zone);
          }
        }finally{Clock.ZoneOverride=savedZone;}
        string previous=Storage.DirectoryPath;Storage.DirectoryPath=Path.Combine(folder,"version-update-cache");
        try{CalendarFeedStore.Save(feed);var restored=CalendarFeedStore.Load(null);check(CalendarData.Entries(null,null,false,restored).Single().DateOnly==entry.DateOnly,"Date-only version notice survives cache reload");}finally{Storage.DirectoryPath=previous;}
        fixture.Add("7014","["+name+"] Version Trailer",published.AddHours(1),"<p>New version ["+name+"] will officially launch on Nov. 25, 2026.</p>");
        var repeated=await new BannerClient(http).FetchCalendar(feed);
        check(CalendarData.Entries(null,null,false,repeated).Count==1,"Repeated announcements of one version are deduplicated by version identity");
        fixture.Add("7015","["+name+"] Version Pre-Download & Update Notice",published.AddDays(1),"<p>Americas / Europe Server: Nov. 24, 2026 at 17:00 – Nov. 24, 2026 at 23:00 (UTC-5)</p>");
        var precise=await new BannerClient(http).FetchCalendar(repeated);var resolved=CalendarData.Entries(null,null,false,precise);
        check(resolved.Count==1 && !resolved[0].DateOnly.HasValue && resolved[0].End.HasValue,"Full maintenance supersedes preliminary notices of the same version even when its UTC day differs");
        var localized=feed.Sources.Single().VersionUpdate;localized.Name="Зустріч у світі грибів";
        var schedule=precise.Schedules.Single();schedule.Version="Інша офіційна локалізована назва";
        check(CalendarData.Entries(schedule,null,false,feed).Count==1 && !CalendarData.Entries(schedule,null,false,feed)[0].DateOnly.HasValue,"Canonical version identity reconciles localized or renamed display captions");
        schedule.VersionIdentity="different version";
        check(CalendarData.Entries(schedule,null,false,feed).Count==2,"An unrelated maintenance never suppresses a preliminary version date");
        check(VersionUpdates.Parse("<p>Rewards can be claimed until Nov. 25, 2026.</p>","[Other] New Version Guide",null,NewsClient.NewsUrl+"/7016",published.ToUnixTimeSeconds())==null,"A reward deadline is not mistaken for the version launch");
        check(VersionUpdates.Parse("<p>New version [Other] will officially launch on Feb. 30, 2027.</p>","[Other] Version DEV Comm",null,NewsClient.NewsUrl+"/7016",published.ToUnixTimeSeconds())==null,"Invalid dates cannot enter the preliminary calendar");

      }
    }
  }
}
