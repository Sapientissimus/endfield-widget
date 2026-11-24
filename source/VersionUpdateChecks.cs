using System;
using System.IO;
using System.Linq;
using System.Net.Http;
namespace EndfieldWidget {
  partial class Widget {
    static async System.Threading.Tasks.Task CheckUnreadableMaintenance(Action<bool,string> check,string folder) {
      using(Publications.NativeFixture("en-us"))
      using(var fixture=new CalendarBulletinFixture())
      using(var http=new HttpClient(fixture)) {
        var publication=new DateTimeOffset(2026,11,22,2,30,0,TimeSpan.Zero);
        const string name="Meeting You in the World o'Shrooms";
        string maintenanceTitle="["+name+"] Version Pre-Download & Update Notice";
        string announcement=NewsClient.NewsUrl+"/3835";
        string unknownTime=L.T("Exact time not announced");
        string unreadableTime=L.T("Could not read the maintenance schedule");
        fixture.Add("7011","Arknights: Endfield ["+name+"] Version DEV Comm",publication,
          "<p>New version ["+name+"] will officially launch on Nov. 25, 2026.</p>");
        var feed=await new BannerClient(http).FetchCalendar(null);
        var untouched=CalendarData.Entries(null,null,false,feed).Single();
        check(CalendarPresentation.Time(untouched)==unknownTime,
          "A date-only launch remains unannounced before an official maintenance notice exists");

        fixture.Add("3835",maintenanceTitle,publication.AddDays(1),
          "<p>The official maintenance schedule uses an unfamiliar layout.</p>");
        feed=await new BannerClient(http).FetchCalendar(feed);
        foreach(bool asia in new[]{false,true}) {
          var entry=CalendarData.Entries(null,null,asia,feed).Single();
          check(entry.DateOnly==new DateTime(2026,11,25)
            && CalendarPresentation.Time(entry)==unreadableTime
            && CalendarData.SourceUrl(entry)==announcement,
            "An unreadable maintenance notice labels the same preliminary date and links to that announcement without duplicates for "+(asia?"Asia":"Europe"));
        }
        var previous=Storage.DirectoryPath;
        Storage.DirectoryPath=Path.Combine(folder,"unreadable-maintenance-cache");
        try {
          CalendarFeedStore.Save(feed);
          feed=CalendarFeedStore.Load(null);
          var entry=CalendarData.Entries(null,null,false,feed).Single();
          check(CalendarPresentation.Time(entry)==unreadableTime && entry.Source==announcement,
            "The version-specific unreadable schedule and new source link survive cache reload");
        } finally {Storage.DirectoryPath=previous;}
        var selected=L.Code;
        try {
          L.Set("uk");
          check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,feed).Single())=="Не вдалося прочитати розклад",
            "The unreadable schedule state follows the selected interface language");
        } finally {L.Set(selected);}

