using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EndfieldWidget {
  partial class Widget {
    async Task AuditContinuation(string kind,string fixtures) {
      if(kind=="next-timer")AuditTimerContinuation();
      else if(kind=="next-account")await AuditAccountContinuation();
      else if(kind=="next-language")await AuditLanguageContinuation();
      else if(kind=="next-details")await AuditDetailsContinuation();
      else if(kind=="next-wire")await AuditWireContinuation(fixtures);
      else if(kind=="next-years")await AuditYearContinuation();
      else throw new ArgumentException("Unknown continuation group.");
    }
    async Task AuditIdle(){await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);}
    void AuditSettings(){Settings();settingsDialog.WindowStartupLocation=WindowStartupLocation.Manual;settingsDialog.Left=settingsDialog.Top=-10000;}
    void AuditTimerContinuation() {
      DemoReading();awaitingFreshSanity=false;var now=DateTimeOffset.UtcNow;sanity.Checked=now.AddSeconds(-2);sanity.FullAt=long.MaxValue/1000;
      bool survived=false;try{var interval=BackgroundInterval(now);survived=interval.TotalMilliseconds>=50 && interval.TotalSeconds<=15;}catch(ArgumentOutOfRangeException){}
      AuditHarness.Check(survived && sanity.LocalFullAt()==null,"B01","An invalid live recovery timestamp cannot crash adaptive timer scheduling");
      sanity.FullAt=now.AddHours(2).ToUnixTimeSeconds();sanity.ServerTime=sanity.Checked.ToUnixTimeSeconds();
      var valid=BackgroundInterval(now);AuditHarness.Check(valid.TotalMilliseconds>=50 && valid.TotalSeconds<=15,"B01","A valid resource reading retains bounded adaptive scheduling");
    }
    async Task AuditAccountContinuation() {
      DemoReading();config.RoleId="audit-alpha";availableRoles=new List<GameRole>{new GameRole{RoleId="audit-alpha",ServerId="2",Name="Alpha"},new GameRole{RoleId="audit-beta",ServerId="2",Name="Beta"}};AuditSettings();
      var choice=(ComboBox)settingsDialog.FindName("CharacterSelector");refreshBusy=true;choice.SelectedIndex=1;
      AuditHarness.Check(config.RoleId=="audit-alpha" && ((GameRole)choice.SelectedItem).RoleId=="audit-alpha","B02","A character selection during another refresh cannot falsely appear applied");
      refreshBusy=false;settingsDialog.Close();availableRoles=null;
      using(var handler=new NextAuditHttp(request=>{
        string path=request.RequestUri.AbsolutePath;
        if(path=="/user/oauth2/v2/grant")return "{\"status\":0,\"data\":{\"code\":\"audit-grant\"}}";
        if(path=="/web/v1/user/auth/generate_cred_by_code")return "{\"code\":0,\"data\":{\"cred\":\"audit-cred\",\"token\":\"audit-signing-key\",\"userId\":\"audit-user\"}}";
        if(path=="/api/v1/game/player/binding")return "{\"code\":0,\"data\":{\"list\":[{\"appCode\":\"endfield\",\"bindingList\":[{\"uid\":\"audit-uid\",\"roles\":[{\"roleId\":\"audit-alpha\",\"serverId\":\"2\",\"nickname\":\"Alpha\"},{\"roleId\":\"audit-beta\",\"serverId\":\"2\",\"nickname\":\"Beta\"}]}]}]}}";
        if(path=="/api/v1/game/endfield/card/detail")return Json.Encode(new {code=0,timestamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),data=new {detail=new {dungeon=new {curStamina=80,maxStamina=360,maxTs=DateTimeOffset.UtcNow.AddHours(20).ToUnixTimeSeconds()},@base=new {name="Offline audit"}}}});
        throw new InvalidOperationException("Unexpected offline endpoint.");
      }))using(var client=new HttpClient(handler)) {
        skport=new SkportClient(client);AuditSettings();await RefreshSanity();
        choice=(ComboBox)settingsDialog.FindName("CharacterSelector");
        AuditHarness.Check(availableRoles!=null && availableRoles.Count==2 && choice.Items.Count==2 && ((GameRole)choice.SelectedItem).RoleId==config.RoleId && ((StackPanel)settingsDialog.FindName("CharacterRow")).Visibility==Visibility.Visible,"B03","An already open settings window receives roles discovered by a background account refresh");
        settingsDialog.Close();
        handler.BindingGate=new TaskCompletionSource<bool>();
        var field=typeof(Widget).GetField("http",BindingFlags.Instance|BindingFlags.NonPublic);var previousHttp=field.GetValue(this);field.SetValue(this,client);
        try {
          string beforeToken=accountToken,beforeProtected=config.ProtectedToken;AuditSettings();((PasswordBox)settingsDialog.FindName("TokenInput")).Password="offline-audit-new-account";
          ((Button)settingsDialog.FindName("ConnectAccountButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
          ApplyPowerState(PowerModes.Suspend,DateTimeOffset.UtcNow);handler.BindingGate.SetResult(true);
          for(int i=0;i<100 && refreshBusy;i++)await Task.Delay(10);await AuditIdle();
          AuditHarness.Check(!refreshBusy && accountToken==beforeToken && config.ProtectedToken==beforeProtected,"B11","An interrupted connection cannot replace the existing account with a pre-suspend login response");
          ApplyPowerState(PowerModes.Resume,DateTimeOffset.UtcNow);settingsDialog.Close();
        }finally{field.SetValue(this,previousHttp);}
      }
    }
    async Task AuditLanguageContinuation() {
      DemoReading();AuditSettings();var choice=(ComboBox)settingsDialog.FindName("LanguageSelector");choice.SelectedValue="en";choice.SelectedValue="ja";await AuditIdle();
      AuditHarness.Check(config.InterfaceLanguage=="ja" && (string)((ComboBox)settingsDialog.FindName("LanguageSelector")).SelectedValue=="ja","B04","Rapid queued language choices leave the active settings window in the latest chosen language");
      AuditHarness.Check(Application.Current.Windows.Cast<Window>().Count(w=>w.IsVisible && w!=window)==1,"B04","Coalesced language choices leave exactly one settings window");
      ChangeInterfaceLanguage("uk",settingsDialog);choice=(ComboBox)settingsDialog.FindName("LanguageSelector");choice.SelectedValue="en";refreshBusy=true;await AuditIdle();
      AuditHarness.Check(config.InterfaceLanguage=="uk" && (string)((ComboBox)settingsDialog.FindName("LanguageSelector")).SelectedValue=="uk","B05","A queued language change rechecks a refresh that started before its dispatcher callback");
      refreshBusy=false;settingsDialog.Close();
    }
    async Task AuditDetailsContinuation() {
      DemoReading();ShowCalendar();var completion=new TaskCompletionSource<CalendarDetailRecord>();calendarDetailFetchOverride=e=>completion.Task;
      var entry=new CalendarEntry{Kind=CalendarKind.Maintenance,Name="Recovery fixture",Source=NewsClient.NewsUrl+"/9560",At=DateTimeOffset.UtcNow.AddDays(1)};
      var state=CalendarDetailFor(entry);state.IsExpanded=true;ApplyPowerState(PowerModes.Suspend,DateTimeOffset.UtcNow);
      completion.SetResult(new CalendarDetailRecord{Key=CalendarDetailsData.Key(entry),Source=entry.Source,Description="PRE-RECOVERY-AUDIT",Checked=DateTimeOffset.UtcNow});await AuditIdle();await Task.Delay(60);
      AuditHarness.Check(state.Description!="PRE-RECOVERY-AUDIT" && !state.Busy && state.RetryVisibility==Visibility.Visible,"B06","Pre-suspend calendar details are rejected and leave a usable retry instead of a stuck loader");
      AuditHarness.Check(!CalendarDetailsStore.Load().Values.Any(r=>r.Description=="PRE-RECOVERY-AUDIT"),"B06","Rejected pre-recovery detail data is not saved as a fresh description");
      ApplyPowerState(PowerModes.Resume,DateTimeOffset.UtcNow);calendarDetailFetchOverride=e=>Task.FromResult(new CalendarDetailRecord{Key=CalendarDetailsData.Key(e),Source=e.Source,Description="AFTER-RECOVERY-AUDIT",Checked=DateTimeOffset.UtcNow});
      state.Retry.Execute(null);await AuditIdle();AuditHarness.Check(state.Description=="AFTER-RECOVERY-AUDIT" && !state.Busy,"B06","A fresh calendar detail request succeeds after recovery");
      calendarDialog.Close();calendarDetailFetchOverride=null;
      using(var handler=new NextAuditHttp(_=>"{\"code\":0,\"data\":{\"total\":0,\"list\":[]}}"))using(var client=new HttpClient(handler)) {
        banners=new BannerClient(client){CacheEnabled=true};news=new NewsClient(client,banners);await banners.Archive();RequestRecovery(DateTimeOffset.UtcNow);await banners.Archive();
        AuditHarness.Check(handler.Requests==2,"B07","Power/network recovery invalidates the shared short-lived publication cache");
      }
    }
    async Task AuditWireContinuation(string fixtures) {
      var now=DateTimeOffset.UtcNow;var retained=new GameEvent{Name="Retained fixture",Source=NewsClient.NewsUrl+"/9560",Published=now.ToUnixTimeSeconds(),EuropeEndUnix=now.AddDays(1).ToUnixTimeSeconds(),AsiaEndUnix=now.AddDays(1).ToUnixTimeSeconds()};
      var previous=CalendarFeedState.FromSources(new[]{new CalendarSourceState{Source=retained.Source,Events=new List<GameEvent>{retained},LastSuccess=now}},now,1,false);
      using(var handler=new NextAuditHttp(_=>"{\"code\":0,\"data\":{\"total\":0,\"list\":null}}"))using(var client=new HttpClient(handler)) {
        var source=new BannerClient(client){CacheEnabled=true};bool rejected=false;try{await source.Archive();}catch(FormatException){rejected=true;}
        AuditHarness.Check(rejected,"B08","A null archive list is not accepted as a legitimate empty official history");
        var feed=await source.FetchCalendar(previous);AuditHarness.Check(feed.Partial && feed.Events.Count==1 && feed.Events[0].Name==retained.Name,"B08","Malformed archive responses preserve known calendar dates as stale data");
      }
      var payload=Json.Decode(File.ReadAllText(Path.Combine(fixtures,"localization","en-us-9560.json")));((Dictionary<string,object>)Json.At(payload,"data"))["cid"]="999999";
      using(Publications.NativeFixture("en-us"))using(var handler=new NextAuditHttp(request=>request.RequestUri.AbsolutePath=="/api/bulletin"?Json.Encode(new{code=0,data=new{total=1,list=new[]{new{cid="9560",title="[Fixture] Version Update Notes",displayTime=now.ToUnixTimeSeconds()}}}}):Json.Encode(payload)))using(var client=new HttpClient(handler)) {
        bool rejected=false;try{await new BannerClient(client).FetchSchedule();}catch(FormatException){rejected=true;}
        AuditHarness.Check(rejected,"B09","An explicitly mismatched bulletin ID cannot supply dates for the requested announcement");
      }
      ((Dictionary<string,object>)Json.At(payload,"data"))["cid"]="9560";
      using(Publications.NativeFixture("en-us"))using(var handler=new NextAuditHttp(request=>request.RequestUri.AbsolutePath=="/api/bulletin"?Json.Encode(new{code=0,data=new{total=1,list=new[]{new{cid="09560",title="[Fixture] Version Update Notes",displayTime=now.ToUnixTimeSeconds()}}}}):Json.Encode(payload)))using(var client=new HttpClient(handler)) {
        var value=await new BannerClient(client).FetchSchedule();AuditHarness.Check(value.Source.EndsWith("/09560",StringComparison.Ordinal),"B09","Equivalent numeric bulletin IDs with leading zeros remain valid");
      }
    }
    async Task AuditYearContinuation() {
      var publication=new DateTimeOffset(2026,12,20,12,0,0,TimeSpan.Zero);string source=NewsClient.NewsUrl+"/910000";
      bool maintenance=false;try{var value=NewsClient.ParseSchedule("<p>Americas / Europe Server: December 31 at 23:00 – January 1 at 02:00 (UTC-5)</p>","[Year transition] Version Update Notes",source,2026);maintenance=value.Start==new DateTimeOffset(2027,1,1,4,0,0,TimeSpan.Zero) && value.End==new DateTimeOffset(2027,1,1,7,0,0,TimeSpan.Zero);}catch(FormatException){}
      AuditHarness.Check(maintenance,"B10","An explicitly dated maintenance interval can cross New Year without an explicit end year");
      bool explicitRejected=false;try{NewsClient.ParseSchedule("<p>Americas / Europe Server: December 31, 2026 at 23:00 – January 1, 2026 at 02:00 (UTC-5)</p>","[Year transition] Version Update Notes",source,2026);}catch(FormatException){explicitRejected=true;}
      AuditHarness.Check(explicitRejected,"B10","An explicit past end year is rejected rather than silently rolled into a different year");
      using(Publications.NativeFixture("en-us")) {
        string debut="<p>Rate-UP 6★ operator [Si]</p><p>Availability: January 5 at 12:00 – January 25 at 11:59</p>";
        using(var handler=new NextAuditHttp(request=>request.RequestUri.AbsolutePath=="/api/bulletin"?NextArchive("910000","[New Dawn] Chartered Headhunting",publication):NextArticle("910000","[New Dawn] Chartered Headhunting",debut)))using(var client=new HttpClient(handler)) {
          var state=await new BannerClient(client).Fetch(new BannerState{Notices=new List<BannerNotice>(),Debuts=new List<Debut>()},Schedule.Seed(),false);var next=state.Debuts.Single();
          AuditHarness.Check(next.EuropeStart==new DateTimeOffset(2027,1,5,17,0,0,TimeSpan.Zero) && next.AsiaStart==new DateTimeOffset(2027,1,5,4,0,0,TimeSpan.Zero) && BannerClient.End(next,false).Value.Year==2027,"B10","A January debut announced in December uses the upcoming year for both server regions and its ending");
        }
        using(var handler=new NextAuditHttp(request=>request.RequestUri.AbsolutePath=="/api/bulletin"?NextArchive("910003","[New Dawn] Chartered Headhunting",publication):NextArticle("910003","[New Dawn] Chartered Headhunting",debut.Replace("January 5 at","January 5, 2026 at").Replace("January 25 at","January 25, 2026 at"))))using(var client=new HttpClient(handler)) {
          var state=await new BannerClient(client).Fetch(new BannerState{Notices=new List<BannerNotice>(),Debuts=new List<Debut>()},Schedule.Seed(),false);
          AuditHarness.Check(state.Debuts.Single().EuropeStart.Year==2026,"B10","An explicit debut year stays authoritative even in a December publication");
        }
        string schedule="<p>Americas / Europe Server: January 5 at 01:00 – January 5 at 07:00 (UTC-5)</p>";
        using(var handler=new NextAuditHttp(request=>request.RequestUri.AbsolutePath=="/api/bulletin"?NextArchive("910001","[Year transition] Version Update Notes",publication):NextArticle("910001","[Year transition] Version Update Notes",schedule)))using(var client=new HttpClient(handler)) {
          var value=await new BannerClient(client).FetchSchedule();AuditHarness.Check(value.Start.Year==2027 && value.End.Year==2027,"B10","A January maintenance announced in December resolves its implicit year from publication context");
        }
        string html="<p>Event Time:</p><p>January 5 at 12:00 – January 25 at 11:59</p><p>■ Event Details:</p><p>Offline fixture.</p><p>Asia Server: UTC+8</p><p>Americas / Europe Server: UTC-5</p>";
        var events=GameEventDates.Parse(html,"[New Year Fixture] Narrative Event","910002",publication.ToUnixTimeSeconds());
        AuditHarness.Note("year-boundary events="+events.Count+", dates="+string.Join(";",events.Select(e=>e.EuropeStartUnix+".."+e.EuropeEndUnix)));
        AuditHarness.Check(events.Count==1 && events[0].EuropeStartUnix==new DateTimeOffset(2027,1,5,17,0,0,TimeSpan.Zero).ToUnixTimeSeconds() && events[0].End(false).Value.Year==2027,"B10","A January event announced in December retains both its start and end dates");
      }
    }
    static string NextArchive(string cid,string title,DateTimeOffset publication){return Json.Encode(new{code=0,data=new{total=1,list=new[]{new{cid=cid,title=title,displayTime=publication.ToUnixTimeSeconds()}}}});}
    static string NextArticle(string cid,string title,string html){return Json.Encode(new{code=0,data=new{cid=cid,title=title,data=html}});}
  }
  sealed class NextAuditHttp:HttpMessageHandler {
    readonly Func<HttpRequestMessage,string> response;internal int Requests;
    internal TaskCompletionSource<bool> BindingGate;
    public NextAuditHttp(Func<HttpRequestMessage,string> value){response=value;}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;if(BindingGate!=null && request.RequestUri.AbsolutePath=="/api/v1/game/player/binding")await BindingGate.Task.ConfigureAwait(false);return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(response(request),Encoding.UTF8,"application/json")};}
  }
}
