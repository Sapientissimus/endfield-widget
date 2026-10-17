using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace EndfieldWidget {
  partial class Widget {
    void CheckBackupPolicy(Action<bool,string> check,string folder) {
      string previous=Storage.DirectoryPath;bool recovered=Storage.SettingsRecovered;string problem=Storage.SettingsProblem;
      try {
        Storage.DirectoryPath=Path.Combine(folder,"backup-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Storage.DirectoryPath);
        string path=Path.Combine(Storage.DirectoryPath,"settings.json"),backup=path+".bak";
        var c=new Config{Scale=1,CompactScale=.9,ProtectedToken="PROTECTED-FIXTURE",RoleId="ROLE-FIXTURE"};Storage.Write("settings.json",c);
        check(Storage.ReadBackup()!=null && Storage.ReadBackup().Scale==1,"The first settings save creates a readable local backup");
        c.Scale=.8;Storage.Write("settings.json",c);Storage.Write("settings.json",c);
        check(Storage.ReadBackup().Scale==1 && Storage.Read<Config>("settings.json").Scale==.8,"Identical autosaves retain the previous distinct settings backup");
        File.WriteAllText(path,"{truncated");var restored=Storage.Read<Config>("settings.json");
        check(restored!=null && restored.Scale==1 && restored.ProtectedToken=="PROTECTED-FIXTURE" && Storage.SettingsRecovered,"A truncated settings file recovers the last valid profile including its protected credentials");
        restored.Scale=.95;Storage.Write("settings.json",restored);
        check(Storage.Read<Config>("settings.json").Scale==.95 && Storage.ReadBackup().Scale==1,"Repairing a broken primary file does not overwrite the valid backup with corrupt data");
        restored.ProtectedToken=null;restored.RoleId=null;Storage.Write("settings.json",restored);
        check(Storage.ReadBackup().ProtectedToken==null && Storage.ReadBackup().RoleId==null,"Explicit account removal also removes the old credentials from the backup");
        string committed=File.ReadAllText(path);bool rejected=false;
        using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))try{restored.Scale=.7;Storage.Write("settings.json",restored);}catch(IOException){rejected=true;}
        check(rejected && File.ReadAllText(path)==committed && Directory.GetFiles(Storage.DirectoryPath,"*.tmp.*").Length==0,"A failed atomic settings replacement preserves the committed file and removes temporary data");
        File.WriteAllText(path,"{}");File.WriteAllText(backup,"not json");
        check(Storage.Read<Config>("settings.json")==null && Storage.SettingsProblem.Length>0,"Invalid primary and backup files are reported instead of being accepted as an empty profile");
        Storage.Write("settings.json",new Config{Scale=.9});check(Storage.ReadBackup()!=null && Storage.ReadBackup().Scale==.9,"Saving a new valid profile replaces an unusable existing backup");
      }finally{Storage.DirectoryPath=previous;Storage.SettingsRecovered=recovered;Storage.SettingsProblem=problem;}
    }
    void CheckModePreferences(Action<bool,string> check) {
      var c=new Config{Compact=true,MonitorId="secondary",MonitorX=.8,MonitorY=.25,HasPosition=true,ShowUpdates=false,ShowClock=false};c.EnsureModeProfiles();
      check(c.ModeProfiles.Count==3 && c.ModeProfiles.Values.All(p=>p.MonitorId=="secondary" && !p.ShowUpdates && !p.ShowClock) && !object.ReferenceEquals(c.Mode("full"),c.Mode("compact")),"Legacy placement and visible blocks migrate into three independent mode preferences");
      c.ActivateMode("full");c.MonitorId="left";c.MonitorX=.1;c.MonitorY=.2;c.PositionAnchor="bl";c.EdgeMarginX=22;c.ShowNextDebut=false;c.RememberMode();
      c.ActivateMode("compact");c.MonitorId="right";c.MonitorX=.9;c.MonitorY=.7;c.ShowPlayButton=false;c.RememberMode();
      c.ActivateMode("micro");c.MonitorId="top";c.MonitorX=.5;c.MonitorY=.02;c.ShowPlayButton=true;c.RememberMode();
      c.ActivateMode("full");check(c.MonitorId=="left" && c.PositionAnchor=="bl" && c.EdgeMarginX==22 && !c.ShowNextDebut && c.ShowPlayButton,"Returning to full mode restores its own monitor, corner, margins and blocks");
      c.ActivateMode("compact");check(c.MonitorId=="right" && c.MonitorX==.9 && c.MonitorY==.7 && c.ShowNextDebut && !c.ShowPlayButton,"Compact placement and game-button visibility are independent of full mode");
      var saved=new JavaScriptSerializer().Deserialize<Config>(Json.Encode(c));saved.ActivateMode("micro");
      check(saved.MonitorId=="top" && saved.MonitorX==.5 && saved.ShowPlayButton,"All independent mode preferences survive serialization and process restart");
      DisplayArea primary=new DisplayArea{Id="primary",Primary=true,Work=new Rect(0,0,1920,1040)},left=new DisplayArea{Id="left",Work=new Rect(-2560,0,2560,1400)};
      saved.ActivateMode("full");var fallback=Placement.Preferred(saved,new[]{primary});
      check(fallback==primary && saved.MonitorId=="left" && saved.Mode("compact").MonitorId=="right" && Placement.Preferred(saved,new[]{primary,left})==left,"A missing mode-specific monitor falls back without losing any mode's intended display");
    }
    void CheckContinuityFeatures(Action<bool,string> check,string folder) {
      CheckBackupPolicy(check,folder);CheckModePreferences(check);
      string original=Json.Encode(config);var oldHistory=appearanceHistory;var oldExporter=reportExporter;var oldCalendarNow=calendarNow;var oldSchedule=schedule;var oldBanners=bannerState;Window dialog=null;
      var oldShortcuts=shortcuts;bool oldToggle=globalToggleKey,oldMenu=globalMenuKey;
      try {
        CheckModeViewPositions(check);
        config.MicroMode=config.Compact=false;config.EnsureModeProfiles();config.RememberMode();ApplyLayout(true);InitializeAppearanceHistory();
        dialog=SettingsWindow();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-10000;dialog.Show();dialog.UpdateLayout();
        var scale=(Slider)dialog.FindName("ScaleSlider");double originalScale=config.Scale;
        string activeDirectory=Storage.DirectoryPath;
        try {
          Storage.DirectoryPath=Path.Combine(folder,"first-ui-backup-"+Guid.NewGuid().ToString("N"));
          scale.Value=originalScale==.98?97:98;
          check(((Button)dialog.FindName("RestoreAppearanceButton")).IsEnabled && Storage.ReadBackup()!=null,"The first appearance save immediately enables restoration after its backup is created");
          UndoAppearance();
        }finally{Storage.DirectoryPath=activeDirectory;}
        InitializeAppearanceHistory();
        appearanceHistory.BeginGesture();scale.Value=95;scale.Value=90;scale.Value=85;appearanceHistory.EndGesture();
        ((Button)dialog.FindName("UndoAppearanceButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(config.Scale==originalScale && scale.Value==originalScale*100 && !appearanceHistory.CanUndo,"One undo restores an entire slider gesture and synchronizes the settings value");
        string token=config.ProtectedToken,role=config.RoleId;bool consent=config.NotifySanity,clicks=config.ClickThrough;
        var zoom=(Slider)dialog.FindName("CropZoomSlider");double oldZoom=zoom.Value;zoom.Value=oldZoom==125?135:125;
        check(UndoAppearance() && zoom.Value==oldZoom && config.ProtectedToken==token && config.RoleId==role && config.NotifySanity==consent && config.ClickThrough==clicks,"Undo restores portrait framing without changing account, notification consent or click-through");
        var previous=new JavaScriptSerializer().Deserialize<Config>(Json.Encode(config));previous.BackgroundOpacity=.67;previous.PortraitOpacity=.42;previous.ProtectedToken="OTHER-ACCOUNT";previous.RoleId="OTHER-ROLE";
        Storage.AtomicWrite(Path.Combine(Storage.DirectoryPath,"settings.json.bak"),Json.Encode(previous));
        ((Button)dialog.FindName("RestoreAppearanceButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(config.BackgroundOpacity==.67 && config.PortraitOpacity==.42 && config.ProtectedToken==token && config.RoleId==role,"Manual backup restoration applies appearance only and never replaces the connected account");
        check(UndoAppearance() && config.BackgroundOpacity!=.67,"Restoring a previous appearance is itself undoable");
        var blockMode=(ComboBox)dialog.FindName("BlockModeSelector");blockMode.SelectedIndex=1;bool fullPlay=config.ShowPlayButton;SetSwitch(dialog,"ShowPlaySwitch",false);
        check(config.ShowPlayButton==fullPlay && !config.Mode("compact").ShowPlayButton,"Editing compact-mode blocks leaves the active full mode unchanged");
        SetWidgetMode("compact");check(!config.ShowPlayButton && Find<Button>("CompactPlayButton").Visibility==Visibility.Collapsed,"Switching modes applies the separately edited compact blocks");
        blockMode.SelectedIndex=2;check(((FrameworkElement)((CheckBox)dialog.FindName("ShowNextSwitch")).Parent).Visibility==Visibility.Collapsed,"The ultra-small block editor exposes only content that exists in its single row");
        SetWidgetMode("full");
        var now=new DateTimeOffset(2026,10,15,21,30,0,TimeSpan.Zero);calendarNow=()=>now;
        schedule=null;bannerState=new BannerState{Debuts=new List<Debut>{new Debut{Name="Si",Source=NewsClient.NewsUrl+"/9560",EuropeStart=new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero),AsiaStart=new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero),EuropeEndUnix=new DateTimeOffset(2026,11,5,16,59,0,TimeSpan.Zero).ToUnixTimeSeconds(),AsiaEndUnix=new DateTimeOffset(2026,11,5,3,59,0,TimeSpan.Zero).ToUnixTimeSeconds()},new Debut{Name="Argent Flow",Source=NewsClient.NewsUrl+"/9560",EuropeStart=new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero),AsiaStart=new DateTimeOffset(2026,11,5,4,0,0,TimeSpan.Zero)}}};ShowCalendar();
        SelectCalendarDay(new DateTime(2027,2,1));((Button)calendarDialog.FindName("CalendarToday")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(calendarDay==Clock.Local(now).Date && calendarMonth==new DateTime(2026,10,1),"Today navigation uses the Eastern European calendar date and restores its containing month");
        var expected=CalendarData.NearestDay(CalendarData.Entries(schedule,bannerState,config.BannerRegion=="Asia"),now);
        ((Button)calendarDialog.FindName("CalendarUpcoming")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(expected.HasValue && calendarDay==expected.Value && calendarMonth.Month==expected.Value.Month,"The nearest-event button selects the next official event and its month");
        var ongoing=new CalendarEntry{Title="Overnight",At=now.AddHours(-2),End=now.AddHours(2)};
        check(CalendarData.NearestDay(new[]{ongoing},now)==Clock.Local(now).Date && CalendarData.NearestDay(new CalendarEntry[0],now)==null,"Nearest-event navigation handles ongoing overnight events and empty schedules");
        SaveImage(calendarDialog,Path.Combine(folder,"calendar-navigation.png"));
        bannerState=null;schedule=null;RenderCalendar();check(!((Button)calendarDialog.FindName("CalendarUpcoming")).IsEnabled,"The nearest-event button is disabled when no future or ongoing official event exists");calendarDialog.Close();
        string secret="TOKEN-MARKER-DO-NOT-EXPORT";config.ProtectedToken=secret;config.RoleId="ROLE-MARKER-DO-NOT-EXPORT";
        var failure=FailureRecord.From(new InvalidOperationException(secret+" C:\\Users\\Private\\account.json"));string report=DiagnosticsReport.Render(config,true,false,false,failure);
        check(report.Contains(BuildInfo.Version) && report.Contains(BuildInfo.Hash()) && !report.Contains(secret) && !report.Contains(config.RoleId) && !report.Contains("account.json") && !report.Contains("ProtectedToken"),"The diagnostic report identifies the exact build while excluding account fields, paths and exception messages");
        string reportPath=Path.Combine(folder,"safe-diagnostic-report.txt");reportExporter=body=>{File.WriteAllText(reportPath,body);return true;};
        ((Button)dialog.FindName("SaveReportButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(File.Exists(reportPath) && !File.ReadAllText(reportPath).Contains(secret) && ((TextBlock)dialog.FindName("ReportFeedback")).Text=="Report saved.","The actual report button exports the whitelisted report through an isolated file picker substitute");
        reportExporter=body=>false;((Button)dialog.FindName("SaveReportButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));check(((TextBlock)dialog.FindName("ReportFeedback")).Text=="","Cancelling report export leaves no false success message");
        reportExporter=body=>{throw new IOException(secret);};((Button)dialog.FindName("SaveReportButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));check(!((TextBlock)dialog.FindName("ReportFeedback")).Text.Contains(secret),"Report-save errors do not expose exception details or sensitive data");
        ((TextBlock)dialog.FindName("ReportFeedback")).Text="";
        config.ProtectedToken=token;config.RoleId=role;
        shortcuts=new ShortcutRegistry((id,m,k)=>k!=0x78,id=>{});shortcuts.Initial(ParseShortcuts(ShortcutValues()));string feedback;
        var values=ShortcutValues();values[2]="Ctrl+Shift+F9";string oldPlay=config.HotkeyPlay;
        check(!SaveShortcuts(values,out feedback) && feedback.Contains("Ctrl+Shift+F9") && feedback.Contains("Launch / open game") && config.HotkeyPlay==oldPlay,"A reserved shortcut names both the conflicting chord and its action while preserving preferences");
        values=ShortcutValues();values[0]=values[1]="Ctrl+Alt+J";
        check(!SaveShortcuts(values,out feedback) && feedback.Contains("Ctrl+Alt+J") && feedback.Contains("Show / hide") && feedback.Contains("Widget menu"),"Duplicate shortcuts identify the exact chord and both competing actions");
        var budget=new RecoveryBudget();int registrations=0,unregistrations=0;
        using(var recoveryTest=new CrashRecovery((command,flags)=>{check(command=="--recovered" && flags==12,"Windows recovery requests preserve the app mode without enabling update or reboot startup");registrations++;return 0;},()=>unregistrations++)) {
          check(recoveryTest.Start(budget,false,now) && registrations==1,"A normal launch registers Windows crash recovery once");
        }
        check(unregistrations==1,"A clean exit unregisters crash recovery");
        check(budget.Begin(true,now.AddSeconds(60)) && !budget.Begin(true,now.AddSeconds(120)) && budget.Begin(true,now.AddMinutes(16)),"Recovery attempts are limited within a time window and can resume after the cooldown");
        check(budget.Begin(false,now.AddMinutes(17)) && budget.Attempts==0,"An intentional manual launch starts a fresh recovery budget");
        var failing=new CrashRecovery((command,flags)=>unchecked((int)0x80004005),()=>{});
        check(!failing.Start(new RecoveryBudget(),false,now) && !failing.Registered,"An unavailable Windows recovery registration cannot crash normal startup");
        ((ScrollViewer)dialog.FindName("SettingsScroll")).ScrollToBottom();dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-about.png"));
      }finally {
        if(dialog!=null)dialog.Close();if(calendarDialog!=null)calendarDialog.Close();calendarNow=oldCalendarNow;reportExporter=oldExporter;schedule=oldSchedule;bannerState=oldBanners;
        if(shortcuts!=null && shortcuts!=oldShortcuts)shortcuts.Clear();shortcuts=oldShortcuts;globalToggleKey=oldToggle;globalMenuKey=oldMenu;
        var restored=new JavaScriptSerializer().Deserialize<Config>(original);foreach(var property in typeof(Config).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        appearanceHistory=oldHistory;ApplyDesignPreset();ApplyTextScale();ApplyArtworkAppearance();ApplyPortraitCrop();ApplyHoverOpacity();ApplyLayout(true);Paint();SavePosition();
      }
    }
    void CheckModeViewPositions(Action<bool,string> check) {
      string original=Json.Encode(config);var physical=displayOverride;
      try {
        var primary=new DisplayArea{Id="continuity-primary",Primary=true,Work=new Rect(0,0,1920,1040)};
        var secondary=new DisplayArea{Id="continuity-secondary",Work=new Rect(-2560,-200,2560,1440)};
        displayOverride=new List<DisplayArea>{primary,secondary};
        SetWidgetMode("full");config.MonitorId=secondary.Id;config.MonitorX=.2;config.MonitorY=.3;config.PositionAnchor="free";SavePosition();ApplyLayout(true);
        double fullX=window.Left,fullY=window.Top,fullHeight=window.Height;
        SetWidgetMode("compact");config.MonitorId=primary.Id;config.MonitorX=.75;config.MonitorY=.65;config.PositionAnchor="free";SavePosition();ApplyLayout(true);
        double compactX=window.Left,compactY=window.Top;
        SetWidgetMode("micro");config.MonitorId=secondary.Id;config.MonitorX=.8;config.MonitorY=.05;config.PositionAnchor="free";SavePosition();ApplyLayout(true);
        double microX=window.Left,microY=window.Top;
        SetWidgetMode("full");check(Math.Abs(window.Left-fullX)<1 && Math.Abs(window.Top-fullY)<1 && Math.Abs(window.Height-fullHeight)<1 && window.Left<0,"The full WPF view returns to its own size and position on a simulated monitor left of the primary");
        SetWidgetMode("compact");check(Math.Abs(window.Left-compactX)<1 && Math.Abs(window.Top-compactY)<1 && window.Left>=0,"The compact WPF view restores its separate position on another simulated monitor");
        SetWidgetMode("micro");check(Math.Abs(window.Left-microX)<1 && Math.Abs(window.Top-microY)<1 && microY!=fullY,"The ultra-small WPF row restores its own third position");
        displayOverride=new List<DisplayArea>{primary};SetWidgetMode("full");check(window.Left>=0 && config.MonitorId==secondary.Id,"Removing a simulated mode-specific display gives a visible fallback without changing the saved target");
        displayOverride=new List<DisplayArea>{primary,secondary};ApplyLayout(true);check(Math.Abs(window.Left-fullX)<1 && Math.Abs(window.Top-fullY)<1,"The current mode returns to its original position when the simulated display reconnects");
      }finally {
        var restored=new JavaScriptSerializer().Deserialize<Config>(original);foreach(var property in typeof(Config).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        displayOverride=physical;ApplyLayout(true);
      }
    }
  }
}