        fixture.Failures["3835"]=System.Net.HttpStatusCode.ServiceUnavailable;
        var unavailable=await new BannerClient(http).FetchCalendar(feed);
        check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,unavailable).Single())==unreadableTime,
          "An unavailable identified maintenance notice still links its existing preliminary date to the official notice");
        fixture.Failures.Remove("3835");

        foreach(string pending in new[]{
          "<p>Maintenance hours: TBA</p>",
          "<p>The maintenance schedule will be announced later.</p>",
          "<p>▼// Version Maintenance Time</p><p>To be announced</p>"}) {
          fixture.SetBody("3835",pending);
          feed=await new BannerClient(http).FetchCalendar(feed);
          foreach(bool asia in new[]{false,true}) {
            var entry=CalendarData.Entries(null,null,asia,feed).Single();
            check(!feed.Partial && entry.DateOnly.HasValue && CalendarPresentation.Time(entry)==unknownTime,
              "An explicitly pending maintenance timetable remains unannounced instead of unreadable for "+(asia?"Asia":"Europe")+": "+pending);
          }
        }
        fixture.SetBody("3835","<p>Reward distribution: TBA.</p><p>The maintenance schedule uses an unfamiliar layout.</p>");
        feed=await new BannerClient(http).FetchCalendar(feed);
        check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,feed).Single())==unreadableTime,
          "TBA for unrelated rewards cannot hide an unreadable maintenance timetable");
        fixture.Add("3834",maintenanceTitle,publication.AddDays(2),"<p>Maintenance time: TBA.</p>");
        var newerPending=await new BannerClient(http).FetchCalendar(feed);
        check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,newerPending).Single())==unknownTime,
          "A newer explicitly pending timetable supersedes an older unreadable announcement of the same version");
        fixture.Remove("3834");

        fixture.SetBody("3835","<p>Americas / Europe Server: Nov. 24, 2026 at 17:00 – Nov. 24, 2026 at 23:00 (UTC-5)</p>");
        feed=await new BannerClient(http).FetchCalendar(feed);
        foreach(bool asia in new[]{false,true}) {
          var entry=CalendarData.Entries(null,null,asia,feed).Single();
          check(!entry.DateOnly.HasValue && entry.Source==announcement && CalendarPresentation.Time(entry)!=unreadableTime,
            "A successful retry replaces the unreadable date with one confirmed maintenance for "+(asia?"Asia":"Europe"));
        }
        fixture.SetBody("3835","<p>Maintenance hours: TBA.</p>");
        var pendingPrecise=await new BannerClient(http).FetchCalendar(feed);
        check(!CalendarData.Entries(null,null,false,pendingPrecise).Single().DateOnly.HasValue,
          "Pending wording never erases previously verified precise maintenance hours");
        fixture.SetBody("3835","<p>The schedule format changed temporarily.</p>");
        var unreadablePrecise=await new BannerClient(http).FetchCalendar(feed);
        check(!CalendarData.Entries(null,null,false,unreadablePrecise).Single().DateOnly.HasValue,
          "A temporary format failure never downgrades cached verified maintenance to a preliminary date");
        fixture.Failures["3835"]=System.Net.HttpStatusCode.ServiceUnavailable;
        var retained=await new BannerClient(http).FetchCalendar(feed);
        check(!CalendarData.Entries(null,null,false,retained).Single().DateOnly.HasValue,
          "A transient failure keeps a previously verified precise schedule");

        fixture.Remove("3835");
        var withdrawn=await new BannerClient(http).FetchCalendar(unavailable);
        check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,withdrawn).Single())==unknownTime,
          "A withdrawn maintenance announcement no longer marks the preliminary date unreadable");
        fixture.Add("3838",maintenanceTitle,publication.AddDays(-10),"<p>An old unreadable notice of the same name.</p>");
        var older=await new BannerClient(http).FetchCalendar(null);
        check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,older).Single())==unknownTime,
          "An older notice with the same title cannot mark a newly announced version date unreadable");
        fixture.Remove("3838");
        fixture.Add("3839","["+name+"] Version Update Notes",publication.AddDays(1),"<p>An unrelated version overview.</p>");
        fixture.Failures["3839"]=System.Net.HttpStatusCode.ServiceUnavailable;
        fixture.Add("3836","[Different Version] Version Pre-Download & Update Notice",publication.AddDays(1),"<p>Unreadable unrelated maintenance.</p>");
        fixture.Add("3837","[Another Event] Fun & Games Event Details",publication.AddDays(1),"<p>No event dates.</p>");
        fixture.Failures["3837"]=System.Net.HttpStatusCode.ServiceUnavailable;
        var unrelated=await new BannerClient(http).FetchCalendar(null);
        check(CalendarPresentation.Time(CalendarData.Entries(null,null,false,unrelated).Single())==unknownTime,
          "Failures of another version or an unrelated event never mark this version's schedule unreadable");
      }
    }

    void CheckUnreadableMaintenanceView(Action<bool,string> check,string folder) {
      var oldSchedule=schedule;var oldFeed=calendarFeed;var oldBanners=bannerState;var oldNow=calendarNow;var oldMask=calendarFilterMask;var oldQuery=calendarQuery;var oldLanguage=L.Code;var oldZone=Clock.ZoneOverride;var oldRegion=config.BannerRegion;var oldOpen=calendarOpenOverride;
      try {
        L.Set("en");Clock.ZoneOverride=()=>TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        var now=new DateTimeOffset(2026,11,23,10,0,0,TimeSpan.Zero);calendarNow=()=>now;schedule=null;bannerState=null;calendarQuery="";calendarFilterMask=CalendarData.AllKinds;calendarRemembered=false;
        const string name="Meeting You in the World o'Shrooms";string identity=CalendarData.Normalize(name),source=NewsClient.NewsUrl+"/3835";
        var preliminary=new VersionUpdateNotice{Name=name,Identity=identity,Source=NewsClient.NewsUrl+"/7011",Date="2026-11-25",Published=now.AddDays(-1).ToUnixTimeSeconds()};
        calendarFeed=CalendarFeedState.FromSources(new[]{new CalendarSourceState{Source=preliminary.Source,VersionUpdate=preliminary},new CalendarSourceState{Source=source,MaintenanceIdentity=identity,MaintenanceUnreadable=true,Published=now.ToUnixTimeSeconds(),Problem="format"}},now,1,true);
        ShowCalendar();SelectCalendarDay(new DateTime(2026,11,25));calendarDialog.UpdateLayout();
        check(calendarDayModels.Count==1 && calendarAgendaModels.Count==1 && calendarDayModels[0].TimeText=="Could not read the maintenance schedule" && calendarDayModels[0].PeriodText.Contains("Could not read the maintenance schedule"),"The rendered preliminary-date card directly shows the unreadable schedule instead of claiming no time was announced");
        string opened=null;calendarOpenOverride=url=>opened=url;
        calendarDayModels[0].OpenSource.Execute(null);
        check(opened==source && calendarDayModels[0].Source==source && CalendarDetailsData.Key(calendarDayModels[0].Entry).StartsWith(source+"|"),"The announcement action and details cache target the new maintenance announcement, not the old DEV Comm");
        config.BannerRegion="Asia";calendarQuery="World o'Shrooms";RenderCalendar();
        check(calendarDayModels.Count==1 && calendarAgendaModels.Count==1 && calendarDayModels[0].Source==source,"Version-name search and the Asia setting retain the single unreadable maintenance card with its new source");
        SaveImage(calendarDialog,Path.Combine(folder,"unreadable-maintenance-en.png"));
      }finally{calendarOpenOverride=oldOpen;if(calendarDialog!=null)calendarDialog.Close();schedule=oldSchedule;calendarFeed=oldFeed;bannerState=oldBanners;calendarNow=oldNow;calendarFilterMask=oldMask;calendarQuery=oldQuery;Clock.ZoneOverride=oldZone;config.BannerRegion=oldRegion;L.Set(oldLanguage);calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;}
    }

    static async System.Threading.Tasks.Task CheckPreDownloadNotice(Action<bool,string> check,string folder) {
      var canonical=File.ReadAllText(Path.Combine(folder,"localization","en-us-3835.json"));
      var translated=File.ReadAllText(Path.Combine(folder,"localization","ru-ru-3835.json"));
      var published=DateTimeOffset.FromUnixTimeSeconds(1795406400);
      using(Publications.Enter(Publications.ForLanguage("ru"))) {
        var projected=LocalizedArticles.Project(Json.Decode(canonical),Json.Decode(translated));
        check(Json.Str(projected,"data","title").Contains("Встретимся в грибном краю"),"A complete line-break-packed maintenance article retains its official localized version name");
        var schedule=NewsClient.ParseSchedule(Json.Str(projected,"data","data"),Json.Str(projected,"data","title"),NewsClient.NewsUrl+"/3835",published);
        check(schedule.Start==new DateTimeOffset(2026,11,24,22,0,0,TimeSpan.Zero) && schedule.End==new DateTimeOffset(2026,11,25,4,0,0,TimeSpan.Zero),"The real November pre-download notice yields exact maintenance UTC times");
        var asiaHtml=System.Text.RegularExpressions.Regex.Replace(Json.Str(Json.Decode(canonical),"data","data"),@"Americas / Europe Server:[^<]+","");
        var asiaSchedule=NewsClient.ParseSchedule(asiaHtml,Json.Str(Json.Decode(canonical),"data","title"),NewsClient.NewsUrl+"/3835",published);
        check(asiaSchedule.Start==schedule.Start && asiaSchedule.End==schedule.End,"The real Asia06:00–12:00 UTC+8 and Europe17:00–23:00 UTC-5 maintenance windows resolve to the same global interval");
        var pendingCanonical=Json.Decode(canonical);
        var pendingLocalized=Json.Decode(translated);
        var canonicalData=(System.Collections.Generic.Dictionary<string,object>)Json.At(pendingCanonical,"data");
        var localizedData=(System.Collections.Generic.Dictionary<string,object>)Json.At(pendingLocalized,"data");
        canonicalData["data"]="<p>Dear Endministrator.</p><p>Maintenance hours: TBA.</p><p>New version [Meeting You in the World o'Shrooms].</p>";
        localizedData["data"]="<p>Уважаемый Эндминистратор.</p><p>Часы технического обслуживания будут объявлены позже.</p><p>Новая версия «Встретимся в грибном краю».</p>";
        using(var fixture=new PreDownloadNoticeFixture(Json.Encode(pendingCanonical),Json.Encode(pendingLocalized)))
        using(var http=new HttpClient(fixture)) {
          var feed=await new BannerClient(http).FetchCalendar(null);
          var pendingSource=feed.Sources.Single();
          check(!feed.Partial && pendingSource.Undated && !pendingSource.MaintenanceUnreadable
            && pendingSource.MaintenanceIdentity=="meeting you in the world o'shrooms",
            "Canonical pending maintenance wording survives the selected Russian article projection without a false read error");
        }
        var broken=Json.Decode(translated);((System.Collections.Generic.Dictionary<string,object>)((System.Collections.Generic.Dictionary<string,object>)broken)["data"])["data"]="<p>Incomplete translation<br>One short line</p>";
        bool refused=false;try{LocalizedArticles.Project(Json.Decode(canonical),broken);}catch(FormatException){refused=true;}
        check(refused,"An actually incomplete translation is still rejected when the canonical article uses line breaks");
        using(var fixture=new PreDownloadNoticeFixture(canonical,translated))using(var http=new HttpClient(fixture)) {
          var client=new BannerClient(http);var precise=await client.FetchSchedule();
          var feed=await client.FetchCalendar(null);
          check(!feed.Partial && feed.Schedules.Count==1 && precise.Start==schedule.Start && precise.VersionIdentity=="meeting you in the world o'shrooms","Production fetch/projection accepts the real localized notice and preserves canonical maintenance identity");
          var notice=new VersionUpdateNotice{Name="Meeting You in the World o'Shrooms",Identity=precise.VersionIdentity,Source=NewsClient.NewsUrl+"/7011",Date="2026-11-25",Published=published.AddDays(-1).ToUnixTimeSeconds()};
          feed.Sources.Add(new CalendarSourceState{Source=notice.Source,VersionUpdate=notice});
          var entries=CalendarData.Entries(precise,null,false,feed);
          check(entries.Count==1 && !entries[0].DateOnly.HasValue && entries[0].Name=="Встретимся в грибном краю","The confirmed localized maintenance replaces an English preliminary notice without duplicates");
          var asiaEntries=CalendarData.Entries(precise,null,true,feed);
          check(asiaEntries.Count==1 && asiaEntries[0].At==entries[0].At && asiaEntries[0].End==entries[0].End,"Selecting Asia preserves the globally simultaneous maintenance and still removes the preliminary duplicate");
          var zone=Clock.ZoneOverride;
          try {
            Clock.ZoneOverride=()=>TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
            check(CalendarPresentation.Time(asiaEntries[0])=="06:00–12:00","An Asia Windows time zone displays the published Asia maintenance hours");
            Clock.ZoneOverride=()=>TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
            check(CalendarPresentation.Time(asiaEntries[0])=="00:00–06:00","Asia server selection keeps maintenance converted to the user's Kyiv Windows time zone");
          } finally {Clock.ZoneOverride=zone;}
        }
      }
    }

    void CheckPreDownloadNoticeView(Action<bool,string> check,string folder) {
      var oldSchedule=schedule;var oldFeed=calendarFeed;var oldBanners=bannerState;var oldNow=calendarNow;var oldMask=calendarFilterMask;var oldQuery=calendarQuery;var oldLanguage=L.Code;var oldZone=Clock.ZoneOverride;
      try {
        L.Set("en");Clock.ZoneOverride=()=>TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        var now=new DateTimeOffset(2026,11,23,10,0,0,TimeSpan.Zero);calendarNow=()=>now;bannerState=null;calendarQuery="";calendarFilterMask=CalendarData.AllKinds;calendarRemembered=false;
        var article=Json.Decode(File.ReadAllText(Path.Combine(folder,"localization","en-us-3835.json")));
        string title=Json.Str(article,"data","title"),source=NewsClient.NewsUrl+"/3835";
        schedule=NewsClient.ParseSchedule(Json.Str(article,"data","data"),title,source,now);schedule.VersionIdentity=VersionUpdates.Identity(title);
        var preliminary=new VersionUpdateNotice{Name=schedule.Version,Identity=schedule.VersionIdentity,Source=NewsClient.NewsUrl+"/7011",Date="2026-11-25",Published=now.AddDays(-1).ToUnixTimeSeconds()};
        calendarFeed=CalendarFeedState.FromSources(new[]{new CalendarSourceState{Source=preliminary.Source,VersionUpdate=preliminary},new CalendarSourceState{Source=source,Maintenance=schedule}},now,2,false);
        ShowCalendar();SelectCalendarDay(new DateTime(2026,11,25));calendarDialog.UpdateLayout();
        check(calendarDayModels.Count==1 && calendarAgendaModels.Count==1 && calendarDayModels[0].TimeText=="00:00–06:00" && !calendarDayModels[0].Entry.DateOnly.HasValue,"The rendered Kyiv calendar shows one confirmed November25 maintenance card at00:00–06:00, replacing the preliminary notice");
        SaveImage(calendarDialog,Path.Combine(folder,"maintenance-notice-en.png"));
      }finally{if(calendarDialog!=null)calendarDialog.Close();schedule=oldSchedule;calendarFeed=oldFeed;bannerState=oldBanners;calendarNow=oldNow;calendarFilterMask=oldMask;calendarQuery=oldQuery;Clock.ZoneOverride=oldZone;L.Set(oldLanguage);calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;}
    }

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
          var localNotice=VersionUpdates.Parse(Json.Str(projected,"data","data"),Json.Str(projected,"data","title"),Json.Str(projected,"data","canonicalTitle"),NewsClient.NewsUrl+"/7011",published.ToUnixTimeSeconds(),Json.Str(projected,"data","localizedTitle"));
          check(localNotice!=null && localNotice.Day==actualNotice.Day && localNotice.Identity==actualNotice.Identity && localNotice.Name=="Встреча в мире грибов","The official DEV Comm survives Russian name projection with its canonical version identity and unchanged date");
        }


        const string canonicalName="Meeting You in the World o'Shrooms";
        const string canonicalTitle="Arknights: Endfield ["+canonicalName+"] Version DEV Comm";
        string canonicalHtml="<p>New version ["+canonicalName+"] will officially launch on Nov. 25, 2026.</p>";
        var titleOnly=VersionUpdates.Parse(canonicalHtml,canonicalTitle,canonicalTitle,NewsClient.NewsUrl+"/7011",published.ToUnixTimeSeconds(),"Arknights: Endfield «Встреча в мире грибов» — заметки разработчиков");
        check(titleOnly.Name=="Встреча в мире грибов" && titleOnly.Identity==CalendarData.Normalize(canonicalName),"A localized official title supplies the display name even when the body remains English");
        foreach(var caption in new[]{"", "Developer communication", canonicalTitle}) {
          var fallback=VersionUpdates.Parse(canonicalHtml,canonicalTitle,canonicalTitle,NewsClient.NewsUrl+"/7011",published.ToUnixTimeSeconds(),caption);
          check(fallback.Name==canonicalName && fallback.Identity==titleOnly.Identity,"Missing or English official translation keeps the canonical display fallback: "+caption);
        }
        var renamed=VersionUpdates.Parse(canonicalHtml,canonicalTitle,canonicalTitle,NewsClient.NewsUrl+"/7011",published.ToUnixTimeSeconds(),"『アークナイツ：エンドフィールド』「キノコ菌グ！？君との大冒険！」バージョン開発通信");
        check(renamed.Name=="キノコ菌グ！？君との大冒険！" && renamed.Identity==titleOnly.Identity,"Japanese title punctuation is supported without changing canonical version identity");
        var localizedSource=new CalendarSourceState{Source=titleOnly.Source,VersionUpdate=titleOnly};
        string previousTitleStorage=Storage.DirectoryPath;Storage.DirectoryPath=Path.Combine(folder,"version-title-cache");
        try {
          CalendarFeedStore.Save(CalendarFeedState.FromSources(new[]{localizedSource},published,1,false));
          var restoredTitle=CalendarFeedStore.Load(null).Sources.Single().VersionUpdate;
          check(restoredTitle.Name==titleOnly.Name && restoredTitle.Identity==titleOnly.Identity,"Localized display name and canonical identity both survive cache reload");
          var full=new Schedule{Version="Other localized caption",VersionIdentity=titleOnly.Identity,Source=NewsClient.NewsUrl+"/7015",Start=published.AddDays(3),End=published.AddDays(3).AddHours(6)};
          check(CalendarData.Entries(full,null,false,CalendarFeedState.FromSources(new[]{localizedSource},published,1,false)).Count==1,"A title-localized preliminary entry is still replaced by the canonical full maintenance schedule");
        }finally{Storage.DirectoryPath=previousTitleStorage;}
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
        fixture.LocalizedTitles["7011"]="Arknights: Endfield «Встреча в мире грибов» — заметки разработчиков";
        feed=await new BannerClient(http).FetchCalendar(feed);
        check(feed.Sources.Single().VersionUpdate.Name=="Встреча в мире грибов" && feed.Sources.Single().VersionUpdate.Identity==titleOnly.Identity,"Refreshing an existing English notice uses the selected edition title without changing its identity");
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
  sealed class PreDownloadNoticeFixture:System.Net.Http.HttpMessageHandler {
    readonly string canonical,localized;
    public PreDownloadNoticeFixture(string en,string ru){canonical=en;localized=ru;}
    protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,System.Threading.CancellationToken cancellation) {
      string body;
      if(request.RequestUri.AbsolutePath.EndsWith("/api/bulletin")) {
        var item=Json.At(Json.Decode(canonical),"data");body=Json.Encode(new{code=0,data=new{total=1,list=new[]{item}}});
      } else if(request.RequestUri.AbsolutePath.EndsWith("/3835"))body=request.RequestUri.Query.Contains("lang=ru-ru")?localized:canonical;
      else return System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.NotFound){Content=new System.Net.Http.StringContent("")});
      return System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new System.Net.Http.StringContent(body,System.Text.Encoding.UTF8,"application/json")});
    }
  }

}
