using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EndfieldWidget {
  partial class Widget {
    static ShortcutSpec TestShortcut(int id,string text){ShortcutSpec spec;if(!ShortcutSpec.TryCreate(id,text,out spec))throw new Exception("Shortcut parse: "+text);return spec;}
    void CheckShortcutPolicy(Action<bool,string> check) {
      var h=TestShortcut(1,"Ctrl+Alt+H");check(h.Modifiers==3 && h.Key==0x48,"Shortcut parsing maps named modifiers and keys to the Windows registration values");
      ShortcutSpec invalid;check(!ShortcutSpec.TryCreate(1,"not a shortcut",out invalid) && !ShortcutSpec.TryCreate(1,"H",out invalid),"Invalid and unmodified letter shortcuts are rejected");
      var disabled=TestShortcut(2,"");check(disabled.Key==0,"An explicitly disabled shortcut remains unregistered");
      var occupied=new HashSet<string>();bool failNew=false;
      Func<uint,uint,string> key=(m,k)=>(m&15)+":"+k;
      var ids=new Dictionary<int,string>();
      var registry=new ShortcutRegistry((id,m,k)=>{string token=key(m,k);if(failNew && k==0x4a || occupied.Contains(token))return false;occupied.Add(token);ids[id]=token;return true;},id=>{string token;if(ids.TryGetValue(id,out token)){occupied.Remove(token);ids.Remove(id);}});
      registry.Initial(new[]{h});failNew=true;
      check(!registry.Replace(new[]{TestShortcut(1,"Ctrl+Alt+J")}) && registry.Active.ContainsKey(1) && registry.Active[1].Key==0x48,"A conflicting new shortcut restores the previously working registration");
      failNew=false;check(registry.Replace(new[]{TestShortcut(1,"Ctrl+Alt+J"),disabled}) && registry.Active.Count==1 && !occupied.Contains("3:72"),"Successful shortcut replacement releases the old chord and skips disabled actions");
      check(!registry.Replace(new[]{TestShortcut(1,"Ctrl+Alt+K"),TestShortcut(2,"Ctrl+Alt+K")}) && registry.Active[1].Key==0x4a,"Duplicate assignments are rejected before disturbing existing registrations");
      registry.Clear();check(occupied.Count==0 && ids.Count==0,"Disposing configured shortcuts releases all registered combinations");
    }
    void CheckBannerEndPolicy(Action<bool,string> check,string folder) {
      string fixture=Path.Combine(folder,"version-9560.json");string html=Json.Str(Json.Decode(File.ReadAllText(fixture)),"data","data");
      var notices=BannerClient.ParseOverview(html,"[Sanctuary of Ink] Version Update Notes","9560",1792026000);
      var si=notices.First(n=>n.Name=="Si");var next=notices.First(n=>n.Name=="Argent Flow");var start=new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero);
      long end=BannerClient.ResolveEnd(si.LocalEnd,start,false,2026),asia=BannerClient.ResolveEnd(si.AsiaLocalEnd,start,true,2026);
      check(si.EndChecked && end==new DateTimeOffset(2026,11,5,16,59,0,TimeSpan.Zero).ToUnixTimeSeconds() && asia==new DateTimeOffset(2026,11,5,3,59,0,TimeSpan.Zero).ToUnixTimeSeconds(),"The official version overview provides Si's distinct regional end times without using the next debut as an estimate");
      check(next.EndChecked && BannerClient.ResolveEnd(next.LocalEnd,start,false,2026)==0,"An announcement saying 'before version maintenance' leaves the end date unknown");
      check(BannerClient.ResolveEnd("Oct. 1, 2026 at 11:59",start,false,2026)==0 && BannerClient.ResolveEnd("not announced",start,false,2026)==0,"Invalid and pre-start banner ends cannot become countdowns");
      string regional="Availability: Asia Server: Oct. 15, 2026 at 12:00 – Nov. 5, 2026 at 11:59 (UTC+8) Americas/Europe Server: Oct. 15, 2026 at 12:00 – Nov. 6, 2026 at 09:00 (UTC-5)";
      check(BannerClient.AvailabilityEnd(regional,false).Contains("Nov. 6") && BannerClient.AvailabilityEnd(regional,true).Contains("Nov. 5"),"Regional availability parsing does not mix the Europe and Asia end dates");
      check(Clock.Time(DateTimeOffset.FromUnixTimeSeconds(end))=="18:59" && Clock.Time(DateTimeOffset.FromUnixTimeSeconds(asia))=="05:59","Official regional ends are converted using Eastern European winter-time rules");
      var debut=new Debut{Name="Si",Source=NewsClient.NewsUrl+"/9560",EuropeStart=start,AsiaStart=start,EuropeEndUnix=end,AsiaEndUnix=asia};
      var state=new BannerState {Debuts=new List<Debut>{debut,new Debut{Name="Si",Source=NewsClient.NewsUrl+"/9999",EuropeStart=start.AddDays(100),AsiaStart=start.AddDays(100)},new Debut{Name="Fake",Source="https://example.com/",EuropeStart=start}}};
      var entries=CalendarData.Entries(null,state,false);
      check(entries.Count==2 && entries.All(e=>e.Title.Contains("Si")),"The calendar excludes repeated operators and unofficial entries while retaining the original debut and its confirmed end");
      check(CalendarData.OnDay(entries,new DateTime(2026,11,5)).Count()==1 && CalendarData.OnDay(entries,new DateTime(2026,11,6)).Count()==0,"Calendar event days follow Eastern European dates rather than machine-local or UTC dates");
      debut.EuropeEndUnix=0;check(CalendarData.Entries(null,state,false).Count==1,"Unknown banner ends do not produce calendar events");
      var night=new Schedule{Version="Overnight",Source=NewsClient.NewsUrl+"/1234",Start=new DateTimeOffset(2026,11,1,21,0,0,TimeSpan.Zero),End=new DateTimeOffset(2026,11,2,2,0,0,TimeSpan.Zero)};
      var overnight=CalendarData.Entries(night,null,false);check(CalendarData.OnDay(overnight,new DateTime(2026,11,1)).Any() && CalendarData.OnDay(overnight,new DateTime(2026,11,2)).Any(),"An overnight maintenance period appears on both affected Eastern European dates");
    }
    async Task CheckStudioFeatures(Action<bool,string> check,string folder) {
      CheckShortcutPolicy(check);CheckBannerEndPolicy(check,folder);
      var previous=Json.Encode(config);var oldBanners=bannerState;var oldSchedule=schedule;
      var oldActivity=gameActivity;var oldProbe=gameProbe;var oldLauncher=gameLauncher;var oldFocus=gameFocuser;var oldStartProbe=gameStartProbe;var oldStart=gameStartedAt;bool oldApprox=approximateSession;
      var oldShortcuts=shortcuts;bool oldToggle=globalToggleKey,oldMenu=globalMenuKey;Window dialog=null;
      var original=BannerClient.Select(bannerState,DateTimeOffset.UtcNow,config.BannerRegion=="Asia");
      try {
        var legacy=new JavaScriptSerializer().Deserialize<Config>("{\"Scale\":0.9,\"CompactScale\":1.2,\"BackgroundOpacity\":0.6,\"ShowWatermark\":false}");legacy.NormalizeStudio();
        check(legacy.Scale==.9 && legacy.CompactScale==1.2 && legacy.BackgroundOpacity==.6 && !legacy.ShowWatermark && legacy.TextScale==1 && legacy.MicroScale==1 && legacy.HotkeyToggle=="Ctrl+Alt+H","Old profiles retain their appearance and shortcuts while new studio preferences receive independent defaults");
        config.TextScale=1;config.MicroScale=1;config.MicroMode=false;config.Compact=false;config.Scale=config.CompactScale=1;config.DesignPreset="soft";config.SanityStyle="bar";config.FadeWhenIdle=false;ApplyDesignPreset();ApplyTextScale();ApplyLayout(true);Paint();
        var fullSize=new Size(window.Width,window.Height);SetWidgetMode("micro");
        check(window.Width==360 && window.Height==66 && Find<Border>("MicroView").Visibility==Visibility.Visible && Find<Border>("FullView").Visibility==Visibility.Collapsed && Find<Border>("CompactView").Visibility==Visibility.Collapsed,"The ultra-small mode is a single compact row with its own rendering surface");
        Paint();Find<Button>("MicroSettingsButton").Focus();window.UpdateLayout();SaveImage(window,Path.Combine(folder,"widget-micro.png"));
        check(Find<TextBlock>("MicroSanity").Text.Contains("/") && Find<TextBlock>("MicroRecovery").Text.Length>0,"The small row shows the same Sanity reading and recovery state as the full widget");
        int launches=0;gameActivity=new GameActivity();gameProbe=()=>false;gameLauncher=()=>launches++;Find<Button>("MicroPlayButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await gameLaunchTask;
        check(launches==1,"The ultra-small play action uses the shared Steam launcher");
        config.MicroScale=1.15;ApplyLayout(true);check(Math.Abs(window.Width-414)<.01 && config.Scale==1 && config.CompactScale==1,"Changing the ultra-small scale leaves both existing mode scales intact");
        SetWidgetMode("full");check(new Size(window.Width,window.Height)==fullSize,"Returning from the ultra-small row restores the full view dimensions");
        double baseFont=Find<TextBlock>("SanityNumber").FontSize;config.TextScale=1.2;ApplyTextScale();ApplyLayout(true);check(Math.Abs(Find<TextBlock>("SanityNumber").FontSize-baseFont*1.2)<.01 && Find<TextBlock>("SanityNumber").FontSize==Find<TextBlock>("SanityMaximum").FontSize && window.Width==420 && window.Height>fullSize.Height,"Text can grow independently while both Sanity values retain equal sizing and receive extra vertical room");
        window.UpdateLayout();SaveImage(window,Path.Combine(folder,"widget-large-text.png"));config.TextScale=1;ApplyTextScale();ApplyLayout(true);
        foreach(string style in new[]{"bar","ring","numbers"}){config.SanityStyle=style;Paint();check((Find<Grid>("FullSanityRing").Visibility==Visibility.Visible)==(style=="ring") && (Find<ProgressBar>("SanityBar").Visibility==Visibility.Visible)==(style=="bar"),"Sanity style selects its intended rendering: "+style);window.UpdateLayout();SaveImage(window,Path.Combine(folder,"widget-sanity-"+style+".png"));SetWidgetMode("compact");Find<Button>("CompactSettingsButton").Focus();window.UpdateLayout();SaveImage(window,Path.Combine(folder,"compact-sanity-"+style+".png"));SetWidgetMode("full");}
        config.SanityStyle="bar";UpdateSanityStyle();var accent=currentTheme.Accent;
        foreach(string preset in new[]{"strict","minimal","soft"}){config.DesignPreset=preset;ApplyDesignPreset();ApplyLayout(true);check(currentTheme.Accent==accent && ((SolidColorBrush)window.Resources["ThemeAccentBrush"]).Color==accent,"The "+preset+" preset preserves the current operator's automatic accent palette");window.UpdateLayout();SaveImage(window,Path.Combine(folder,"widget-preset-"+preset+".png"));}
        check(Find<Border>("FullView").CornerRadius.TopLeft==18,"Returning to the soft preset restores the original rounded appearance");
        config.FadeWhenIdle=false;ApplyHoverOpacity();check(window.Opacity==1,"Disabling idle dimming restores full widget opacity");
        var seed=BannerState.Seed();var first=seed.Debuts.First(d=>d.Key=="si");var other=seed.Debuts.First(d=>d.Key!="si");
        ApplyDebut(first);var camera=config.Frame("si",false);camera.Zoom=.8;camera.X=-20;camera.Y=15;var compactCamera=config.Frame("si",true);compactCamera.Zoom=1.35;compactCamera.X=12;ApplyPortraitCrop();
        var fullTransform=(TransformGroup)Find<Grid>("FullPortraitTransform").RenderTransform;var smallTransform=(TransformGroup)Find<Grid>("CompactPortraitTransform").RenderTransform;
        check(((ScaleTransform)fullTransform.Children[0]).ScaleX==.8 && ((ScaleTransform)smallTransform.Children[0]).ScaleX==1.35,"Full and compact portraits use separate camera zoom and position preferences");
        ApplyDebut(other);check(((ScaleTransform)((TransformGroup)Find<Grid>("FullPortraitTransform").RenderTransform).Children[0]).ScaleX==1,"A different debut operator starts with its own default framing");
        ApplyDebut(first);check(((ScaleTransform)((TransformGroup)Find<Grid>("FullPortraitTransform").RenderTransform).Children[0]).ScaleX==.8,"Returning to an operator restores that operator's previous crop");
        check(Find<Grid>("PortraitFade").ClipToBounds && Find<Grid>("FullPortraitTransform").OpacityMask!=null && Find<Grid>("CompactPortraitTransform").OpacityMask!=null,"Fixed and moving portrait masks protect the crop edges even when zooming out");
        window.UpdateLayout();SaveImage(window,Path.Combine(folder,"widget-crop.png"));
        var serialized=new JavaScriptSerializer().Deserialize<Config>(Json.Encode(config));serialized.NormalizeStudio();check(serialized.Frame("si",false).Zoom==.8 && serialized.Frame("si",true).Zoom==1.35,"Per-operator and per-mode crop preferences survive profile serialization");
        int focuses=0;var now=DateTimeOffset.UtcNow;gameActivity=new GameActivity();gameProbe=()=>true;gameStartProbe=()=>now.AddMinutes(-32);gameFocuser=()=>{focuses++;return true;};PollGameActivity(now);PaintSession(now.AddSeconds(7));
        check(!approximateSession && Find<TextBlock>("SessionLabel").Text.Contains("32:07"),"A known game process start provides the actual session duration");
        await LaunchGame();check(focuses==1 && launches==1,"Clicking an existing game restores its window and never sends a second launch request");
        gameFocuser=()=>false;await LaunchGame();check(gameLaunchError.Contains("window") && Find<Button>("PlayButton").IsEnabled,"A temporarily unavailable game window leaves a recoverable focus action");
        gameActivity=new GameActivity();gameStartProbe=()=>null;PollGameActivity(now);PaintSession(now.AddMinutes(3));check(approximateSession && Find<TextBlock>("SessionLabel").Text.Contains("≈03:00"),"When Windows cannot expose process start time, the observed session duration is clearly approximate");
        config.ShowSessionTime=false;PaintSession(now);check(Find<TextBlock>("SessionLabel").Visibility==Visibility.Collapsed && Find<TextBlock>("CompactSessionLabel").Visibility==Visibility.Collapsed,"The session timer can be hidden independently in both existing modes");
        gameActivity=new GameActivity();gameProbe=()=>false;gameLaunchError="";UpdateGameLaunchControls();
        config.ShowSessionTime=true;dialog=SettingsWindow();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=dialog.Top=-10000;dialog.Show();dialog.UpdateLayout();
        ((Slider)dialog.FindName("CropZoomSlider")).Value=110;((ComboBox)dialog.FindName("CropModeSelector")).SelectedIndex=1;((Slider)dialog.FindName("CropYSlider")).Value=-25;
        check(config.Frame("si",false).Zoom==1.1 && config.Frame("si",true).Y==-25,"Crop settings target the selected operator and selected rendering mode");
        ((Button)dialog.FindName("CropResetButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));check(config.Frame("si",true).Zoom==1 && config.Frame("si",true).Y==0 && config.Frame("si",false).Zoom==1.1,"Resetting the camera affects only the selected mode and operator");
        ((ComboBox)dialog.FindName("PresetSelector")).SelectedIndex=1;((ComboBox)dialog.FindName("SanityStyleSelector")).SelectedIndex=1;((Slider)dialog.FindName("TextScaleSlider")).Value=110;
        check(config.DesignPreset=="strict" && config.SanityStyle=="ring" && config.TextScale==1.1,"New appearance controls apply and persist their selected values");
        var screens=CurrentDisplays();((ComboBox)dialog.FindName("AnchorSelector")).SelectedIndex=2;((TextBox)dialog.FindName("MarginXInput")).Text="31";((TextBox)dialog.FindName("MarginYInput")).Text="27";((Button)dialog.FindName("ApplyPlacementButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var screen=Placement.Preferred(config,screens);var pixel=PixelScale();check(config.PositionAnchor=="tr" && config.EdgeMarginX==31 && config.EdgeMarginY==27 && Math.Abs(window.Top*pixel.Y-screen.Work.Top-27)<2 && Math.Abs(window.Left*pixel.X-(screen.Work.Right-window.Width*pixel.X-31))<2,"The placement controls choose the monitor, anchored corner and exact pixel margins");
        ((TextBox)dialog.FindName("MarginXInput")).Text="9999";((Button)dialog.FindName("ApplyPlacementButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));check(config.EdgeMarginX==31 && ((TextBlock)dialog.FindName("PlacementFeedback")).Text.Contains("300"),"Invalid exact placement values are rejected without moving the saved target");
        string feedback;shortcuts=null;check(SaveShortcuts(new[]{"Ctrl+Shift+F8","Ctrl+Shift+F9","Ctrl+Shift+F10","Ctrl+Shift+F11",""},out feedback) && config.HotkeyToggle=="Ctrl+Shift+F8" && config.HotkeyRefresh=="","The shortcut controls save all independent actions and an explicit disabled action on an isolated backend");
        check(!SaveShortcuts(new[]{"Ctrl+Shift+F8","Ctrl+Shift+F8","","",""},out feedback) && config.HotkeyMenu=="Ctrl+Shift+F9","A duplicate shortcut edit leaves the previously saved settings intact");
        config.TextScale=1;config.DesignPreset="soft";config.SanityStyle="bar";ApplyTextScale();ApplyDesignPreset();SetWidgetMode("full");
        string overview=Json.Str(Json.Decode(File.ReadAllText(Path.Combine(folder,"version-9560.json"))),"data","data");var official=BannerClient.ParseOverview(overview,"[Sanctuary of Ink] Version Update Notes","9560",1792026000);
        bannerState=new BannerState{Debuts=new List<Debut>()};foreach(var notice in official){var start=notice.Name=="Si"?new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero):new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero);bannerState.Debuts.Add(new Debut{Name=notice.Name,Source=NewsClient.NewsUrl+"/9560",Key=notice.Name=="Si"?"si":"argent-flow",EuropeStart=start,AsiaStart=start,EuropeEndUnix=BannerClient.ResolveEnd(notice.LocalEnd,start,false,2026)});}
        ShowCalendar();var calendar=calendarDialog;calendarDay=new DateTime(2026,11,5);calendarMonth=new DateTime(2026,11,1);RenderCalendar();calendar.UpdateLayout();
        check(((UniformGrid)calendar.FindName("CalendarDays")).Children.Count==42,"The calendar renders a complete Monday-first six-week month grid");SaveImage(calendar,Path.Combine(folder,"calendar.png"));
        ((Button)calendar.FindName("CalendarNext")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));check(calendarMonth.Month==12,"The mini-calendar navigates between months");calendar.Close();check(calendarDialog==null,"Closing the calendar releases its independent window reference");
        var saved=Storage.Read<Config>("settings.json");check(saved.PortraitFrames.Count>0 && saved.HotkeyToggle=="Ctrl+Shift+F8" && saved.PositionAnchor=="tr","Camera, placement and shortcut preferences are saved together without losing unrelated profile settings");
        ((ScrollViewer)dialog.FindName("SettingsScroll")).ScrollToTop();dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-studio.png"));
      } finally {
        if(dialog!=null)dialog.Close();if(calendarDialog!=null)calendarDialog.Close();if(shortcuts!=null && shortcuts!=oldShortcuts)shortcuts.Clear();shortcuts=oldShortcuts;globalToggleKey=oldToggle;globalMenuKey=oldMenu;
        var restored=new JavaScriptSerializer().Deserialize<Config>(previous);foreach(var property in typeof(Config).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        config.NormalizeStudio();bannerState=oldBanners;schedule=oldSchedule;gameActivity=oldActivity;gameProbe=oldProbe;gameLauncher=oldLauncher;gameFocuser=oldFocus;gameStartProbe=oldStartProbe;gameStartedAt=oldStart;approximateSession=oldApprox;gameLaunchError="";
        currentArtKey="";ApplyDebut(original);ApplyDesignPreset();ApplyTextScale();ApplyArtworkAppearance();ApplyPortraitCrop();ApplyHoverOpacity();ApplyLayout(true);Paint();UpdateGameLaunchControls();SavePosition();
      }
    }
  }
}
