using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Web.Script.Serialization;

namespace EndfieldWidget {
  partial class Widget {
    static void EnterNumber(Window dialog,string name,string text) {
      var input=(TextBox)dialog.FindName(name);input.Text=text;
      input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(dialog),0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent});
    }
    static void SetSwitch(Window dialog,string name,bool value){var control=(CheckBox)dialog.FindName(name);control.IsChecked=value;control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
    static void CheckPersonalizedPolicies(Action<bool,string> check) {
      var old=new JavaScriptSerializer().Deserialize<Config>("{\"Scale\":0.9,\"Compact\":true,\"NotifySanity\":true}");old.NormalizePreferences();
      check(old.ShowPlayButton && old.ShowNextDebut && old.ShowClock && old.ShowUpdates && !old.ClickThrough && old.NotifySanity && old.SanityThreshold==90 && old.MaintenanceLeadMinutes==30,"Older profiles retain visible content, existing notification consent and the old notification thresholds");
      var hidden=new JavaScriptSerializer().Deserialize<Config>("{\"ShowPlayButton\":false,\"ShowClock\":false,\"ShowNextDebut\":false,\"ShowUpdates\":false,\"ClickThrough\":true}");
      check(!hidden.ShowPlayButton && !hidden.ShowClock && !hidden.ShowNextDebut && !hidden.ShowUpdates && hidden.ClickThrough,"Explicit saved content and mouse preferences override migration defaults");
      var now=new DateTimeOffset(2026,10,15,8,0,0,TimeSpan.Zero);
      var options=new Config {RoleId="fixture",NotifySanity=true,SanityThreshold=80};
      var reading=new Sanity {RoleId="fixture",Current=79,Maximum=100,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddMinutes(31).ToUnixTimeSeconds()};
      var policy=new NotificationPolicy(null);
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Custom percentage notification waits below the chosen threshold");
      reading.Current=80;var notice=policy.Next(options,reading,null,now,true,false,true);
      check(notice!=null && notice.Title.Contains("80%"),"The exact custom percentage threshold triggers its matching message");
      policy.Deliver(notice,now,false,n=>true);options.SanityThreshold=75;
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Changing a threshold does not duplicate an already delivered Sanity notification");
      options.SanityThreshold=98;policy.Next(options,reading,null,now,true,false,true);reading.Current=99;
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Raising a percentage threshold and crossing it again cannot rearm the same recovery cycle");
      options.SanityNoticeMode="minutes";options.SanityLeadMinutes=240;
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Changing notification mode preserves the same-cycle deduplication marker");reading.Current=80;
      options.SanityNoticeMode="minutes";options.SanityLeadMinutes=30;policy=new NotificationPolicy(null);
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Time-based Sanity notification waits outside the selected lead time");
      reading.FullAt=now.AddMinutes(30).ToUnixTimeSeconds();notice=policy.Next(options,reading,null,now,true,false,true);
      check(notice!=null && notice.Message.Contains("30 minutes"),"The exact time-to-full boundary triggers a time-based Sanity notification");
      check(!policy.Deliver(notice,now,true,n=>{throw new Exception("DND must suppress delivery");}) && policy.History.Sent.Count==0,"Do Not Disturb defers a time-based alert without consuming it");
      check(policy.Deliver(notice,now,false,n=>true) && policy.Next(options,reading,null,now,true,false,true)==null,"Time-based Sanity alerts deliver once after Do Not Disturb ends");
      policy=new NotificationPolicy(new JavaScriptSerializer().Deserialize<NotificationHistory>(Json.Encode(policy.History)));
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Time-based Sanity deduplication survives a restart");
      options.SanityLeadMinutes=5;policy.Next(options,reading,null,now,true,false,true);options.SanityLeadMinutes=60;
      check(policy.Next(options,reading,null,now,true,false,true)==null,"Shortening then widening the time threshold cannot rearm an existing alert");options.SanityLeadMinutes=30;
      reading.FullAt=now.AddHours(2).ToUnixTimeSeconds();policy.Next(options,reading,null,now,true,false,true);reading.FullAt=now.AddMinutes(20).ToUnixTimeSeconds();
      check(policy.Next(options,reading,null,now,true,false,true)!=null,"A fresh later recovery cycle rearms the time-based notification");
      reading.FullAt=0;check(policy.Next(options,reading,null,now,true,false,true)==null,"Missing recovery timing cannot create a guessed time-based notification");
      reading.FullAt=now.AddMinutes(20).ToUnixTimeSeconds();
      check(policy.Next(options,reading,null,now.AddMinutes(11),true,false,true)==null && policy.Next(options,reading,null,now,false,false,true)==null,"Time-based notifications reject stale readings and failed refreshes");
      var update=new Schedule {Version="Fixture",Source=NewsClient.NewsUrl+"/1234",Checked=now,Start=now.AddMinutes(90),End=now.AddHours(5)};
      options.NotifySanity=false;options.NotifyMaintenance=true;options.MaintenanceLeadMinutes=60;policy=new NotificationPolicy(null);
      check(policy.Next(options,reading,update,now,false,true,false)==null,"Maintenance notification waits outside the configured lead time");
      options.MaintenanceLeadMinutes=120;notice=policy.Next(options,reading,update,now,false,true,false);
      check(notice!=null && notice.Message.Contains("12:30"),"A custom maintenance lead time retains the correct Eastern European time");
      policy.Deliver(notice,now,false,n=>true);options.MaintenanceLeadMinutes=180;
      check(policy.Next(options,reading,update,now,false,true,false)==null,"Changing maintenance lead time does not duplicate the same maintenance alert");
    }
    void CheckPersonalizedView(Action<bool,string> check,Window dialog,string folder) {
      var savedSchedule=schedule;double scale=config.Scale,small=config.CompactScale;bool compact=config.Compact;
      try {
        CheckPersonalizedPolicies(check);
        config.Compact=false;ApplyLayout(true);
        EnterNumber(dialog,"ScaleInput","92%");window.UpdateLayout();
        check(Math.Abs(config.Scale-.92)<.001 && Math.Abs(window.Width-386.4)<.6,"Typing an exact percentage and Enter applies a non-five-percent full scale with pixel rounding");
        EnterNumber(dialog,"CompactScaleInput","83");
        check(Math.Abs(config.CompactScale-.83)<.001 && Math.Abs(config.Scale-.92)<.001,"Exact compact scale entry preserves the full-mode scale");
        EnterNumber(dialog,"ScaleInput","131");EnterNumber(dialog,"CompactScaleInput","NaN");
        check(Math.Abs(config.Scale-.92)<.001 && Math.Abs(config.CompactScale-.83)<.001,"Invalid and out-of-range scale input cannot change either saved scale");
        ((Button)dialog.FindName("ScaleReset")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(config.Scale==1 && config.CompactScale==.83 && ((TextBox)dialog.FindName("ScaleInput")).Text=="100","Resetting full scale leaves compact scale untouched");
        ((Button)dialog.FindName("CompactScaleReset")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(config.CompactScale==1 && ((Slider)dialog.FindName("CompactScaleSlider")).Value==100,"Compact scale resets independently to 100 percent");
        schedule=new Schedule {Version="Fixture future",Source=NewsClient.NewsUrl+"/1234",Start=DateTimeOffset.UtcNow.AddHours(2),End=DateTimeOffset.UtcNow.AddHours(8)};
        Paint();ApplyLayout(true);window.UpdateLayout();double fullShown=window.Height,fullHiddenRows=Find<RowDefinition>("FullNextRow").ActualHeight+Find<RowDefinition>("FullPlayRow").ActualHeight+Find<RowDefinition>("FullUpdateRow").ActualHeight;
        SetSwitch(dialog,"ShowNextSwitch",false);SetSwitch(dialog,"ShowPlaySwitch",false);SetSwitch(dialog,"ShowUpdateSwitch",false);window.UpdateLayout();
        check(Math.Abs(window.Height-(fullShown-fullHiddenRows))<1 && Find<RowDefinition>("FullNextRow").ActualHeight==0 && Find<RowDefinition>("FullPlayRow").ActualHeight==0 && Find<Border>("FullUpdateCard").Visibility==Visibility.Collapsed,"Hidden full-mode cards remove their complete measured rows and window height");
        check(window.FindName("ClockBlock")==null && Find<Button>("SettingsButton").Visibility==Visibility.Visible,"The removed clock leaves the widget controls available");
        SaveImage(window,Path.Combine(folder,"widget-minimal.png"));
        SetWidgetMode("compact");((ComboBox)dialog.FindName("BlockModeSelector")).SelectedIndex=1;
        check(config.ShowNextDebut && config.ShowPlayButton,"Compact mode retains its independent blocks when full-mode blocks are hidden");
        window.UpdateLayout();double compactShown=window.Height,compactHiddenRows=Find<RowDefinition>("CompactNextRow").ActualHeight+Find<RowDefinition>("CompactUpdateRow").ActualHeight;
        SetSwitch(dialog,"ShowNextSwitch",false);SetSwitch(dialog,"ShowPlaySwitch",false);SetSwitch(dialog,"ShowUpdateSwitch",false);window.UpdateLayout();
        check(Math.Abs(window.Height-(compactShown-compactHiddenRows))<1 && Find<TextBlock>("NextBannerSummary").Visibility==Visibility.Collapsed && window.FindName("CompactClockLabel")==null,"Compact mode removes its own measured content without empty card rows");
        SaveImage(window,Path.Combine(folder,"compact-minimal.png"));Paint();
        check(Find<Border>("CompactUpdateCard").Visibility==Visibility.Collapsed,"Automatic schedule painting respects the user's hidden update block");
        var stored=Storage.Read<Config>("settings.json");
        check(stored!=null && !stored.ShowNextDebut && !stored.ShowUpdates && !stored.ShowPlayButton,"Content choices survive serialization");
        SetSwitch(dialog,"ShowNextSwitch",true);SetSwitch(dialog,"ShowPlaySwitch",true);SetSwitch(dialog,"ShowUpdateSwitch",true);
        SetWidgetMode("full");((ComboBox)dialog.FindName("BlockModeSelector")).SelectedIndex=0;
        SetSwitch(dialog,"ShowNextSwitch",true);SetSwitch(dialog,"ShowPlaySwitch",true);SetSwitch(dialog,"ShowUpdateSwitch",true);ApplyLayout(true);window.UpdateLayout();
        check(Math.Abs(window.Height-fullShown)<1 && Find<Border>("FullUpdateCard").Visibility==Visibility.Visible && Find<Button>("PlayButton").IsEnabled,"Restoring blocks restores the measured original layout and the active game button");
        var blocks=(Expander)dialog.FindName("VisibleBlocks");blocks.IsExpanded=true;var topScroll=(ScrollViewer)dialog.FindName("SettingsScroll");topScroll.ScrollToTop();dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-blocks.png"));blocks.IsExpanded=false;
        SetSwitch(dialog,"SanityNotifySwitch",true);SetSwitch(dialog,"MaintenanceNotifySwitch",true);
        ((ComboBox)dialog.FindName("SanityNoticeMode")).SelectedIndex=1;EnterNumber(dialog,"SanityNoticeValue","45");EnterNumber(dialog,"MaintenanceNoticeValue","60");
        check(config.SanityNoticeMode=="minutes" && config.SanityLeadMinutes==45 && config.MaintenanceLeadMinutes==60,"Notification controls persist the selected mode and both lead times");
        ((ComboBox)dialog.FindName("SanityNoticeMode")).SelectedIndex=0;EnterNumber(dialog,"SanityNoticeValue","85");
        check(config.SanityThreshold==85 && config.SanityLeadMinutes==45 && ((TextBlock)dialog.FindName("SanityNoticeUnit")).Text=="%","Percentage and time-based Sanity preferences retain their own values");
        EnterNumber(dialog,"SanityNoticeValue","0");EnterNumber(dialog,"MaintenanceNoticeValue","999");
        check(config.SanityThreshold==85 && config.MaintenanceLeadMinutes==60,"Invalid notification thresholds cannot alter saved preferences");
        EnterNumber(dialog,"SanityNoticeValue","85");EnterNumber(dialog,"MaintenanceNoticeValue","60");
        var scroll=(ScrollViewer)dialog.FindName("SettingsScroll");scroll.ScrollToBottom();dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-notifications.png"));
        SetSwitch(dialog,"SanityNotifySwitch",false);SetSwitch(dialog,"MaintenanceNotifySwitch",false);
        check(((FrameworkElement)dialog.FindName("SanityNoticeOptions")).Visibility==Visibility.Collapsed,"Disabled notifications fold away their additional settings");
        widgetMenu.IsOpen=true;bool handled=false;var before=new Point(window.Left,window.Top);HotkeyMessage(IntPtr.Zero,0x312,new IntPtr(ToggleHotkey),IntPtr.Zero,ref handled);
        check(handled && !window.IsVisible && !widgetMenu.IsOpen,"The hide hotkey message hides the widget and dismisses its context menu");handled=false;HotkeyMessage(IntPtr.Zero,0x312,new IntPtr(ToggleHotkey),IntPtr.Zero,ref handled);
        check(handled && window.IsVisible && new Point(window.Left,window.Top)==before,"The show hotkey message restores the widget at its saved place");
      } finally {
        schedule=savedSchedule;config.Scale=scale;config.CompactScale=small;config.Compact=compact;
        config.ShowNextDebut=config.ShowPlayButton=config.ShowUpdates=config.ShowClock=true;config.ClickThrough=false;config.NotifySanity=config.NotifyMaintenance=false;ApplyLayout(true);SavePosition();
      }
    }
    void CheckNativePersonalization(Action<bool,string> check,Window dialog) {
      IntPtr handle=desktopSurface.Handle;bool wasLocked=config.PositionLocked;config.PositionLocked=false;
      SetSwitch(dialog,"ClickThroughSwitch",true);
      long style=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);
      check(config.ClickThrough && !window.IsHitTestVisible && (style&DesktopNative.Transparent)!=0 && (style&DesktopNative.Layered)!=0,"Click-through uses the real layered native window while suppressing widget hit tests");
      check(desktopSurface.IsAttached && (DesktopNative.GetStyle(handle,DesktopNative.StyleIndex)&DesktopNative.Child)==0 && !desktopSurface.TryUserMove(100,100),"Click-through preserves desktop ownership and rejects dragging");
      window.Hide();ShowWidget();style=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);
      check((style&DesktopNative.Transparent)!=0,"Click-through remains enabled across hide and show");
      SetSwitch(dialog,"ClickThroughSwitch",false);style=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);
      check(!config.ClickThrough && window.IsHitTestVisible && (style&DesktopNative.Transparent)==0,"Independent settings restore normal pointer handling");
      using(var menu=new System.Windows.Forms.ContextMenuStrip()) {
        menu.Items.Add("Show");menu.Items.Add("Menu");menu.Items.Add("Settings");
        try {
          BuildTrayPersonalization(menu);SetClickThrough(true);
          check(trayClickThrough.Checked,"The tray menu reflects enabled click-through");
          trayClickThrough.PerformClick();
          check(!config.ClickThrough && window.IsHitTestVisible && (DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex)&DesktopNative.Transparent)==0,"The tray recovery command restores mouse handling without interacting with the widget");
          trayVisibility.PerformClick();check(!window.IsVisible,"The tray visibility command hides the widget");
          trayVisibility.PerformClick();check(window.IsVisible && desktopSurface.IsAttached,"The tray visibility command restores the desktop layer");
        }finally{trayVisibility=null;trayClickThrough=null;}
      }
      config.PositionLocked=wasLocked;desktopSurface.UpdateLock();SavePosition();
      bool registered=RegisterHotKey(handle,ToggleHotkey,0x4003,0x48);
      if(registered) {
        try {check(!RegisterHotKey(new WindowInteropHelper(dialog).Handle,ToggleHotkey,0x4003,0x48),"The toggle shortcut registers globally and a conflicting registration is rejected");}
        finally {UnregisterHotKey(handle,ToggleHotkey);}
      }else check(!globalToggleKey,"A shortcut already reserved by another program leaves tray recovery available");
    }
  }
}
