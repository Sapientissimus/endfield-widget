using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace EndfieldWidget {
  partial class Widget {
    Window settingsDialog;
    Action refreshAccountSettings;
    void SynchronizeAccountSettings(){if(refreshAccountSettings!=null)refreshAccountSettings();}
    Window SettingsWindow() {
      Window dialog;
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("SettingsWindow.xaml"))dialog=(Window)XamlReader.Load(stream);
      dialog.Resources.MergedDictionaries.Add(window.Resources);
      dialog.Language=XmlLanguage.GetLanguage(L.Culture.Name);
      dialog.Height=Math.Min(dialog.Height,SystemParameters.WorkArea.Height-40);
      // Keep every settings entry point independent of the Explorer owner chain,
      // including startup before desktop attachment and temporary shell recovery.
      dialog.Owner=null;dialog.WindowStartupLocation=WindowStartupLocation.CenterScreen;
      DesktopNative.HideFromSwitcher(dialog,false);
      if(diagnosticView)dialog.Loaded+=(s,e)=>{dialog.Title="Endfield settings · Display test";IntPtr handle=new System.Windows.Interop.WindowInteropHelper(dialog).Handle;long style=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);DesktopNative.SetStyle(handle,DesktopNative.ExtendedStyleIndex,(style|DesktopNative.AppWindow)&~DesktopNative.ToolWindow);};
      dialog.Loaded+=(s,e)=>{
        var scroll=(ScrollViewer)dialog.FindName("SettingsScroll");scroll.ApplyTemplate();
        var bar=scroll.Template.FindName("PART_VerticalScrollBar",scroll) as ScrollBar;
        if(bar!=null){bar.Style=(Style)dialog.Resources[typeof(ScrollBar)];bar.Width=6;bar.MinWidth=6;bar.MaxWidth=6;bar.ClipToBounds=true;}
      };
      Func<string,object> find=name=>dialog.FindName(name);
      var region=(ComboBox)find("RegionSelector");var artStatus=(TextBlock)find("BannerCheckStatus");
      var badge=(TextBlock)find("AccountConnectionBadge");var password=(PasswordBox)find("TokenInput");
      var hint=(TextBlock)find("TokenHint");var connect=(Button)find("ConnectAccountButton");
      var disconnect=(Button)find("DisconnectAccountButton");var roleBox=(ComboBox)find("CharacterSelector");
      var characterRow=(StackPanel)find("CharacterRow");var message=(TextBlock)find("ConnectionMessage");
      var locked=(CheckBox)find("PositionLockSwitch");bool updatingRoles=false;
      var compact=(CheckBox)find("CompactSwitch");var startupSwitch=(CheckBox)find("StartupSwitch");
      var sanityNotify=(CheckBox)find("SanityNotifySwitch");var maintenanceNotify=(CheckBox)find("MaintenanceNotifySwitch");var quietSwitch=(CheckBox)find("FullscreenQuietSwitch");
      var scale=(Slider)find("ScaleSlider");var compactScale=(Slider)find("CompactScaleSlider");var opacity=(Slider)find("OpacitySlider");
      var feedback=(TextBlock)find("SettingsFeedback");
      compact.IsChecked=config.Compact;sanityNotify.IsChecked=config.NotifySanity;maintenanceNotify.IsChecked=config.NotifyMaintenance;quietSwitch.IsChecked=config.PauseInFullscreen;
      scale.Value=config.Scale*100;compactScale.Value=config.CompactScale*100;opacity.Value=config.BackgroundOpacity*100;
      ((TextBlock)find("OpacityValue")).Text=Math.Round(opacity.Value)+"%";
      try{startupSwitch.IsChecked=startup.Enabled;}catch{startupSwitch.IsEnabled=false;}
      compact.Click+=(s,e)=>SetWidgetMode(compact.IsChecked==true?"compact":"full");
      scale.ValueChanged+=(s,e)=>{config.Scale=scale.Value/100;if(!config.Compact)ApplyLayout(true);SavePosition();};
      compactScale.ValueChanged+=(s,e)=>{config.CompactScale=compactScale.Value/100;if(config.Compact)ApplyLayout(true);SavePosition();};
      opacity.ValueChanged+=(s,e)=>{config.BackgroundOpacity=opacity.Value/100;((TextBlock)find("OpacityValue")).Text=Math.Round(opacity.Value)+"%";ApplyBackgroundOpacity();SavePosition();};
      startupSwitch.Click+=(s,e)=>{
        try{startup.Set(startupSwitch.IsChecked==true);feedback.Visibility=Visibility.Collapsed;}
        catch(Exception error){try{startupSwitch.IsChecked=startup.Enabled;}catch{startupSwitch.IsChecked=false;}feedback.Text=error is InvalidOperationException?error.Message:L.T("Could not change autostart.");feedback.Visibility=Visibility.Visible;}
      };
      sanityNotify.Click+=(s,e)=>{config.NotifySanity=sanityNotify.IsChecked==true;nextNotificationCheck=DateTimeOffset.MinValue;SavePosition();};
      maintenanceNotify.Click+=(s,e)=>{config.NotifyMaintenance=maintenanceNotify.IsChecked==true;nextNotificationCheck=DateTimeOffset.MinValue;SavePosition();};
      quietSwitch.Click+=(s,e)=>{config.PauseInFullscreen=quietSwitch.IsChecked==true;SavePosition();};
      WirePersonalization(dialog);
      WireArtworkAppearance(dialog);
      WireStudioSettings(dialog);
      WireContinuitySettings(dialog);
      WireLanguageSettings(dialog);
      WireGameLaunchSettings(dialog);
      dialog.PreviewKeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;dialog.Close();}};
      Action<string> setMessage=text=>{message.Text=text;message.Visibility=text.Length>0?Visibility.Visible:Visibility.Collapsed;};
      Action updateAccount=()=>{
        bool connected=accountToken.Length>0;
        badge.Text=connected?L.T("Account saved"):L.T("Not connected");
        disconnect.Visibility=connected?Visibility.Visible:Visibility.Collapsed;
        connect.Content=connected?L.T("Update connection"):L.T("Connect account");
        hint.Text="";hint.Visibility=Visibility.Collapsed;
        characterRow.Visibility=connected && availableRoles!=null && availableRoles.Count>0?Visibility.Visible:Visibility.Collapsed;
      };
      Action populateRoles=()=>{
        updatingRoles=true;roleBox.Items.Clear();
        if(availableRoles!=null && availableRoles.Count>0){foreach(var role in availableRoles)roleBox.Items.Add(role);roleBox.SelectedItem=availableRoles.FirstOrDefault(r=>r.RoleId==config.RoleId)??availableRoles[0];}
        updatingRoles=false;updateAccount();
      };
      Action synchronize=()=>{if(dialog.IsVisible)populateRoles();};refreshAccountSettings=synchronize;
      dialog.Closed+=(s,e)=>{if(ReferenceEquals(refreshAccountSettings,synchronize))refreshAccountSettings=null;};
      region.Items.Add(L.T("Europe / Americas"));region.Items.Add(L.T("Asia"));region.SelectedIndex=config.BannerRegion=="Asia"?1:0;
      Action updateBannerStatus=()=>{artStatus.Text=bannerError.Length>0?L.T("Could not update the artwork. Try later."):"";artStatus.Visibility=artStatus.Text.Length>0?Visibility.Visible:Visibility.Collapsed;};
      updateBannerStatus();
      populateRoles();locked.IsChecked=config.PositionLocked;
      ((Button)find("SettingsCloseButton")).Click+=(s,e)=>dialog.Close();
      ((Button)find("SettingsDoneButton")).Click+=(s,e)=>dialog.Close();
      ((Button)find("OpenSkportButton")).Click+=(s,e)=>Open("https://www.skport.com/");
      ((Button)find("OpenTokenPageButton")).Click+=(s,e)=>Open("https://web-api.skport.com/cookie_store/account_token");
      ((Grid)find("SettingsDragHandle")).MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource is TextBlock || e.OriginalSource==s){try{dialog.DragMove();}catch{}}};
      region.SelectionChanged+=async(s,e)=>{
        config.BannerRegion=region.SelectedIndex==1?"Asia":"Europe";SavePosition();Paint();if(calendarDialog!=null)RenderCalendar();
        if(refreshBusy)return;refreshBusy=true;region.IsEnabled=false;
        try{await RefreshBanners();Paint();updateBannerStatus();}
        finally{refreshBusy=false;region.IsEnabled=true;}
      };
      locked.Click+=(s,e)=>{config.PositionLocked=locked.IsChecked==true;if(desktopSurface!=null)desktopSurface.UpdateLock();UpdateMenu();SavePosition();};
      connect.Click+=async(s,e)=>{
        if(refreshBusy){setMessage(L.T("Wait for the current refresh to finish."));return;}
        string candidate=password.Password.Length>0?SkportClient.NormalizeToken(password.Password):accountToken;
        if(candidate.Length==0){setMessage(L.T("Paste your login token."));password.Focus();return;}
        refreshBusy=true;Find<Button>("RefreshButton").IsEnabled=false;connect.IsEnabled=false;roleBox.IsEnabled=false;region.IsEnabled=false;
        int epoch=recovery.Epoch;
        setMessage(L.T("Connecting to SKPORT…"));
        try {
          var client=new SkportClient(http);var roles=await client.Roles(candidate);
          if(!CanApplyResponse(epoch)){setMessage(L.T("SKPORT did not respond in time. Try again."));return;}
          accountToken=candidate;skport=client;availableRoles=roles;config.ProtectedToken=Storage.Protect(candidate);
          populateRoles();config.RoleId=((GameRole)roleBox.SelectedItem).RoleId;SynchronizeAccountSettings();
          Storage.Write("settings.json",config);password.Clear();sanity=null;await RefreshSanity();Paint();updateAccount();
          setMessage(sanityError.Length>0?FriendlyAccountError(sanityError):L.T("Account connected."));
        } catch(Exception ex){setMessage(SkportFailure.Describe(ex));if(!preview)SkportFailure.Record(ex);}
        finally{updatingRoles=false;refreshBusy=false;connect.IsEnabled=true;roleBox.IsEnabled=true;region.IsEnabled=true;Find<Button>("RefreshButton").IsEnabled=true;}
      };
      roleBox.SelectionChanged+=async(s,e)=>{
        var selected=roleBox.SelectedItem as GameRole;if(updatingRoles || selected==null || config.RoleId==selected.RoleId)return;
        if(refreshBusy){populateRoles();setMessage(L.T("Wait for the current refresh to finish."));return;}
        config.RoleId=selected.RoleId;sanity=null;sanityError="";liveSanity=false;SavePosition();refreshBusy=true;roleBox.IsEnabled=false;
        try{await RefreshSanity();Paint();setMessage(FriendlyAccountError(sanityError));}finally{refreshBusy=false;roleBox.IsEnabled=true;}
      };
      disconnect.Click+=(s,e)=>{
        if(refreshBusy){setMessage(L.T("Wait for the current refresh to finish."));return;}
        accountToken="";config.ProtectedToken=null;config.RoleId=null;sanity=null;sanityError="";liveSanity=false;skport=new SkportClient(http);
        availableRoles=null;populateRoles();password.Clear();
        try{Storage.Write("settings.json",config);string p=Path.Combine(Storage.DirectoryPath,"sanity.json");if(File.Exists(p))File.Delete(p);}catch{}
        setMessage(L.T("Account disconnected."));Paint();
      };
      return dialog;
    }
  }
}
