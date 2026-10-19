using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    void SaveImage(Window target,string filename) {
      if(target==calendarDialog)target.Dispatcher.Invoke(DispatcherPriority.ContextIdle,new Action(()=>{}));
      target.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(target);
      var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(filename))encoder.Save(stream);
    }
    void DemoReading() {
      accountToken="offline-fixture";config.RoleId="fixture-role";
      sanity=new Sanity {Current=178,Maximum=240,Checked=DateTimeOffset.UtcNow,ServerTime=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),FullAt=DateTimeOffset.UtcNow.AddHours(7.44).ToUnixTimeSeconds(),RoleId="fixture-role",Nickname="Endministrator"};Paint();
    }
    public void FeatureView(string reportPath,string mode) {
      diagnosticView=true;DemoReading();refreshBusy=mode!="languages";config.Compact=mode=="compact";config.BackgroundOpacity=.90;ApplyBackgroundOpacity();ApplyLayout(true);DesktopView(reportPath);
      keyboardSource=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);if(keyboardSource!=null){keyboardSource.AddHook(HotkeyMessage);globalToggleKey=RegisterHotKey(keyboardSource.Handle,ToggleHotkey,0x4003,0x48);}
      if(mode=="settings" || mode=="languages")window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>Settings()));
      if(timer!=null)timer.Stop();timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
      var deadline=DateTimeOffset.UtcNow.AddMinutes(12);timer.Tick+=(s,e)=>{Paint();File.WriteAllText(reportPath+".state.txt","SettingsVisible="+(settingsDialog!=null && settingsDialog.IsVisible)+"\nWidgetVisible="+window.IsVisible+"\nToggleHotkeyRegistered="+globalToggleKey+"\nClickThrough="+config.ClickThrough+"\nFullScale="+config.Scale+"\nCompactScale="+config.CompactScale+"\nShowNextDebut="+config.ShowNextDebut+"\nMouseOver="+window.IsMouseOver+"\nKeyboardWithin="+window.IsKeyboardFocusWithin+"\nCompactSettingsOpacity="+Find<Button>("CompactSettingsButton").Opacity+"\nCompactSettingsHitTest="+Find<Button>("CompactSettingsButton").IsHitTestVisible+"\nFocused="+(Keyboard.FocusedElement as FrameworkElement==null?"none":((FrameworkElement)Keyboard.FocusedElement).Name)+"\n",Encoding.UTF8);if(DateTimeOffset.UtcNow>=deadline)Exit();};timer.Start();
    }
    public void FeatureTest(string reportPath) {
      var previousZone=Clock.ZoneOverride;var fixtureZone=TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");Clock.ZoneOverride=()=>fixtureZone;
      window.Show();window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var publicationFixture=Publications.NativeFixture("ru-ru");
        var report=new List<string>();Window dialog=null;
        Action<bool,string> check=(condition,label)=>{if(!condition)throw new Exception(label);report.Add("PASS: "+label);};
        string folder=Path.GetDirectoryName(Path.GetFullPath(reportPath));
        try {
          Directory.CreateDirectory(folder);
          check(Storage.Isolated && startup is MemoryStartupRegistration,"Feature checks use an isolated profile and a memory startup backend");
          var legacyScale=new JavaScriptSerializer().Deserialize<Config>("{\"Scale\":0.9,\"Compact\":true}");legacyScale.NormalizeScales();
          check(legacyScale.Scale==.9 && legacyScale.CompactScale==.9 && legacyScale.Compact,"An older shared scale is preserved for both modes, including an active compact mode");
          var freshScale=new Config();freshScale.NormalizeScales();
          check(freshScale.Scale==1 && freshScale.CompactScale==1,"New profiles start both modes at 100 percent");
          var separateScale=new JavaScriptSerializer().Deserialize<Config>("{\"Scale\":0.8,\"CompactScale\":1.2}");separateScale.NormalizeScales();
          check(separateScale.Scale==.8 && separateScale.CompactScale==1.2,"Previously saved independent scales are retained on load");
          CheckPolicies(check);
          using(var fixtureHttp=new HttpClient(new MissingArtFixture())) {
            var dates=await new BannerClient(fixtureHttp).Fetch(BannerState.Seed(),Schedule.Seed(),false);
            check(BannerClient.Next(dates,DateTimeOffset.UtcNow,false)!=null && !string.IsNullOrEmpty(dates.ArtError),"Official debut dates remain available when its portrait cannot be downloaded");
          }
          var screenA=new DisplayArea {Id="primary",Primary=true,Work=new Rect(0,0,1920,1040),Bounds=new Rect(0,0,1920,1080)};
          var screenB=new DisplayArea {Id="secondary",Work=new Rect(-2560,0,2560,1400),Bounds=new Rect(-2560,0,2560,1440)};
          var placement=new Config {MonitorId="secondary",MonitorX=1,MonitorY=.65,HasPosition=true};
          var restored=Placement.Restore(placement,screenB,new Size(420,710));
          check(restored.X==-432 && restored.Y>12,"Saved placement restores onto a monitor left of the primary display");
          check(Placement.Preferred(placement,new[]{screenA}).Id=="primary" && placement.MonitorId=="secondary","Disconnecting a monitor preserves its preferred identity");
          check(Placement.Preferred(placement,new[]{screenA,screenB}).Id=="secondary","Reconnecting a monitor restores the preferred display");
          var changed=new DisplayArea {Id="secondary",Work=new Rect(1920,80,1280,900)};
          var newPosition=Placement.Restore(placement,changed,new Size(420,710));
          check(newPosition.X==2768 && newPosition.Y>=92 && newPosition.Y+710<=980,"Changed resolution and monitor origin retain a visible anchored position");
          check(Placement.Snap(new Rect(-2540,22,420,710),screenB)==new Point(-2548,12),"Snap respects top and left edges on a negative-coordinate monitor");
          check(Placement.Snap(new Rect(1494,320,420,710),screenA)==new Point(1488,318),"Snap respects right and bottom work-area margins");
          check(Placement.Snap(new Rect(800,150,420,710),screenA)==new Point(800,150),"Moving through the interior does not snap unexpectedly");
          DemoReading();
          CheckMenuInput(check);
          CheckUpdateBlocks(check,folder);
          await CheckBannerOverview(check,folder);
          config.MonitorId=screenB.Id;config.MonitorX=.7;config.MonitorY=.3;displayOverride=new List<DisplayArea>{screenA,screenB};ApplyLayout(true);window.UpdateLayout();
          check(window.Left<0,"The real WPF window can restore onto a secondary monitor");
          displayOverride=new List<DisplayArea>{screenA};ApplyLayout(true);window.UpdateLayout();
          check(window.Left>=0 && config.MonitorId==screenB.Id,"Monitor fallback moves the window without overwriting the saved preference");
          displayOverride=new List<DisplayArea>{screenA,screenB};ApplyLayout(true);window.UpdateLayout();
          check(window.Left<0,"The same WPF window returns when its preferred monitor reappears");
          displayOverride=null;config.MonitorId=Placement.Displays().First(s=>s.Primary).Id;config.MonitorX=.7;config.MonitorY=.2;ApplyLayout(true);window.UpdateLayout();
          window.Left=-10000;window.Top=-10000;
          check(Find<Button>("CloseButton").Opacity==0 && !Find<Button>("CloseButton").IsHitTestVisible,"Chrome is hidden and cannot intercept clicks while the pointer is away");
          SaveImage(window,Path.Combine(folder,"widget-full.png"));
          dialog=SettingsWindow();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-10000;dialog.Top=-10000;dialog.Show();dialog.UpdateLayout();
          var memory=(MemoryStartupRegistration)startup;
          var settingsBar=(ScrollBar)((ScrollViewer)dialog.FindName("SettingsScroll")).Template.FindName("PART_VerticalScrollBar",(ScrollViewer)dialog.FindName("SettingsScroll"));
          check(settingsBar.ActualWidth<=6.1,"Settings use a compact scrollbar without clipping the content");
          check(memory.Writes==0 && ((CheckBox)dialog.FindName("StartupSwitch")).IsChecked==false,"Opening settings never creates a startup registration");
          check(((CheckBox)dialog.FindName("SanityNotifySwitch")).IsChecked==false && ((CheckBox)dialog.FindName("MaintenanceNotifySwitch")).IsChecked==false,"Both notification types start disabled");
          var startupSwitch=(CheckBox)dialog.FindName("StartupSwitch");startupSwitch.IsChecked=true;startupSwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
          check(memory.Enabled && memory.Writes==1,"Only the user's startup switch registers a launch command");
          startupSwitch.IsChecked=false;startupSwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));check(!memory.Enabled && memory.Writes==2,"Disabling startup removes the test registration");
          ((Slider)dialog.FindName("ScaleSlider")).Value=75;window.UpdateLayout();
          check(Math.Abs(window.Width-315)<.1 && Math.Abs(config.Scale-.75)<.001,"The scale slider resizes the entire widget to 75 percent");
          ((Slider)dialog.FindName("ScaleSlider")).Value=130;window.UpdateLayout();
          check(window.Width>420 && window.Height<=Placement.Displays().First(s=>s.Primary).Work.Height,"Large scale stays inside the available work area");
          ((Slider)dialog.FindName("ScaleSlider")).Value=100;
          ((Slider)dialog.FindName("OpacitySlider")).Value=60;
          check(((SolidColorBrush)window.Resources["WidgetBackgroundBrush"]).Color.A==153 && ((SolidColorBrush)window.Resources["ThemeTextBrush"]).Color.A==255,"Opacity changes the background while text stays opaque");
          check(((SolidColorBrush)dialog.FindResource("ThemeBackgroundBrush")).Color.A==255,"Settings remain opaque and readable when widget opacity changes");
          var alternate=BannerState.Seed().Debuts.First(d=>d.Key!=currentArtKey);ApplyDebut(alternate);
          check(((SolidColorBrush)window.Resources["WidgetBackgroundBrush"]).Color.A==153,"A new operator palette retains the chosen background opacity");
          ApplyDebut(BannerClient.Select(BannerState.Seed(),DateTimeOffset.UtcNow,false));((Slider)dialog.FindName("OpacitySlider")).Value=100;
          var compactSwitch=(CheckBox)dialog.FindName("CompactSwitch");compactSwitch.IsChecked=true;compactSwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));window.UpdateLayout();
          check(config.Compact && window.Width==360 && window.Height==Find<Grid>("WidgetLayout").Height && window.Height>140 && window.Height<600 && Find<Border>("FullView").Visibility==Visibility.Collapsed,"Compact mode changes both layout and actual window dimensions to fit its reading content");
          check(Find<TextBlock>("CompactVersion").Text==schedule.Version,"Compact mode shares the same live update data");
          SaveImage(window,Path.Combine(folder,"widget-compact.png"));
          var fullScale=(Slider)dialog.FindName("ScaleSlider");var compactScale=(Slider)dialog.FindName("CompactScaleSlider");
          fullScale.Value=75;window.UpdateLayout();
          check(window.Width==360 && config.Scale==.75 && config.CompactScale==1,"Changing the full-mode scale leaves the active compact widget unchanged");
          compactScale.Value=85;window.UpdateLayout();
          check(Math.Abs(window.Width-306)<.1 && config.Scale==.75 && config.CompactScale==.85,"The compact slider resizes only the compact widget");
          compactSwitch.IsChecked=false;compactSwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
          check(Math.Abs(window.Width-315)<.1 && config.CompactScale==.85,"Returning to the full mode restores its independent scale");
          compactScale.Value=120;window.UpdateLayout();
          check(Math.Abs(window.Width-315)<.1 && config.Scale==.75 && config.CompactScale==1.2,"Changing the compact-mode scale leaves the active full widget unchanged");
          compactMenu.IsChecked=true;compactMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));window.UpdateLayout();
          check(config.Compact && Math.Abs(window.Width-432)<.1,"The context menu also restores the saved compact scale");
          SaveImage(window,Path.Combine(folder,"compact-independent-scale.png"));
          compactMenu.IsChecked=false;compactMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));window.UpdateLayout();
          check(!config.Compact && Math.Abs(window.Width-315)<.1,"The context menu restores the saved full-mode scale");
          fullScale.Value=100;dialog.Close();
          dialog=SettingsWindow();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-10000;dialog.Top=-10000;dialog.Show();dialog.UpdateLayout();
          check(((Slider)dialog.FindName("ScaleSlider")).Value==100 && ((Slider)dialog.FindName("CompactScaleSlider")).Value==120,"Reopening settings retains both independent slider values");
          lockMenu.IsChecked=true;lockMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));UpdateMenu();
          check(config.PositionLocked && lockMenu.Header.ToString()=="Unlock position","The context menu locks and labels the position correctly");
          lockMenu.IsChecked=false;lockMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
          var input=(PasswordBox)dialog.FindName("TokenInput");input.Focus();await TestTextInput(input,"dummy-feature-input");check(input.Password=="dummy-feature-input","Token input remains interactive in the expanded settings");input.Clear();
          SaveImage(dialog,Path.Combine(folder,"settings-top.png"));
          var scroll=(ScrollViewer)dialog.FindName("SettingsScroll");scroll.ScrollToBottom();dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-bottom.png"));
          check(scroll.VerticalOffset>0 && ((CheckBox)dialog.FindName("FullscreenQuietSwitch")).IsVisible,"All notification controls are accessible by scrolling");
          var saved=Storage.Read<Config>("settings.json");check(saved!=null && saved.Scale==config.Scale && saved.CompactScale==config.CompactScale && saved.Scale!=saved.CompactScale && saved.BackgroundOpacity==config.BackgroundOpacity && saved.Compact==config.Compact && saved.MonitorId==config.MonitorId,"Both distinct mode scales and other view and monitor preferences survive serialization");
          CheckPersonalizedView(check,dialog,folder);
          await CheckBackgroundFeatures(check,folder);
          CheckArtworkAppearance(check,dialog,folder);
          await CheckGameActivity(check);
          await CheckGameLaunch(check,folder);
          await CheckGameDiscovery(check,folder);
          await CheckSmallPlayButtons(check,folder);
          await CheckStudioFeatures(check,folder);
          CheckContinuityFeatures(check,folder);
          await CheckOptimizationFeatures(check,folder);
          CheckReadingLayout(check,folder);
          await CheckLocalTimeViews(check,dialog);
          await CheckGameEventCalendar(check,folder);
          await CheckAutomaticDates(check);
          await CheckRussianArticles(check,folder);
          await CheckCalendarUpgrade(check,folder);
          check(UserStartupRegistration.Command(@"C:\Program Files\Endfield Widget\EndfieldWidget.exe")=="\"C:\\Program Files\\Endfield Widget\\EndfieldWidget.exe\"","Startup commands preserve paths containing spaces");
          report.Add("ALL FEATURE CHECKS PASSED. Multi-monitor topology changes are simulated; no physical screens, real startup keys, user account, clipboard, or Windows notifications were modified.");
        }catch(Exception error){report.Add("FAILED: "+error.ToString());Environment.ExitCode=1;}
        finally{if(dialog!=null)dialog.Close();Clock.ZoneOverride=previousZone;publicationFixture.Dispose();File.WriteAllLines(reportPath,report,Encoding.UTF8);Exit();}
      }));
    }
    void CheckUpdateBlocks(Action<bool,string> check,string folder) {
      var original=schedule;var now=DateTimeOffset.UtcNow;
      try {
        config.Compact=false;config.Scale=1;
        schedule=new Schedule {Version="Fixture version",Source=NewsClient.NewsUrl+"/1234",Start=now.AddHours(1),End=now.AddHours(7)};
        Paint();ApplyLayout(true);window.UpdateLayout();
        check(Find<Border>("FullUpdateCard").Visibility==Visibility.Visible && Find<RowDefinition>("FullUpdateRow").ActualHeight>0,"An announced future update keeps the full card and its measured content space");
        schedule.Start=now.AddHours(-1);Paint();window.UpdateLayout();
        check(Find<Border>("FullUpdateCard").Visibility==Visibility.Visible && Find<TextBlock>("ScheduleHeading").Text=="MAINTENANCE","An ongoing maintenance card remains visible until its planned end");
        double top=window.Top,left=window.Left,shownHeight=Find<Grid>("WidgetLayout").Height,removedHeight=Find<RowDefinition>("FullUpdateRow").ActualHeight;
        schedule.End=now.AddSeconds(-1);Paint();window.UpdateLayout();
        check(Find<Border>("FullUpdateCard").Visibility==Visibility.Collapsed && Find<RowDefinition>("FullUpdateRow").ActualHeight==0 && Math.Abs(Find<Grid>("WidgetLayout").Height-(shownHeight-removedHeight))<1,"A finished update removes exactly its measured row and gap from the full widget height");
        check(Math.Abs(window.Top-top)<1 && Math.Abs(window.Left-left)<1,"Automatic hiding keeps the widget at the same desktop position");
        check(!schedule.IsUpcomingOrInProgress(schedule.End),"The card is hidden at the exact planned end time");
        SaveImage(window,Path.Combine(folder,"widget-no-update.png"));
        config.Compact=true;ApplyLayout(true);window.UpdateLayout();
        double compactWithoutUpdate=window.Height;
        check(Find<Border>("CompactUpdateCard").Visibility==Visibility.Collapsed && Find<RowDefinition>("CompactUpdateRow").ActualHeight==0 && compactWithoutUpdate<shownHeight,"The compact update card and its complete gap also disappear");
        SaveImage(window,Path.Combine(folder,"compact-no-update.png"));
        schedule.End=now.AddHours(5);Paint();window.UpdateLayout();
        check(Find<Border>("CompactUpdateCard").Visibility==Visibility.Visible && Math.Abs(window.Height-compactWithoutUpdate-Find<RowDefinition>("CompactUpdateRow").ActualHeight)<1 && Find<RowDefinition>("CompactUpdateRow").ActualHeight>0,"Newly available update information restores exactly the measured compact card automatically");
        config.Compact=false;ApplyLayout(true);window.UpdateLayout();
        newsError="Fixture network failure";Paint();window.UpdateLayout();
        check(Find<Border>("FullUpdateCard").Visibility==Visibility.Visible,"A network failure retains a known, unexpired maintenance schedule");
        schedule.Version="";Paint();window.UpdateLayout();
        check(Find<Border>("FullUpdateCard").Visibility==Visibility.Collapsed,"A schedule without a version name is treated as missing information");
        check(!new Schedule {Version="No dates"}.IsUpcomingOrInProgress(now),"An announcement without valid dates cannot show an empty card");
      }finally{schedule=original;newsError="";config.Compact=false;Paint();ApplyLayout(true);}
    }
    static void CheckPolicies(Action<bool,string> check) {
      var now=new DateTimeOffset(2026,10,14,21,40,0,TimeSpan.Zero);
      var config=new Config {RoleId="test-role"};var policy=new NotificationPolicy(null);
      var reading=new Sanity {RoleId="test-role",Current=220,Maximum=240,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddHours(2).ToUnixTimeSeconds()};
      var update=new Schedule {Version="Official update",Source=NewsClient.NewsUrl+"/1234",Checked=now,Start=now.AddMinutes(20),End=now.AddHours(6)};
      check(policy.Next(config,reading,update,now,true,true,true)==null,"Disabled notifications produce no events");
      config.NotifySanity=true;var notice=policy.Next(config,reading,update,now,true,true,true);int sent=0;
      check(notice!=null && !policy.Deliver(notice,now,true,n=>{sent++;return true;}) && sent==0 && policy.History.Sent.Count==0,"Do Not Disturb defers an event without consuming or showing it");
      check(policy.Deliver(notice,now,false,n=>{sent++;return true;}) && sent==1 && policy.Next(config,reading,update,now,true,true,true)==null,"Fresh Sanity triggers once and repeated polling does not duplicate it");
      policy=new NotificationPolicy(new JavaScriptSerializer().Deserialize<NotificationHistory>(Json.Encode(policy.History)));
      check(policy.Next(config,reading,update,now,true,true,true)==null,"Sanity notification history survives a restart");
      reading.Current=100;policy.Next(config,reading,update,now,true,true,true);reading.Current=220;
      check(policy.Next(config,reading,update,now,true,true,true)!=null,"Spending Sanity rearms a later recovery cycle");
      check(policy.Next(config,reading,update,now.AddMinutes(11),true,true,true)==null && policy.Next(config,reading,update,now,false,true,true)==null,"Stale or failed Sanity requests cannot create alerts");
      reading.Checked=now.AddMinutes(1);check(policy.Next(config,reading,update,now,true,true,true)==null,"Future-dated readings cannot create alerts after a clock change");reading.Checked=now;
      check(policy.Next(config,reading,update,now,true,true,false)==null,"Disconnected accounts cannot create Sanity alerts");
      config.NotifySanity=false;config.NotifyMaintenance=true;
      notice=policy.Next(config,reading,update,now,true,true,true);
      check(notice!=null && notice.Message.Contains("10/15/2026") && notice.Message.Contains("01:00"),"Maintenance alerts use the Eastern European date and local time across midnight");
      check(!policy.Deliver(notice,now,true,n=>true),"A fullscreen session also defers maintenance notifications");
      check(policy.Next(config,reading,update,update.Start,true,true,true)==null,"Deferred maintenance events expire when maintenance starts");
      check(policy.Deliver(notice,now,false,n=>true) && policy.Next(config,reading,update,now,true,true,true)==null,"A maintenance event is delivered only once");
      policy=new NotificationPolicy(null);update.Bundled=true;check(policy.Next(config,reading,update,now,true,true,true)==null,"Unverified bundled schedules cannot trigger notifications");update.Bundled=false;
      check(policy.Next(config,reading,update,now,true,false,true)==null,"Offline news does not trigger maintenance alerts");
      update.Checked=now.AddHours(-2);check(policy.Next(config,reading,update,now,true,true,true)==null,"Old announcement checks cannot trigger alerts");
      check(QuietState.Covers(new Rect(-2560,0,2560,1440),new Rect(-2560,0,2560,1440)) && !QuietState.Covers(new Rect(-2560,0,1280,720),new Rect(-2560,0,2560,1440)),"Fullscreen detection handles secondary monitors and ignores ordinary windows");
      var state=BannerState.Seed();var before=Schedule.Seed().End.AddSeconds(-1);
      check(BannerClient.Next(state,before,false).Key=="si" && BannerClient.Select(state,before,false).Key=="typhoeus","Countdown identifies the next debut without changing the current portrait early");
      state.Debuts.Add(new Debut {Name=state.Debuts.First(d=>d.Key=="typhoeus").Name,EuropeStart=before.AddDays(3),AsiaStart=before.AddDays(3),Source=NewsClient.NewsUrl+"/9999"});
      check(BannerClient.Next(state,Schedule.Seed().End,false)==null,"A repeat banner for an already debuted operator is excluded from the countdown");
      state.Debuts.Add(new Debut {Name="Unknown",EuropeStart=before.AddDays(1),AsiaStart=before.AddDays(1),Source="https://example.org/guess"});
      check(BannerClient.Next(state,Schedule.Seed().End,false)==null,"Unannounced or unofficial banner dates are never displayed");
    }
  }
  sealed class MissingArtFixture:HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation) {
      string body;
      long published=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      if(request.RequestUri.AbsolutePath=="/api/bulletin")body=Json.Encode(new {code=0,data=new {total=1,list=new[]{new {cid="9999",title="[Fixture] Chartered Headhunting",displayTime=published}}}});
      else if(request.RequestUri.AbsolutePath=="/api/bulletin/9999") {
        string date=DateTimeOffset.UtcNow.AddDays(1).ToString("MMM. d, yyyy 'at' HH:mm",CultureInfo.InvariantCulture);
        body=Json.Encode(new {code=0,data=new {data="<p>[Fixture] Chartered Headhunting features the Rate-UP 6★ operator [New fixture].</p><p>Availability:</p><p>Americas / Europe Server: "+date+" – Nov. 30, 2026 at 11:59 (server time)</p>"}});
      } else body="<html>Catalogue temporarily unavailable</html>";
      return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
  }
}
