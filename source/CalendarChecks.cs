using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    [DllImport("user32.dll",EntryPoint="SendMessageW")] static extern IntPtr CalendarNativeMessage(IntPtr window,int message,IntPtr argument,IntPtr data);
    public void CalendarTest(string reportPath) {
      window.Show();window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var publicationFixture=Publications.NativeFixture("ru-ru");
        var report=new List<string>();Action<bool,string> check=(passed,label)=>{if(!passed)throw new Exception(label);report.Add("PASS: "+label);};
        try{await CheckCalendarUpgrade(check,Path.GetDirectoryName(Path.GetFullPath(reportPath)));report.Add("ALL CALENDAR CHECKS PASSED. Isolated profile, local HTTP fixtures and simulated DPI/timezones only.");}
        catch(Exception error){report.Add("FAILED: "+error);Environment.ExitCode=1;}
        finally{publicationFixture.Dispose();File.WriteAllLines(reportPath,report,Encoding.UTF8);Exit();}
      }));
    }
    async Task CheckCalendarUpgrade(Action<bool,string> check,string folder) {
      CheckCalendarDomain(check);
      await CheckCalendarSources(check,folder);
      await CheckCalendarViews(check,folder);
      await CheckCalendarDetailsAndMotion(check,folder);
      await CheckCalendarScrolling(check,folder);
    }
    static GameEvent CalendarEvent(string name,string cid,DateTimeOffset publication,DateTimeOffset start,DateTimeOffset end) {
      return new GameEvent{Name=name,Source=NewsClient.NewsUrl+"/"+cid,Published=publication.ToUnixTimeSeconds(),EuropeStartUnix=start.ToUnixTimeSeconds(),AsiaStartUnix=start.ToUnixTimeSeconds(),EuropeEndUnix=end.ToUnixTimeSeconds(),AsiaEndUnix=end.ToUnixTimeSeconds()};
    }
    static void CheckCalendarDomain(Action<bool,string> check) {
      var savedZone=Clock.ZoneOverride;
      try {
        Clock.ZoneOverride=()=>TimeZoneInfo.Utc;var start=new DateTimeOffset(2028,2,28,23,0,0,TimeSpan.Zero);var end=start.AddHours(1);
        var night=new CalendarEntry{Id="midnight",Kind=CalendarKind.Maintenance,Name="Midnight",Title="Midnight",Source=NewsClient.NewsUrl+"/12345",At=start,End=end};
        var midnight=new CalendarSnapshot(new[]{night},15,"");
        check(midnight.OnDay(start.Date).Count==1 && midnight.OnDay(end.Date).Count==0,"A maintenance interval ending at midnight does not occupy the following day");
        night.End=end.AddMinutes(1);midnight=new CalendarSnapshot(new[]{night},15,"");
        check(midnight.OnDay(end.Date).Count==1,"One minute past midnight correctly occupies the following leap day");
        check(CalendarSnapshot.FirstDay(new DateTime(2028,2,1)).DayOfWeek==DayOfWeek.Monday,"The leap-year month grid starts on Monday");
        var publication=new DateTimeOffset(2026,10,15,0,0,0,TimeSpan.Zero);
        var a=CalendarEvent("A  recurring event","10001",publication,publication.AddDays(1),publication.AddDays(20));
        var corrected=CalendarEvent("a recurring EVENT","10002",publication.AddHours(1),publication.AddDays(1),publication.AddDays(21));
        var first=GameEventDates.Latest(new[]{a});string identity=first[0].Identity;
        var merged=GameEventDates.Latest(new[]{a,corrected});
        check(merged.Count==1 && merged[0].EuropeEndUnix==corrected.EuropeEndUnix && merged[0].Identity==identity,"Corrected dates keep a stable identity across whitespace, case and official articles");
        var repeat=CalendarEvent("A recurring event","10003",publication.AddDays(80),publication.AddDays(81),publication.AddDays(100));
        check(GameEventDates.Latest(new[]{a,corrected,repeat}).Count==2,"A later gameplay event with the same name remains a distinct occurrence");
        check(CalendarData.Entries(null,new BannerState{GameEvents=new List<GameEvent>{a,corrected,repeat}},false).Select(e=>e.Id).Distinct().Count()==2,"Distinct recurring gameplay events retain separate calendar identifiers");
        var bad=new Schedule{Version="Invalid",Source=NewsClient.NewsUrl+"/12345",Start=publication,End=publication.AddMinutes(-1)};
        check(CalendarData.Entries(bad,null,false).Count==0,"A reversed official interval cannot enter a calendar snapshot");
        bad.End=publication.AddHours(1);bad.Source="https://example.com/12345";
        check(CalendarData.Entries(bad,null,false).Count==0,"A valid-looking timestamp from an untrusted source is rejected");
        var entries=new[]{new CalendarEntry{Id="one",Name="Carly's   Anthology",Title="Camera",Kind=CalendarKind.EventEnd,Source=NewsClient.NewsUrl+"/12345",At=publication.AddDays(2)},new CalendarEntry{Id="two",Name="Operator",Title="Debut",Kind=CalendarKind.Debut,Source=NewsClient.NewsUrl+"/12346",At=publication.AddDays(3)}};
        var filtered=new CalendarSnapshot(entries,8,"CARLY’S anthology");
        check(filtered.Entries.Count==1 && filtered.OnDay(entries[0].At.Date).Count==1 && filtered.Agenda(publication,30).Count()==1,"Search normalization and category filtering agree in the month grid and future agenda");
        check(filtered.NearestDay(publication)==entries[0].At.Date,"Nearest-event navigation uses the same filtered data as the grid");
        check(new CalendarSnapshot(entries,0,"").Entries.Count==0 && new CalendarSnapshot(entries,15,"missing").Entries.Count==0,"Disabled categories and an empty search result are real empty views");
        CalendarEntry lastIncluded=new CalendarEntry{Id="edge-in",At=publication.AddDays(30).AddTicks(-1),Kind=CalendarKind.EventEnd},firstExcluded=new CalendarEntry{Id="edge-out",At=publication.AddDays(30),Kind=CalendarKind.EventEnd};
        check(new CalendarSnapshot(new[]{lastIncluded,firstExcluded},15,"").Agenda(publication,30).Select(e=>e.Id).SequenceEqual(new[]{"edge-in"}),"A 30-day local agenda includes thirty dates rather than an extra thirty-first day");
        check(CalendarData.NearestDay(new[]{night},night.End.Value)==null,"Maintenance ending exactly now is no longer treated as ongoing");
        var order=CalendarData.Ordered(new[]{entries[0],new CalendarEntry{Id="three",Name="Maintenance",Title="Maintenance",Kind=CalendarKind.Maintenance,At=entries[0].At}}).ToList();
        check(order[0].Kind==CalendarKind.Maintenance,"Simultaneous events use a stable category order");
        check(CalendarPresentation.Status(night,start.AddMinutes(30)).StartsWith("Ongoing") && CalendarPresentation.Status(entries[0],entries[0].At.AddMinutes(1))=="Ended","Cards distinguish an active interval from a completed deadline");
        check(CalendarPresentation.Status(entries[1],entries[1].At.AddMinutes(1))=="Debut has started","A completed debut has a clear event-specific status");
        check(CalendarPresentation.Status(entries[0],entries[0].At.AddHours(-11)).Contains("11 hours"),"Calendar relative timing uses the correct default-language plural for eleven hours");
        var moments=new[]{new DateTimeOffset(2026,10,14,22,30,0,TimeSpan.Zero),new DateTimeOffset(2026,10,25,0,59,0,TimeSpan.Zero),new DateTimeOffset(2026,10,25,1,1,0,TimeSpan.Zero),new DateTimeOffset(2028,2,29,23,59,0,TimeSpan.Zero),new DateTimeOffset(2026,12,31,23,59,0,TimeSpan.Zero)};
        foreach(string id in new[]{"UTC","FLE Standard Time","Eastern Standard Time","India Standard Time","Tokyo Standard Time","Lord Howe Standard Time"}) {
          var zone=TimeZoneInfo.FindSystemTimeZoneById(id);Clock.ZoneOverride=()=>zone;
          foreach(var moment in moments) {
            var entry=new CalendarEntry{Id="zone",Name="Timezone",At=moment,Source=NewsClient.NewsUrl+"/12345",Kind=CalendarKind.EventEnd};var snapshot=new CalendarSnapshot(new[]{entry},15,"");var local=TimeZoneInfo.ConvertTime(moment,zone);
            check(snapshot.OnDay(local.Date).Count==1 && snapshot.OnDay(local.Date.AddDays(1)).Count==0 && snapshot.NearestDay(moment.AddMinutes(-1))==local.Date,"Calendar index, membership and navigation preserve the exact local date: "+id+" / "+moment.ToString("yyyy-MM-dd HH:mm"));
          }
        }
        foreach(double dpi in new[]{1,1.25,1.5,1.75,2})foreach(var physical in new[]{new Rect(0,0,1920,1040),new Rect(-1920,0,1920,1040),new Rect(0,0,1280,680)}) {
          var work=new Rect(physical.X/dpi,physical.Y/dpi,physical.Width/dpi,physical.Height/dpi);var fit=FitCalendar(new Rect(99999,-99999,960,790),work);
          check(fit.Left>=work.Left && fit.Top>=work.Top && fit.Right<=work.Right && fit.Bottom<=work.Bottom,"Calendar bounds stay on the selected monitor at DPI "+dpi.ToString(CultureInfo.InvariantCulture)+" / "+physical);
        }
      }finally{Clock.ZoneOverride=savedZone;}
    }
    static string CalendarArticle(DateTimeOffset published,string end) {
      return "<p>▼// Event Period</p><p>"+published.AddDays(1).ToString("MMM. d, yyyy 'at' HH:mm",CultureInfo.InvariantCulture)+" – "+end+" (server time)</p><p>Asia Server Time Zone: UTC+8</p><p>Americas / Europe Server Time Zone: UTC-5</p>";
    }
    static async Task CheckCalendarSources(Action<bool,string> check,string folder) {
      string previousDirectory=Storage.DirectoryPath;Storage.DirectoryPath=Path.Combine(folder,"public-source-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Storage.DirectoryPath);
      try {
        var published=new DateTimeOffset(2029,3,10,0,0,0,TimeSpan.Zero);
        using(var fixture=new CalendarBulletinFixture())using(var httpClient=new HttpClient(fixture)) {
          fixture.Add("80001","[New machinery] Challenge Event Details",published,CalendarArticle(published,"Mar. 30, 2029 at 04:00"));
          fixture.Add("80002","[Broken banner] Chartered Headhunting",published,"<p>Unknown banner layout.</p>");
          var client=new BannerClient(httpClient);var feed=await client.FetchCalendar(null);
          bool bannerFailed=false;try{await client.Fetch(null,Schedule.Seed(),false);}catch{bannerFailed=true;}
          check(bannerFailed && feed.Events.Count==1 && !feed.Partial,"Calendar dates load independently when the operator banner parser fails");
          check(feed.Events[0].EuropeEndUnix==new DateTimeOffset(2029,3,30,9,0,0,TimeSpan.Zero).ToUnixTimeSeconds() && feed.Events[0].AsiaEndUnix==new DateTimeOffset(2029,3,29,20,0,0,TimeSpan.Zero).ToUnixTimeSeconds(),"An unknown future article with Event Period wording retains its exact regional UTC deadlines");
          fixture.SetBody("80001",CalendarArticle(published,"Mar. 31, 2029 at 04:00"));var correction=await client.FetchCalendar(feed);
          check(correction.Events.Count==1 && correction.Events[0].EuropeEndUnix==feed.Events[0].EuropeEndUnix+86400,"An independently fetched correction removes the previous day from the calendar");
          fixture.Failures["80001"]=HttpStatusCode.ServiceUnavailable;var partial=await client.FetchCalendar(correction);
          check(partial.Partial && partial.Events.Count==1 && partial.Sources.Single().Problem=="server" && partial.Events[0].EuropeEndUnix==correction.Events[0].EuropeEndUnix,"A server outage preserves the last confirmed deadline and records the real partial state");
          check(IsTransientFailure(new BulletinException("server",503)) && !IsTransientFailure(new BulletinException("rate-limit",429)),"Temporary server failures retry while rate limits do not cause rapid global retry loops");
          fixture.Failures.Clear();fixture.SetBody("80001",CalendarArticle(published,"Before the next version update and maintenance"));var withdrawn=await client.FetchCalendar(partial);
          check(!withdrawn.Partial && withdrawn.Events.Count==0 && withdrawn.Sources.Single().Undated,"A successful withdrawal of the exact date does not leave an obsolete cached deadline");
          fixture.SetBody("80001","<p>A changed layout with no time section.</p>");var malformed=await client.FetchCalendar(correction);
          check(malformed.Partial && malformed.Events.Count==1 && malformed.Sources.Single().Problem=="format","A changed article format retains known dates without claiming a complete update");
          fixture.SetBody("80001",CalendarArticle(published,"Mar. 31, 2029 at 04:00"));fixture.DelayMilliseconds=180;
          var pending=client.FetchCalendar(correction);check(!pending.IsCompleted,"A slow official response returns control before parsing completes");await pending;fixture.DelayMilliseconds=0;
          fixture.DelayMilliseconds=500;pending=client.FetchCalendar(correction);httpClient.CancelPendingRequests();var canceled=await pending;
          check(canceled.Partial && canceled.Events.Count==1 && canceled.Sources.Single().Problem=="timeout","Canceling an obsolete request keeps confirmed data without hanging its reader");
          fixture.DelayMilliseconds=0;var afterCancel=await client.FetchCalendar(correction);check(!afterCancel.Partial && afterCancel.Events.Count==1,"The same client recovers after canceled requests without a restart");
          CalendarFeedStore.Save(correction);var loaded=CalendarFeedStore.Load(null);
          check(loaded.Events.Count==1 && loaded.Events[0].EuropeEndUnix==correction.Events[0].EuropeEndUnix,"The versioned public cache round-trips confirmed calendar data");
          check(Math.Abs((loaded.Checked-correction.Checked).TotalSeconds)<1 && Math.Abs((loaded.Sources.Single().LastSuccess-correction.Sources.Single().LastSuccess).TotalSeconds)<1,"Public cache observation timestamps survive a restart-shaped read");
          var next=await client.FetchCalendar(correction);CalendarFeedStore.Save(next);File.WriteAllText(Path.Combine(Storage.DirectoryPath,"calendar-ru.json"),"{interrupted");
          var recovered=CalendarFeedStore.Load(null);check(recovered.Events.Count==1,"A damaged public cache recovers its last valid backup");
          File.WriteAllText(Path.Combine(Storage.DirectoryPath,"calendar.json.tmp.interrupted"),"{}");
          check(CalendarFeedStore.Load(null).Events.Count==1,"A leftover interrupted write is ignored instead of replacing the valid cache");
          next.Checked=next.Checked.AddDays(-2);CalendarFeedStore.Save(next);
          check(CalendarFeedStore.Load(null).Events.Count==1,"Moving the computer clock backwards does not block a new valid public cache write");
          var bad=new CalendarFeedState{Schema=999,Sources=new List<CalendarSourceState>()};
          check(!CalendarFeedStore.Valid(bad),"Unknown public cache schemas are rejected instead of silently interpreted");
          var legacy=CalendarFeedState.Seed(new BannerState{GameEvents=correction.Events,Checked=published});
          check(legacy.Events.Count==1 && legacy.Events[0].Source==correction.Events[0].Source,"Existing version 1.4 event data migrates without manual re-entry");
        }
        using(var fixture=new CalendarBulletinFixture())using(var httpClient=new HttpClient(fixture)) {
          for(int i=0;i<1005;i++)fixture.Add((90000+i).ToString(CultureInfo.InvariantCulture),"Information "+i,published.AddSeconds(-i),"<p>Information</p>");
          var client=new BannerClient(httpClient){CacheEnabled=true};var clock=new DateTimeOffset(2026,10,16,0,0,0,TimeSpan.Zero);client.CacheNow=()=>clock;
          var first=await client.Archive();int requests=fixture.Pages;
          check(first.Count==1005 && requests==51,"The complete public index is built beyond the former 1000-announcement limit");
          clock=clock.AddMinutes(1);var cached=await client.Archive();
          check(cached.Count==1005 && fixture.Pages==requests+1,"An unchanged complete archive checks one page instead of downloading its entire history again");
          fixture.Add("999999","[Brand new] Challenge Event Details",published.AddDays(1),CalendarArticle(published.AddDays(1),"Apr. 2, 2029 at 04:00"));clock=clock.AddMinutes(1);requests=fixture.Pages;
          var added=await client.Archive();check(added.Count==1006 && added.Any(a=>Json.Str(a,"cid")=="999999") && fixture.Pages==requests+1,"Incremental index reuse still discovers a new unknown announcement immediately");
          clock=clock.AddMinutes(1);requests=fixture.Requests;var together=await Task.WhenAll(client.Archive(),client.Archive());
          check(together.All(a=>a.Count==1006) && fixture.Requests==requests+1,"Concurrent calendar and banner readers share one official archive request");
          fixture.Remove("90001");clock=clock.AddMinutes(1);requests=fixture.Pages;var removed=await client.Archive();
          check(removed.Count==1005 && !removed.Any(a=>Json.Str(a,"cid")=="90001") && fixture.Pages>requests+1,"A changed archive size triggers a full consistency check and removes deleted entries");
          clock=clock.AddDays(2);requests=fixture.Pages;await client.Archive();check(fixture.Pages==requests+51,"A periodic full index validation refreshes older publication metadata");
          clock=clock.AddDays(2);fixture.MissingPage=2;bool rejected=false;try{await client.Archive();}catch(FormatException){rejected=true;}
          check(rejected,"A missing archive page cannot be mistaken for a verified complete history");
        }
      }finally{Storage.DirectoryPath=previousDirectory;}
    }
    async Task CheckCalendarViews(Action<bool,string> check,string folder) {
      var oldZone=Clock.ZoneOverride;var oldNow=calendarNow;var oldSchedule=schedule;var oldBanners=bannerState;var oldFeed=calendarFeed;var oldMask=calendarFilterMask;string oldQuery=calendarQuery,oldRegion=config.BannerRegion;var oldBounds=calendarLastBounds;
      try {
        if(calendarDialog!=null)calendarDialog.Close();var utc=TimeZoneInfo.Utc;Clock.ZoneOverride=()=>utc;var now=new DateTimeOffset(2026,11,5,8,0,0,TimeSpan.Zero);calendarNow=()=>now;calendarRemembered=false;calendarFilterMask=15;calendarQuery="";calendarFeed=null;config.BannerRegion="Europe";
        schedule=new Schedule{Version="Sanctuary of Ink",Source=NewsClient.NewsUrl+"/80001",Start=now.AddHours(-1),End=now.AddHours(5)};
        string html=Json.Str(Json.Decode(File.ReadAllText(Path.Combine(folder,"version-9560.json"))),"data","data");
        bannerState=new BannerState{Debuts=new List<Debut>{new Debut{Name="Si",Source=NewsClient.NewsUrl+"/9560",EuropeStart=now.AddDays(-20),AsiaStart=now.AddDays(-20),EuropeEndUnix=now.AddHours(8).ToUnixTimeSeconds(),AsiaEndUnix=now.AddHours(8).ToUnixTimeSeconds()},new Debut{Name="Argent Flow",Source=NewsClient.NewsUrl+"/9560",EuropeStart=now.AddHours(9),AsiaStart=now.AddHours(9)}},GameEvents=GameEventDates.Parse(html,"[Sanctuary of Ink] Version Update Notes","9560",1792026000)};
        ShowCalendar();calendarDialog.UpdateLayout();SelectCalendarDay(now.Date);calendarDialog.UpdateLayout();
        var nativeHandle=new WindowInteropHelper(calendarDialog).Handle;long nativeExtended=DesktopNative.GetStyle(nativeHandle,DesktopNative.ExtendedStyleIndex),nativeStyle=DesktopNative.GetStyle(nativeHandle,DesktopNative.StyleIndex);
        check(!calendarDialog.AllowsTransparency && (nativeExtended&0x80000)==0 && (nativeExtended&DesktopNative.ToolWindow)!=0 && (nativeExtended&DesktopNative.AppWindow)==0,"The calendar uses the ordinary accelerated window path while remaining outside Alt+Tab and the taskbar");
        check(window.AllowsTransparency && (DesktopNative.GetStyle(new WindowInteropHelper(window).Handle,DesktopNative.ExtendedStyleIndex)&0x80000)!=0 && (nativeStyle&DesktopNative.Child)==0,"The desktop widget keeps its original layered renderer and the calendar is never reparented as a child");
        check(calendarDialog.Owner==null && (nativeExtended&DesktopNative.NoActivate)==0,"The calendar stays independent and accepts normal keyboard focus");
        DesktopNative.Rectangle nativeBounds;DesktopNative.GetWindowRect(nativeHandle,out nativeBounds);int hitX=nativeBounds.Left+2,hitY=(nativeBounds.Top+nativeBounds.Bottom)/2;long hit=CalendarNativeMessage(nativeHandle,0x84,IntPtr.Zero,new IntPtr(unchecked((hitY&65535)<<16 | hitX&65535))).ToInt64();
        check(hit==10,"The native calendar frame recognizes its resize border without synthetic dragging");
        check(calendarDay==now.Date && calendarMonth==new DateTime(2026,11,1),"The first calendar opening consistently uses its injected current local day");
        check(CalendarFind<UniformGrid>("CalendarDays").Children.Count==42 && calendarDayVisuals.Select(v=>v.Button).Distinct().Count()==42,"The new view creates exactly one reusable button for each day cell");
        check(CalendarFind<ListBox>("CalendarAgenda").Items.Count>=5 && calendarSnapshot.OnDay(now.Date).Count>=4,"The real view combines all calendar categories and multiple events on one day");
        CommandManager.InvalidateRequerySuggested();await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var cards=CalendarVisualElements<Button>(CalendarFind<ListBox>("CalendarEvents")).Where(b=>b.CommandParameter is CalendarEntry).ToList();
        check(cards.Count>0 && cards.All(b=>b.IsEnabled),"Realized selected-day source actions are enabled after normal data binding");
        File.WriteAllLines(Path.Combine(folder,"card-interaction.txt"),cards.Select(b=>"source="+((CalendarEntry)b.CommandParameter).Source+"; canExecute="+b.Command.CanExecute(b.CommandParameter)+"; enabled="+b.IsEnabled));
        check(calendarDayModels.Count>=4 && cards.All(b=>b.Command.CanExecute(b.CommandParameter) && b.IsEnabled) && calendarDayModels.All(m=>m.OpenSource.CanExecute(m.SourceTarget)),"Every selected-day card is interactive and points to an official source");
        string opened=null;calendarOpenOverride=url=>opened=url;cards[0].Command.Execute(cards[0].CommandParameter);
        check(CalendarData.Official(opened),"The real card action opens its official source through the validated command");
        check(!calendarSourceCommand.CanExecute(new CalendarEntry{Source="https://example.com/"}),"The calendar source command rejects an arbitrary external address");
        var dayButton=calendarDayVisuals.Single(v=>(DateTime)v.Button.Tag==now.Date).Button;
        check(dayButton.BorderThickness.Left>1 && (string)dayButton.GetValue(System.Windows.Automation.AutomationProperties.NameProperty)!=null,"Today keeps its distinct outline and accessible name while selected");
        var controls=calendarDayVisuals.Select(v=>v.Button).ToArray();int gridBuilds=CalendarGridBuilds,cardBuilds=CalendarCardBuilds,agendaBuilds=CalendarAgendaBuilds;RenderCalendar(false);
        check(CalendarGridBuilds==gridBuilds && CalendarCardBuilds==cardBuilds && CalendarAgendaBuilds==agendaBuilds && ReferenceEquals(cards[0],CalendarVisualElements<Button>(CalendarFind<ListBox>("CalendarEvents")).First(b=>b.CommandParameter is CalendarEntry)),"A no-op refresh preserves real card controls and does not rebuild the grid or agenda");
        SelectCalendarDay(now.AddDays(1).Date);
        check(controls.SequenceEqual(calendarDayVisuals.Select(v=>v.Button)),"Selecting another day reuses the existing 42 day controls");
        var focused=calendarDayVisuals.Single(v=>(DateTime)v.Button.Tag==calendarDay).Button;focused.Focus();Keyboard.Focus(focused);
        var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(calendarDialog),0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent};focused.RaiseEvent(key);
        check(key.Handled && calendarDay==now.AddDays(2).Date && calendarDayVisuals.Count(v=>v.Button.IsTabStop)==1,"Local arrow navigation moves the selected day and retains one grid tab stop");
        var adjacent=calendarDayVisuals.First(v=>((DateTime)v.Button.Tag).Month==10).Button;adjacent.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(calendarMonth.Month==10 && calendarDay.Month==10,"Clicking a neighboring-month day moves the month and selection together");
        CalendarFind<Button>("CalendarToday").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var search=CalendarFind<TextBox>("CalendarSearch");search.Focus();await TestTextInput(search,"arena");ApplyCalendarSearch();calendarDialog.UpdateLayout();
        check(calendarSnapshot.Entries.Count==1 && CalendarFind<ListBox>("CalendarAgenda").Items.Count==1 && calendarSnapshot.Entries[0].Name=="Arena of Games","The real search input filters the grid, future agenda and model together");
        var searchHost=search.Template.FindName("PART_ContentHost",search) as ScrollViewer;var character=search.GetRectFromCharacterIndex(0);
        check(searchHost!=null && searchHost.ActualHeight>=search.ActualHeight-4 && character.Left<search.Padding.Left+8 && character.Top<search.Padding.Top+6,"Search text has a full-height content host and one set of padding so typed glyphs remain visible");
        CalendarFind<Button>("CalendarUpcoming").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(calendarDay==new DateTime(2026,11,19),"Nearest-event navigation respects the active search");
        search.Text="no matching event";ApplyCalendarSearch();check(!CalendarFind<Button>("CalendarUpcoming").IsEnabled && CalendarFind<TextBlock>("CalendarAgendaEmpty").Visibility==Visibility.Visible,"A failed search has a visible empty state and disables impossible navigation");
        CalendarFind<Button>("CalendarClearSearch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(calendarQuery=="" && calendarSnapshot.Entries.Count>1,"The clear action restores all available calendar data");
        calendarDialog.UpdateLayout();await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var rebound=CalendarVisualElements<Button>(CalendarFind<ListBox>("CalendarAgenda")).Where(b=>b.CommandParameter is CalendarEntry).ToList();
        check(rebound.Count>0 && rebound.All(b=>b.IsEnabled && b.Command.CanExecute(null)),"Newly rebound source actions are immediately enabled without waiting for input or a global command requery");
        var filter=CalendarFind<ToggleButton>("CalendarEventEnds");filter.IsChecked=false;filter.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(calendarSnapshot.Entries.All(e=>e.Kind!=CalendarKind.EventEnd) && calendarAgendaModels.All(m=>m.Entry.Kind!=CalendarKind.EventEnd),"Category toggles consistently filter the calendar and agenda");
        CalendarFind<ToggleButton>("CalendarAll").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));SelectCalendarDay(now.Date);
        CalendarFind<Button>("CalendarMonth").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));CalendarFind<ComboBox>("CalendarPickerMonth").SelectedIndex=1;CalendarFind<TextBox>("CalendarPickerYear").Text="2028";ApplyCalendarPicker();
        check(calendarMonth==new DateTime(2028,2,1),"The month picker jumps directly to a leap-year February");
        OpenCalendarPicker();CalendarFind<TextBox>("CalendarPickerYear").Text="0";ApplyCalendarPicker();
        check(CalendarFind<TextBlock>("CalendarPickerError").Visibility==Visibility.Visible && calendarMonth.Year==2028,"An invalid picker year produces feedback without changing the current month");
        CalendarFind<Popup>("CalendarPicker").IsOpen=false;SelectCalendarDay(new DateTime(1,1,2));calendarDialog.UpdateLayout();var minimum=calendarDayVisuals.Single(v=>(DateTime)v.Button.Tag==calendarDay).Button;minimum.Focus();Keyboard.Focus(minimum);var pastLimit=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(calendarDialog),0,Key.Up){RoutedEvent=Keyboard.PreviewKeyDownEvent};minimum.RaiseEvent(pastLimit);
        check(pastLimit.Handled && calendarDay==new DateTime(1,1,2) && !CalendarFind<Button>("CalendarPrevious").IsEnabled,"Keyboard navigation safely stops at the minimum supported year");
        SelectCalendarDay(new DateTime(9998,12,31));calendarDialog.UpdateLayout();var maximum=calendarDayVisuals.Single(v=>(DateTime)v.Button.Tag==calendarDay).Button;maximum.Focus();Keyboard.Focus(maximum);var nextLimit=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(calendarDialog),0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent};maximum.RaiseEvent(nextLimit);
        check(nextLimit.Handled && calendarDay==new DateTime(9998,12,31) && !CalendarFind<Button>("CalendarNext").IsEnabled,"Keyboard navigation and month controls safely stop at the maximum supported year");
        CalendarFind<Popup>("CalendarPicker").IsOpen=false;SelectCalendarDay(now.Date);
        var sameWindow=calendarDialog;ShowCalendar();check(ReferenceEquals(sameWindow,calendarDialog),"Repeated calendar commands activate one existing window instead of creating duplicates");
        int epoch=recovery.Epoch;var beforeFeed=calendarFeed;var completion=new TaskCompletionSource<CalendarFeedState>();var apply=ApplyCalendarFeed(completion.Task,epoch);recovery.Epoch++;completion.SetResult(CalendarFeedState.Seed(bannerState));await apply;
        check(ReferenceEquals(calendarFeed,beforeFeed),"A response from an invalidated connection epoch cannot replace newer calendar state");recovery.Epoch=epoch;
        var priorNow=now;now=new DateTimeOffset(2026,11,6,0,0,0,TimeSpan.Zero);TickCalendar(now);
        check(calendarDayVisuals.Single(v=>(DateTime)v.Button.Tag==now.Date).Button.BorderThickness.Left>1 && calendarDayVisuals.Single(v=>(DateTime)v.Button.Tag==priorNow.Date).Button.BorderThickness.Left==1,"The shared background tick moves the Today outline across midnight while preserving selection");now=priorNow;RenderCalendar(false);
        SaveImage(calendarDialog,Path.Combine(folder,"calendar-overview.png"));
        foreach(double scale in new[]{1,1.25,1.5,1.75,2}) {
          calendarDialog.Width=Math.Max(420,960/scale);calendarDialog.Height=Math.Max(400,790/scale);calendarDialog.UpdateLayout();LayoutCalendar();calendarDialog.UpdateLayout();
          bool stacked=calendarDialog.ActualWidth<790;check(Grid.GetRow(CalendarFind<Grid>("CalendarAgendaPane"))==(stacked?1:0) && CalendarFind<Button>("CalendarClose").IsVisible && CalendarFind<ScrollViewer>("CalendarBodyScroll").ViewportHeight>0,"Responsive logical layout remains navigable at simulated DPI "+scale.ToString(CultureInfo.InvariantCulture));
          SaveImage(calendarDialog,Path.Combine(folder,"calendar-dpi-"+scale.ToString(CultureInfo.InvariantCulture).Replace('.','_')+".png"));
        }
        calendarDialog.Width=960;calendarDialog.Height=790;calendarDialog.UpdateLayout();LayoutCalendar();
        search=CalendarFind<TextBox>("CalendarSearch");search.Text="Carly";ApplyCalendarSearch();SelectCalendarDay(new DateTime(2026,11,5));calendarDialog.Close();ShowCalendar();calendarDialog.UpdateLayout();
        check(calendarQuery=="Carly" && CalendarFind<TextBox>("CalendarSearch").Text=="Carly" && calendarDay==new DateTime(2026,11,5),"Closing and reopening preserves the session's search and selected date");
        CalendarFind<Button>("CalendarClearSearch").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var tokyo=TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");var selectedEntry=calendarSnapshot.Entries.First(e=>e.Kind==CalendarKind.EventEnd && e.Name.Contains("Carly"));calendarSelectedId=selectedEntry.Id;Clock.ZoneOverride=()=>tokyo;RenderCalendar();
        check(calendarDay==TimeZoneInfo.ConvertTime(selectedEntry.At,tokyo).Date && calendarSelectedId==selectedEntry.Id,"A timezone change follows the selected event by stable identity");
        Clock.ZoneOverride=()=>utc;RenderCalendar();SelectCalendarDay(now.Date);
        var beforeColor=((SolidColorBrush)calendarDialog.FindResource("ThemeAccentBrush")).Color;var original=currentTheme;
        try{currentTheme=OperatorTheme.FromAccent(205,.48,190);currentTheme.Apply(window.Resources);calendarDialog.UpdateLayout();check(((SolidColorBrush)calendarDialog.FindResource("ThemeAccentBrush")).Color!=beforeColor,"The open calendar receives a changed operator palette without reopening");SaveImage(calendarDialog,Path.Combine(folder,"calendar-blue-palette.png"));}finally{currentTheme=original;currentTheme.Apply(window.Resources);}
        calendarFeed=CalendarFeedState.FromSources(new CalendarSourceState[0],now,0,true);RenderCalendar();SaveImage(calendarDialog,Path.Combine(folder,"calendar-offline.png"));
        check(CalendarFind<TextBlock>("CalendarLiveStatus").Text.Contains("saved"),"Partial public data is described briefly without technical API details");
        calendarFeed=null;
        bannerState=new BannerState{GameEvents=Enumerable.Range(0,1000).Select(i=>CalendarEvent("Long official gameplay event name "+i+" with a readable second line",""+(60000+i),now.AddDays(-1),now.AddHours(1+i%28),now.AddDays(1+i%28))).ToList()};schedule=null;RenderCalendar();calendarDialog.UpdateLayout();
        await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        int realized=CalendarVisualCount<ListBoxItem>(CalendarFind<ListBox>("CalendarAgenda"));
        check(calendarAgendaModels.Count==1000 && realized>0 && realized<100,"The future agenda virtualizes a thousand events instead of creating a thousand visible cards");
        SelectCalendarDay(now.AddDays(1).Date);calendarDialog.UpdateLayout();var dayScroll=CalendarDayScroller();var agendaScroll=FindCalendarVisual<ScrollViewer>(CalendarFind<ListBox>("CalendarAgenda"));dayScroll.ScrollToVerticalOffset(15);agendaScroll.ScrollToVerticalOffset(15);await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        check(calendarDayModels.Count>20 && CalendarVisualCount<ListBoxItem>(CalendarFind<ListBox>("CalendarEvents"))<10,"A crowded selected day also virtualizes cards instead of eagerly building its entire list");
        double dayOffset=dayScroll.VerticalOffset,agendaOffset=agendaScroll.VerticalOffset;var updated=bannerState.GameEvents[0];bannerState.GameEvents[0]=CalendarEvent(updated.Name,"60000",now.AddDays(-1),now.AddHours(1),DateTimeOffset.FromUnixTimeSeconds(updated.EuropeEndUnix).AddMinutes(30));RenderCalendar();calendarDialog.UpdateLayout();await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        check(dayOffset>0 && Math.Abs(dayScroll.VerticalOffset-dayOffset)<1,"A corrected selected-day card preserves the selected list's scroll position");
        check(agendaOffset>0 && Math.Abs(agendaScroll.VerticalOffset-agendaOffset)<1,"A corrected source preserves the virtualized agenda's scroll position");
        SaveImage(calendarDialog,Path.Combine(folder,"calendar-dense.png"));
        bannerState=null;RenderCalendar();SaveImage(calendarDialog,Path.Combine(folder,"calendar-empty.png"));
        check(calendarSnapshot.Entries.Count==0 && CalendarFind<TextBlock>("CalendarAgendaEmpty").Visibility==Visibility.Visible,"A truly empty calendar remains usable and explains the absence of dates");
        calendarDialog.Close();check(calendarDialog==null && calendarSearchTimer==null && calendarDayVisuals.Count==0 && calendarDisplayChanged==null,"Closing releases the calendar controls, debounce timer and display-change subscription");
      }finally {
        calendarOpenOverride=null;if(calendarDialog!=null)calendarDialog.Close();Clock.ZoneOverride=oldZone;calendarNow=oldNow;schedule=oldSchedule;bannerState=oldBanners;calendarFeed=oldFeed;calendarFilterMask=oldMask;calendarQuery=oldQuery;config.BannerRegion=oldRegion;calendarLastBounds=oldBounds;calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;
      }
    }
    static int CalendarVisualCount<T>(DependencyObject root) where T:DependencyObject {
      int count=root is T?1:0;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)count+=CalendarVisualCount<T>(VisualTreeHelper.GetChild(root,i));return count;
    }
    static IEnumerable<T> CalendarVisualElements<T>(DependencyObject root) where T:DependencyObject {
      if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in CalendarVisualElements<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
  }
  sealed class CalendarBulletinFixture:HttpMessageHandler {
    readonly List<Dictionary<string,object>> articles=new List<Dictionary<string,object>>();readonly Dictionary<string,string> bodies=new Dictionary<string,string>();
    public readonly Dictionary<string,HttpStatusCode> Failures=new Dictionary<string,HttpStatusCode>();
    public int Requests,Pages,MissingPage,DelayMilliseconds;
    public void Add(string cid,string title,DateTimeOffset published,string html){articles.Add(new Dictionary<string,object>{{"cid",cid},{"title",title},{"displayTime",published.ToUnixTimeSeconds()}});bodies[cid]=html;}
    public void SetBody(string cid,string html){bodies[cid]=html;}
    public void Remove(string cid){articles.RemoveAll(a=>Json.Str(a,"cid")==cid);bodies.Remove(cid);}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
      Interlocked.Increment(ref Requests);if(DelayMilliseconds>0)await Task.Delay(DelayMilliseconds,cancellation).ConfigureAwait(false);
      if(request.RequestUri.AbsolutePath.EndsWith("/api/bulletin",StringComparison.Ordinal)) {
        Interlocked.Increment(ref Pages);var match=System.Text.RegularExpressions.Regex.Match(request.RequestUri.Query,@"[?&]page=(\d+)");int page=match.Success?int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture):1;
        var list=page==MissingPage?new List<Dictionary<string,object>>():articles.OrderByDescending(a=>Json.Num(a,"displayTime")).Skip((page-1)*20).Take(20).ToList();
        return Response(HttpStatusCode.OK,Json.Encode(new{code=0,data=new{total=articles.Count,list=list}}));
      }
      string cid=request.RequestUri.Segments.Last();HttpStatusCode status;if(Failures.TryGetValue(cid,out status))return Response(status,"unavailable");string html;
      var article=articles.FirstOrDefault(a=>Json.Str(a,"cid")==cid);
      return bodies.TryGetValue(cid,out html)?Response(HttpStatusCode.OK,Json.Encode(new{code=0,data=new{data=html,title=article==null?"":Json.Str(article,"title")}})):Response(HttpStatusCode.NotFound,"missing");
    }
    static HttpResponseMessage Response(HttpStatusCode status,string body){return new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
  }
}
