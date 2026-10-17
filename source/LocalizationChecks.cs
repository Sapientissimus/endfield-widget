using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    public void LocalizationTest(string reportPath) {
      window.Left=window.Top=-10000;window.Show();window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var report=new List<string>();Action<bool,string> check=(value,label)=>{if(!value)throw new Exception(label);report.Add("PASS: "+label);};
        try{await CheckLocalization(check,Path.GetDirectoryName(Path.GetFullPath(reportPath)));report.Add("ALL LOCALIZATION CHECKS PASSED. Offline official publications and an isolated profile only.");}
        catch(Exception error){report.Add("FAILED: "+error);Environment.ExitCode=1;}
        finally{if(settingsDialog!=null)settingsDialog.Close();if(calendarDialog!=null)calendarDialog.Close();File.WriteAllLines(reportPath,report);Exit();}
      }));
    }
    async Task CheckLocalization(Action<bool,string> check,string folder) {
      check(Storage.Isolated && startup is MemoryStartupRegistration,"Localization checks cannot use the personal profile or real startup registration");
      var originalCulture=System.Globalization.CultureInfo.CurrentCulture;var originalZone=TimeZoneInfo.Local.Id;var keys=new HashSet<string>(L.Catalog("en").Keys);
      check(L.Languages.Length==15 && L.Languages.Select(l=>l.Code).Distinct().Count()==15,"All fifteen interface languages have unique selectable entries");
      check(L.Normalize("PT-br")=="pt-BR" && L.Normalize("../../outside")=="en","Saved language codes normalize safely and cannot form cache paths");
      string previousLanguage=L.Code;L.Set("uk");
      check(Clock.After(TimeSpan.FromMinutes(1))=="1 хвилину" && Clock.After(TimeSpan.FromMinutes(21))=="21 хвилину" && Clock.After(TimeSpan.FromMinutes(22))=="22 хвилини" && Clock.After(TimeSpan.FromMinutes(14))=="14 хвилин","Case-sensitive duration forms remain correct in the selected locale");
      L.Set(previousLanguage);
      var oldFrames=new Config();oldFrames.PortraitFrames["jsspsi|full"]=new PortraitFrame{Zoom=1.21};check(oldFrames.Frame("si",false).Zoom==1.21 && ReferenceEquals(oldFrames.Frame("si",false),oldFrames.Frame("jsspsi",false)),"Portrait crop settings keep the same identity across official names and legacy bundled artwork");
      DemoReading();config.ProtectedToken="PROTECTED-LOCALIZATION-FIXTURE";config.Scale=.91;config.CompactScale=1.13;config.MicroScale=.83;config.PositionLocked=true;config.EnsureModeProfiles();
      string protectedToken=config.ProtectedToken,role=config.RoleId;int epoch=recovery.Epoch;
      SavePosition();
      Settings();settingsDialog.WindowStartupLocation=WindowStartupLocation.Manual;settingsDialog.Left=settingsDialog.Top=-10000;
      ((PasswordBox)settingsDialog.FindName("TokenInput")).Password="unsaved-offline-draft";
      var origin=new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero);string fixture=Path.Combine(folder,"localization");
      var enBanner=File.ReadAllText(Path.Combine(fixture,"en-us-4489.json"));var enVersion=File.ReadAllText(Path.Combine(fixture,"en-us-9560.json"));
      foreach(var language in L.Languages) {
        var catalog=L.Catalog(language.Code);check(keys.SetEquals(catalog.Keys) && catalog.Values.All(v=>v!=null && v.Length>0 && !v.Any(c=>char.IsControl(c) && c!='\r' && c!='\n' && c!='\t')),language.Code+": every UI key has a nonempty translation without invalid characters");
        var selector=(ComboBox)settingsDialog.FindName("LanguageSelector");check(selector.Items.Count==15,"The settings language selector contains all languages: "+language.Code);
        selector.SelectedValue=language.Code;await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        check((string)selector.SelectedValue==language.Code,language.Code+": selector selected value is present (actual="+(selector.SelectedValue??"null")+", item="+(selector.SelectedItem??"null")+")");
        check(config.InterfaceLanguage==language.Code && L.Code==language.Code && settingsDialog!=null && settingsDialog.IsVisible && (string)((ComboBox)settingsDialog.FindName("LanguageSelector")).SelectedValue==language.Code,language.Code+": the real selector switches the current settings window and persists its value (config="+config.InterfaceLanguage+", current="+L.Code+", busy="+refreshBusy+", settings="+(settingsDialog==null?"closed":settingsDialog.IsVisible.ToString())+")");
        check(((PasswordBox)settingsDialog.FindName("TokenInput")).Password=="unsaved-offline-draft",language.Code+": switching keeps the unsaved password draft in memory");
        check(config.ProtectedToken==protectedToken && config.RoleId==role && config.Scale==.91 && config.CompactScale==1.13 && config.MicroScale==.83 && config.PositionLocked && config.ModeProfiles.Count==3,language.Code+": account, three scales, placement lock and mode profiles remain unchanged");
        check(settingsDialog.Title==L.T("Settings · Endfield") && Find<TextBlock>("PlayLabel").Text==L.T("Play") && (string)compactMenu.Header==L.T("Compact mode"),language.Code+": main window, settings title and context menu use the selected language");
        var stored=Storage.Read<Config>("settings.json");check(stored.InterfaceLanguage==language.Code && stored.ProtectedToken==protectedToken,language.Code+": a restart-shaped settings read keeps language and protected account");
        check(Clock.After(TimeSpan.FromHours(11)).Contains(L.T("duration.hour.many")) && Clock.After(TimeSpan.FromHours(1)).Contains(L.T("duration.hour.accusative")),language.Code+": singular/plural duration labels follow the selected language");
        check(L.Culture.DateTimeFormat.Calendar is System.Globalization.GregorianCalendar && L.DateCaption(new DateTime(2026,10,15),true).Contains("2026"),language.Code+": UI dates retain Gregorian game years");
        using(Publications.Enter(Publications.ForLanguage(language.Code))) {
          string locale=Publications.Locale;check(language.Code=="uk"?locale=="en-us":language.Code=="zh-Hans"?locale=="zh-tw":locale!="en-us" || language.Code=="en",language.Code+": official edition matches the selected language policy");
          object banner=Json.Decode(enBanner),version=Json.Decode(enVersion);
          if(locale!="en-us") {
            banner=LocalizedArticles.Project(banner,Json.Decode(File.ReadAllText(Path.Combine(fixture,locale+"-4489.json"))));
            version=LocalizedArticles.Project(version,Json.Decode(File.ReadAllText(Path.Combine(fixture,locale+"-9560.json"))));
          }
          string body=Json.Str(banner,"data","data"),title=Json.Str(banner,"data","title");var notice=BannerClient.ParseNotice(body,title,"4489",1791972000);
          var scheduleValue=NewsClient.ParseSchedule(Json.Str(version,"data","data"),Json.Str(version,"data","title"),Publications.NewsUrl+"/9560",2026);
          var notices=BannerClient.ParseOverview(Json.Str(version,"data","data"),Json.Str(version,"data","title"),"9560",1792026000);
          var events=GameEventDates.Parse(Json.Str(version,"data","data"),Json.Str(version,"data","title"),"9560",1792026000);
          check(notice!=null && notices.Count==2 && events.Count==4 && !string.IsNullOrEmpty(notice.Name) && !notice.Name.Any(char.IsControl),language.Code+": same official IDs yield localized operators, two debuts and four actual gameplay events");
          check(scheduleValue.Start==origin.AddHours(-6) && scheduleValue.End==origin && events.All(e=>Publications.Official(e.Source) && e.Source.Contains("/"+locale+"/news/")),language.Code+": localization preserves exact UTC maintenance instants and matching official links");
          var entry=new CalendarEntry{Kind=CalendarKind.Debut,Name=notice.Name,At=origin,Source=Publications.NewsUrl+"/4489"};
          var detail=CalendarDetailsData.Parse(body,title,entry,DateTimeOffset.UtcNow);detail.Description=LocalizedArticles.TranslateExcerpt(banner,detail.Description,"\n\n");detail.Participation=LocalizedArticles.TranslateExcerpt(banner,detail.Participation,"\n");
          check(detail.Description.Length>0 && detail.Participation.Length>0 && detail.Description.Length<=1600 && detail.Participation.Length<=1000,language.Code+": descriptions and participation come from the selected official edition");
          CalendarDetailsStore.Save(detail);check(CalendarDetailsStore.Load().ContainsKey(detail.Key),language.Code+": detail cache is reusable only inside its publication namespace");
          var upcoming=notices.Single(n=>n.Name!=notice.Name);var start=NewsClient.ParseDate(upcoming.LocalStart,TimeSpan.FromHours(-5),2026).ToUniversalTime();
          schedule=scheduleValue;bannerState=new BannerState{GameEvents=events,Debuts=new List<Debut>{new Debut{Name=notice.Name,Banner=notice.Banner,Source=entry.Source,Key="si",EuropeStart=origin,AsiaStart=origin},new Debut{Name=upcoming.Name,Banner=upcoming.Banner,Source=Publications.NewsUrl+"/9560",Key="argent-flow",EuropeStart=start,AsiaStart=start}}};calendarFeed=null;bannersVerified=true;
          ApplyDebut(bannerState.Debuts[0]);check(Find<TextBlock>("OperatorName").Text==notice.Name.ToUpper(L.Culture),language.Code+": changing edition updates the name even when the portrait key stays the same");
          SetWidgetMode("full");Paint();window.UpdateLayout();SaveImage(window,Path.Combine(folder,"localized-full-"+language.Code+".png"));
          SetWidgetMode("compact");window.UpdateLayout();SaveImage(window,Path.Combine(folder,"localized-compact-"+language.Code+".png"));SetWidgetMode("full");
          SetWidgetMode("micro");window.UpdateLayout();SaveImage(window,Path.Combine(folder,"localized-micro-"+language.Code+".png"));SetWidgetMode("full");
          settingsDialog.UpdateLayout();SaveImage(settingsDialog,Path.Combine(folder,"localized-settings-"+language.Code+".png"));
          foreach(var expander in LanguageElements(settingsDialog).OfType<Expander>())expander.IsExpanded=true;
          var settingsScroll=(ScrollViewer)settingsDialog.FindName("SettingsScroll");settingsDialog.UpdateLayout();settingsScroll.ScrollToVerticalOffset(settingsScroll.ScrollableHeight*.5);await settingsDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);SaveImage(settingsDialog,Path.Combine(folder,"localized-settings-middle-"+language.Code+".png"));
          settingsScroll.ScrollToBottom();await settingsDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);SaveImage(settingsDialog,Path.Combine(folder,"localized-settings-bottom-"+language.Code+".png"));settingsScroll.ScrollToTop();
          ShowCalendar();calendarDialog.Width=960;calendarDialog.Height=790;SelectCalendarDay(origin.Date);calendarDialog.UpdateLayout();await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          check(CalendarFind<TextBlock>("CalendarSelected").Text==L.DateCaption(origin.Date,true) && CalendarFind<TextBlock>("CalendarSelectedCount").Text.Length>0,language.Code+": calendar labels and selected-day dates refresh with the interface");
          var debutModel=calendarDayModels.First(m=>m.Entry.Kind==CalendarKind.Debut);var detailForCard=new CalendarDetailRecord{PublicationCode=language.Code,Key=CalendarDetailsData.Key(debutModel.Entry),Source=detail.Source,Description=detail.Description,Participation=detail.Participation,Checked=DateTimeOffset.UtcNow};
          calendarDetailCache=new Dictionary<string,CalendarDetailRecord>{{detailForCard.Key,detailForCard}};debutModel.Details.IsExpanded=true;await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          check(debutModel.Details.Description==detail.Description && debutModel.Details.Participation==detail.Participation,language.Code+": an expanded native calendar card shows the selected official prose");
          CalendarFind<ListBox>("CalendarEvents").ScrollIntoView(debutModel);await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          SaveImage(calendarDialog,Path.Combine(folder,"localized-calendar-"+language.Code+".png"));calendarDialog.Close();
          if(language.Code=="zh-Hans")check(Publications.DisplayText("維護與帳號，幹員祀") == "维护与帐号，干员祀","Simplified Chinese is converted without garbage, control characters or a server change");
        }
        check(config.ProtectedToken==protectedToken && config.RoleId==role,language.Code+": public article refresh does not touch SKPORT credentials");
      }
      check(recovery.Epoch>epoch && System.Globalization.CultureInfo.CurrentCulture.Equals(originalCulture) && TimeZoneInfo.Local.Id==originalZone,"Language changes invalidate old responses without changing the Windows culture or time zone");
      var pending=new TaskCompletionSource<bool>();Task<string> captured;
      using(Publications.Enter(Publications.ForLanguage("de")))captured=CapturePublicationAfter(pending.Task);
      L.Set("fr");pending.SetResult(true);check(await captured=="de-de" && Publications.Locale=="fr-fr","Concurrent publication operations keep their captured language after a UI language change");
      check(Publications.Cache("calendar","../../outside")=="calendar-en.json" && Publications.Cache("calendar","uk")==Publications.Cache("calendar","en") && Publications.Cache("calendar","zh-Hans")!=Publications.Cache("calendar","zh-Hant"),"Cache paths are safe; Ukrainian shares English and Chinese scripts stay separate");
      var wrong=Json.Decode(enBanner);((Dictionary<string,object>)((Dictionary<string,object>)wrong)["data"])["cid"]="different-id";bool refused=false;
      try{LocalizedArticles.Project(Json.Decode(enBanner),wrong);}catch(FormatException){refused=true;}check(refused,"Different official article IDs cannot be combined into a translated event");
      var unfinished=Json.Decode(enBanner);((Dictionary<string,object>)((Dictionary<string,object>)unfinished)["data"])["data"]="<p>Unfinished translation</p>";refused=false;
      try{LocalizedArticles.Project(Json.Decode(enBanner),unfinished);}catch(FormatException){refused=true;}check(refused,"Incomplete official translations do not silently become English article prose");
      L.Set("de");var delayedEntry=new CalendarEntry{Kind=CalendarKind.Debut,Name="Si",At=origin,Source="https://endfield.gryphline.com/de-de/news/4489"};
      using(var handler=new LocalizationPublicationFixture(fixture,true,false))using(var client=new HttpClient(handler)) {
        var capturedClient=new BannerClient(client);var delayed=capturedClient.FetchCalendarDetail(delayedEntry);await handler.Started.Task;L.Set("ja");handler.Release.TrySetResult(true);var record=await delayed;
        check(record.PublicationCode=="de" && record.Description.StartsWith("[Erneuerung]",StringComparison.Ordinal) && handler.Editions.SequenceEqual(new[]{"en-us","de-de"}),"A delayed network request keeps its original official edition when the interface changes");
        CalendarDetailsStore.Save(record);check(CalendarDetailsStore.Load().Keys.All(k=>!k.Contains("/de-de/")),"Late German detail writes cannot contaminate the active Japanese cache");
      }
      L.Set("de");using(var handler=new LocalizationPublicationFixture(fixture,false,true))using(var client=new HttpClient(handler)) {
        refused=false;try{await new BannerClient(client).FetchCalendarDetail(delayedEntry);}catch(BulletinException error){refused=error.Problem=="translation";}
        check(refused && handler.Editions.SequenceEqual(new[]{"en-us","de-de"}),"An unavailable selected edition reports failure instead of displaying English descriptions");
      }
      CalendarFeedState oldFeed;using(Publications.Enter(Publications.ForLanguage("de")))oldFeed=CalendarFeedState.FromSources(new CalendarSourceState[0],DateTimeOffset.UtcNow,0,false);
      L.Set("ja");CalendarFeedStore.Save(oldFeed);L.Set("de");check(CalendarFeedStore.Load(null).PublicationCode=="de","Independent calendar cache writes retain the producer language across switches");
      L.Set(config.InterfaceLanguage);settingsDialog.Close();
    }
    static async Task<string> CapturePublicationAfter(Task pending){await pending;return Publications.Locale;}
  }
  sealed class LocalizationPublicationFixture:HttpMessageHandler {
    readonly string folder;readonly bool failTranslation;
    public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(),Release=new TaskCompletionSource<bool>();
    public readonly List<string> Editions=new List<string>();
    public LocalizationPublicationFixture(string path,bool pause,bool fail){folder=path;failTranslation=fail;if(!pause)Release.SetResult(true);}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken cancellationToken) {
      string query=message.RequestUri.Query;string edition=query.Contains("lang=de-de")?"de-de":"en-us";Editions.Add(edition);
      if(edition=="en-us"){Started.TrySetResult(true);await Release.Task;}
      if(edition=="de-de" && failTranslation)return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("")};
      return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(File.ReadAllText(Path.Combine(folder,edition+"-4489.json")),System.Text.Encoding.UTF8,"application/json")};
    }
  }
}
