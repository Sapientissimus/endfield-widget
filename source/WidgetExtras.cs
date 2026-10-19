using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EndfieldWidget {
  partial class Widget {
    IStartupRegistration startup;
    NotificationPolicy notifications;
    bool liveSanity,liveNews,extrasDisposed,globalMenuKey,globalToggleKey;
    bool diagnosticView;
    bool updateBlockVisible;
    ContextMenu widgetMenu;
    MenuItem lockMenu,compactMenu,microMenu;
    HwndSource keyboardSource;
    DispatcherTimer displayChangeTimer;
    DateTimeOffset nextNotificationCheck=DateTimeOffset.MinValue;
    List<DisplayArea> displayOverride;
    const int MenuHotkey=0x4e01;
    const int ToggleHotkey=0x4e02;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    IList<DisplayArea> CurrentDisplays(){return displayOverride??Placement.Displays();}
    void InitializeExtras() {
      config.NormalizeScales();
      config.NormalizePreferences();
      InitializeStudio();
      config.BackgroundOpacity=Placement.Finite(config.BackgroundOpacity) && config.BackgroundOpacity>0?Placement.Clamp(config.BackgroundOpacity,.6,1):1;
      startup=preview || Storage.Isolated?(IStartupRegistration)new MemoryStartupRegistration():new UserStartupRegistration();
      notifications=new NotificationPolicy(preview?null:Storage.Read<NotificationHistory>("notifications.json"));
      ApplyBackgroundOpacity();
      ApplyArtworkAppearance();
      var screens=CurrentDisplays();
      if(string.IsNullOrEmpty(config.MonitorId)) {
        if(config.HasPosition && Placement.Finite(config.Left) && Placement.Finite(config.Top)) {
          var pixels=PixelScale();var rect=new Rect(config.Left*pixels.X,config.Top*pixels.Y,420*pixels.X,710*pixels.Y);
          Placement.Remember(config,Placement.At(rect,screens),rect);
        } else {config.MonitorId=Placement.Preferred(config,screens).Id;config.MonitorX=1;config.MonitorY=.04;}
      }
      BuildWidgetMenu();ApplyLayout(true);
      window.ContextMenuOpening+=(s,e)=>widgetMenu.Placement=PlacementMode.MousePoint;
      window.IsVisibleChanged+=(s,e)=>{UpdateMenu();AdjustBackgroundTimer();};
      Find<Button>("CompactCloseButton").Click+=(s,e)=>window.Hide();
      Find<Button>("CompactSettingsButton").Click+=(s,e)=>Settings();
      Find<Button>("CompactRefreshButton").Click+=async(s,e)=>await Refresh(true);
      Find<Border>("UpcomingCard").MouseLeftButtonUp+=(s,e)=>OpenNextDebut();
      Find<TextBlock>("NextBannerSummary").MouseLeftButtonUp+=(s,e)=>OpenNextDebut();
      Find<TextBlock>("CompactVersion").MouseLeftButtonUp+=(s,e)=>Open(schedule.Source);
      window.Loaded+=(s,e)=>{
        ApplyLayout(true);
        if(!preview && !Storage.Isolated) {
          keyboardSource=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
          if(keyboardSource!=null){keyboardSource.AddHook(HotkeyMessage);InitializeConfiguredHotkeys();}
          SystemEvents.DisplaySettingsChanged+=DisplaysChanged;
        }
      };
      config.EnsureModeProfiles();
      displayChangeTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(400)};
      displayChangeTimer.Tick+=(s,e)=>{displayChangeTimer.Stop();if(!extrasDisposed)ApplyLayout(true);};
    }
    Point PixelScale() {
      var source=PresentationSource.FromVisual(window);
      if(source!=null && source.CompositionTarget!=null){var m=source.CompositionTarget.TransformToDevice;return new Point(m.M11,m.M22);}
      using(var graphics=System.Drawing.Graphics.FromHwnd(IntPtr.Zero))return new Point(graphics.DpiX/96.0,graphics.DpiY/96.0);
    }
    void ApplyLayout(bool restore) {
      updateBlockVisible=HasVisibleUpdate(DateTimeOffset.UtcNow);
      Find<TextBlock>("NextBannerSummary").Visibility=config.ShowNextDebut?Visibility.Visible:Visibility.Collapsed;
      double text=Math.Max(1,config.TextScale);
      double fullSanity=CardHeight("FullSanityCard","FullSanityLayout",370),fullUpdate=updateBlockVisible?CardHeight("FullUpdateCard","FullUpdateLayout",370):0,fullNext=config.ShowNextDebut?CardHeight("UpcomingCard","FullUpcomingLayout",370):0;
      double compactSanity=ContentHeight("CompactSanityLayout",318),compactUpdate=updateBlockVisible?CardHeight("CompactUpdateCard","CompactUpdateLayout",318):0,compactNext=config.ShowNextDebut?ContentHeight("NextBannerSummary",318):0;
      double fullHeight=264+fullSanity+fullUpdate+fullNext+text*((config.ShowPlayButton?53:0)+35),compactHeight=42+text*(52+29)+compactSanity+compactUpdate+compactNext;
      double baseWidth=config.MicroMode || config.Compact?360:420,baseHeight=config.MicroMode?Math.Max(66*text,ContentHeight("MicroDragHandle",322)+32):config.Compact?compactHeight:fullHeight;
      var fullRows=Find<Grid>("FullContent").RowDefinitions;fullRows[1].Height=new GridLength(fullSanity);fullRows[5].Height=new GridLength(35*text);
      var compactRows=((Grid)Find<Border>("CompactView").Child).RowDefinitions;compactRows[0].Height=new GridLength(52*text);compactRows[1].Height=new GridLength(compactSanity);compactRows[4].Height=new GridLength(29*text);
      var pixel=PixelScale();var screens=CurrentDisplays();var target=Placement.Preferred(config,screens);
      IntPtr handle=new WindowInteropHelper(window).Handle;DesktopNative.Rectangle bounds;
      var position=new Point(window.Left*pixel.X,window.Top*pixel.Y);
      if(!restore && handle!=IntPtr.Zero && DesktopNative.GetWindowRect(handle,out bounds)) {
        var current=new Rect(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
        position=current.TopLeft;target=Placement.At(current,screens);
      }
      Find<Border>("FullUpdateCard").Visibility=updateBlockVisible?Visibility.Visible:Visibility.Collapsed;
      Find<Border>("CompactUpdateCard").Visibility=updateBlockVisible?Visibility.Visible:Visibility.Collapsed;
      Find<RowDefinition>("FullUpdateRow").Height=new GridLength(fullUpdate);
      Find<RowDefinition>("CompactUpdateRow").Height=new GridLength(compactUpdate);
      Find<RowDefinition>("FullNextRow").Height=new GridLength(fullNext);
      Find<RowDefinition>("CompactNextRow").Height=new GridLength(compactNext);
      Find<RowDefinition>("FullPlayRow").Height=new GridLength(config.ShowPlayButton?53*text:0);
      Find<Border>("UpcomingCard").Visibility=config.ShowNextDebut?Visibility.Visible:Visibility.Collapsed;
      Find<TextBlock>("NextBannerSummary").Visibility=config.ShowNextDebut?Visibility.Visible:Visibility.Collapsed;
      Find<Button>("PlayButton").Visibility=config.ShowPlayButton?Visibility.Visible:Visibility.Collapsed;
      Find<Button>("CompactPlayButton").Visibility=config.ShowPlayButton?Visibility.Visible:Visibility.Collapsed;
      Find<Button>("MicroPlayButton").Visibility=config.ShowPlayButton?Visibility.Visible:Visibility.Collapsed;
      double radius=config.DesignPreset=="strict"?3:config.DesignPreset=="minimal"?8:17;
      ((Grid)Find<Border>("FullView").Child).Clip=new RectangleGeometry(new Rect(0,0,406,fullHeight-14),radius,radius);
      double fit=Math.Min((target.Work.Width-2*config.EdgeMarginX)/(baseWidth*pixel.X),(target.Work.Height-2*config.EdgeMarginY)/(baseHeight*pixel.Y));
      double scale=Math.Min(config.MicroMode?config.MicroScale:config.Compact?config.CompactScale:config.Scale,Math.Max(.25,fit));
      Find<Grid>("WidgetLayout").Width=baseWidth;Find<Grid>("WidgetLayout").Height=baseHeight;
      Find<Border>("FullView").Visibility=config.Compact || config.MicroMode?Visibility.Collapsed:Visibility.Visible;
      Find<Border>("CompactView").Visibility=config.Compact && !config.MicroMode?Visibility.Visible:Visibility.Collapsed;
      Find<Border>("MicroView").Visibility=config.MicroMode?Visibility.Visible:Visibility.Collapsed;
      window.Width=baseWidth*scale;window.Height=baseHeight*scale;
      if(restore)position=Placement.Restore(config,target,new Size(window.Width*pixel.X,window.Height*pixel.Y));
      else {
        position.X=Placement.Clamp(position.X,target.Work.Left,Math.Max(target.Work.Left,target.Work.Right-window.Width*pixel.X));
        position.Y=Placement.Clamp(position.Y,target.Work.Top,Math.Max(target.Work.Top,target.Work.Bottom-window.Height*pixel.Y));
      }
      window.Left=position.X/pixel.X;window.Top=position.Y/pixel.Y;
      if(handle!=IntPtr.Zero)DesktopNative.SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(position.X),(int)Math.Round(position.Y),0,0,DesktopNative.NoSize|DesktopNative.NoZOrder|DesktopNative.NoActivatePosition);
      UpdateMenu();
      cardLayoutDirty=false;
    }
    void UserMoved() {
      config.PositionAnchor="free";
      DesktopNative.Rectangle native;IntPtr handle=new WindowInteropHelper(window).Handle;
      if(handle!=IntPtr.Zero && DesktopNative.GetWindowRect(handle,out native)) {
        var rect=new Rect(native.Left,native.Top,native.Right-native.Left,native.Bottom-native.Top);
        Placement.Remember(config,Placement.At(rect,CurrentDisplays()),rect);
        var pixel=PixelScale();config.Left=rect.Left/pixel.X;config.Top=rect.Top/pixel.Y;
      }
      SavePosition();
    }
    Point SnapPosition(Rect rectangle){return Placement.Snap(rectangle,Placement.At(rectangle,CurrentDisplays()),config.EdgeMarginX,config.EdgeMarginY);}
    void DisplaysChanged(object sender,EventArgs args) {
      if(extrasDisposed)return;
      window.Dispatcher.BeginInvoke(new Action(()=>{if(!extrasDisposed){displayChangeTimer.Stop();displayChangeTimer.Start();}}));
    }
    void ApplyBackgroundOpacity() {
      foreach(string name in new[]{"Background","Raised","Card"}) {
        var original=window.Resources["Theme"+name+"Brush"] as SolidColorBrush;if(original==null)continue;
        var color=original.Color;color.A=(byte)Math.Round(color.A*config.BackgroundOpacity);
        var brush=new SolidColorBrush(color);brush.Freeze();window.Resources["Widget"+name+"Brush"]=brush;
      }
      foreach(string name in new[]{"Background","Glow","Fade","Shade","Clear"}) {
        object original=window.Resources["Theme"+name+"Color"];if(!(original is Color))continue;
        var color=(Color)original;color.A=(byte)Math.Round(color.A*config.BackgroundOpacity);window.Resources["Widget"+name+"Color"]=color;
      }
    }
    void BuildWidgetMenu() {
      widgetMenu=new ContextMenu {MinWidth=215,PlacementTarget=window};widgetMenu.Resources.MergedDictionaries.Add(window.Resources);
      lockMenu=new MenuItem {Header=L.T("Lock position"),IsCheckable=true};lockMenu.Click+=(s,e)=>{config.PositionLocked=lockMenu.IsChecked;if(desktopSurface!=null)desktopSurface.UpdateLock();SavePosition();};
      compactMenu=new MenuItem {Header=L.T("Compact mode"),IsCheckable=true};compactMenu.Click+=(s,e)=>SetWidgetMode(compactMenu.IsChecked?"compact":"full");
      microMenu=new MenuItem {Header=L.T("Mini mode"),IsCheckable=true};microMenu.Click+=(s,e)=>SetWidgetMode(microMenu.IsChecked?"micro":"full");
      var calendar=new MenuItem {Header=L.T("Calendar")};calendar.Click+=(s,e)=>ShowCalendar();
      var refreshItem=new MenuItem {Header=L.T("Refresh")};refreshItem.Click+=async(s,e)=>await Refresh(true);
      var settingsItem=new MenuItem {Header=L.T("Settings")};settingsItem.Click+=(s,e)=>window.Dispatcher.BeginInvoke(new Action(()=>Settings()));
      var hideItem=new MenuItem {Header=L.T("Hide")};hideItem.Click+=(s,e)=>window.Hide();
      widgetMenu.Items.Add(lockMenu);widgetMenu.Items.Add(compactMenu);widgetMenu.Items.Add(microMenu);widgetMenu.Items.Add(calendar);widgetMenu.Items.Add(new Separator());widgetMenu.Items.Add(refreshItem);widgetMenu.Items.Add(settingsItem);widgetMenu.Items.Add(new Separator());widgetMenu.Items.Add(hideItem);
      widgetMenu.Opened+=(s,e)=>UpdateMenu();window.ContextMenu=widgetMenu;
    }
    void UpdateMenu(){if(lockMenu!=null){lockMenu.IsChecked=config.PositionLocked;lockMenu.Header=config.PositionLocked?L.T("Unlock position"):L.T("Lock position");}if(compactMenu!=null)compactMenu.IsChecked=config.Compact && !config.MicroMode;if(microMenu!=null)microMenu.IsChecked=config.MicroMode;UpdateTrayPersonalization();}
    void ShowMenu(){ShowWidget();widgetMenu.Placement=PlacementMode.Bottom;widgetMenu.IsOpen=true;widgetMenu.Focus();}
    IntPtr HotkeyMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
      if(message!=0x312)return IntPtr.Zero;
      long id=wParam.ToInt64();
      if(id==MenuHotkey || id==ToggleHotkey || id==PlayHotkey || id==ModeHotkey || id==RefreshHotkey) {
        handled=true;DispatchShortcut((int)id);
      }
      return IntPtr.Zero;
    }
    void PaintUpcoming(DateTimeOffset now) {
      var next=BannerClient.Next(bannerState,now,config.BannerRegion=="Asia");
      string missing=bannersVerified?L.T("Not announced yet"):bannerLookupFailed?L.T("Could not update the date"):L.T("Checking announcements…");
      Text("NextOperator",next==null?missing:next.Name);
      Find<TextBlock>("NextOperator").MaxWidth=double.PositiveInfinity;
      Text("NextBannerDate",next==null?"":Clock.Date(config.BannerRegion=="Asia"?next.AsiaStart:next.EuropeStart)+L.T(" at ")+Clock.Time(config.BannerRegion=="Asia"?next.AsiaStart:next.EuropeStart)+"");
      Text("NextBannerCountdown",next==null?"":L.T("In ")+Clock.After((config.BannerRegion=="Asia"?next.AsiaStart:next.EuropeStart)-now));
      Text("NextBannerSummary",next==null?(bannersVerified?L.T("The next debut has not been announced"):missing):next.Name+L.T("\nIn ")+Clock.After((config.BannerRegion=="Asia"?next.AsiaStart:next.EuropeStart)-now));
      Find<TextBlock>("NextBannerCountdown").Visibility=Find<TextBlock>("NextBannerDate").Visibility=next==null?Visibility.Collapsed:Visibility.Visible;
      Find<Border>("UpcomingCard").Cursor=next==null?Cursors.Arrow:Cursors.Hand;
      Find<TextBlock>("NextBannerSummary").ToolTip=next==null?null:Find<TextBlock>("NextBannerDate").Text;
      Find<TextBlock>("CompactVersion").ToolTip=Find<TextBlock>("VersionLabel").Text;
    }
    void OpenNextDebut(){var next=BannerClient.Next(bannerState,DateTimeOffset.UtcNow,config.BannerRegion=="Asia");if(next!=null)Open(next.Source);}
    void CheckNotifications() {
      var now=DateTimeOffset.UtcNow;if(preview || Storage.Isolated || now<nextNotificationCheck)return;nextNotificationCheck=now.AddSeconds(15);
      int oldCount=notifications.History.Sent.Count;
      var notice=notifications.Next(config,sanity,schedule,now,liveSanity && sanityError.Length==0,liveNews && newsError.Length==0,accountToken.Length>0);
      bool sent=false;
      if(notice!=null && tray!=null) {
        IntPtr dialog=settingsDialog!=null?new WindowInteropHelper(settingsDialog).Handle:IntPtr.Zero;
        bool quiet=QuietState.ShouldPause(config.PauseInFullscreen,new WindowInteropHelper(window).Handle,dialog);
        sent=notifications.Deliver(notice,now,quiet,n=>{try{tray.ShowBalloonTip(6000,n.Title,n.Message,System.Windows.Forms.ToolTipIcon.Info);return true;}catch{return false;}});
      }
      if(sent || oldCount!=notifications.History.Sent.Count)try{Storage.Write("notifications.json",notifications.History);}catch{}
    }
    void DisposeExtras() {
      FlushSettingsCommit();
      extrasDisposed=true;if(displayChangeTimer!=null)displayChangeTimer.Stop();
      DisposeBackgroundLoop();
      banners.CancelDownloads();
      DisposeGameLauncher();
      if(crashRecovery!=null)crashRecovery.Dispose();
      if(calendarDialog!=null)calendarDialog.Close();if(shortcuts!=null)shortcuts.Clear();
      SystemEvents.DisplaySettingsChanged-=DisplaysChanged;
      if(keyboardSource!=null && !keyboardSource.IsDisposed){if(globalMenuKey)UnregisterHotKey(keyboardSource.Handle,MenuHotkey);if(globalToggleKey)UnregisterHotKey(keyboardSource.Handle,ToggleHotkey);keyboardSource.RemoveHook(HotkeyMessage);}
    }
  }
}
