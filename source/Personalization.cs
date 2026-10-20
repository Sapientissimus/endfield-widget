using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  partial class Widget {
    Forms.ToolStripMenuItem trayVisibility,trayClickThrough;
    bool HasVisibleUpdate(DateTimeOffset now){return config.ShowUpdates && schedule!=null && schedule.IsUpcomingOrInProgress(now);}
    void ToggleWidget(){if(widgetMenu!=null)widgetMenu.IsOpen=false;if(window.IsVisible)window.Hide();else ShowWidget();UpdateMenu();}
    void SetClickThrough(bool value){config.ClickThrough=value;if(value && widgetMenu!=null)widgetMenu.IsOpen=false;if(desktopSurface!=null)desktopSurface.SetClickThrough(value);else window.IsHitTestVisible=!value;UpdateMenu();SavePosition();}
    void BuildTrayPersonalization(Forms.ContextMenuStrip menu) {
      trayVisibility=menu.Items[0] as Forms.ToolStripMenuItem;
      if(trayVisibility!=null) {
        menu.Items.Remove(trayVisibility);trayVisibility.Dispose();
        trayVisibility=new Forms.ToolStripMenuItem("",null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>ToggleWidget()))){Name="TrayVisibility"};menu.Items.Insert(0,trayVisibility);
      }
      trayClickThrough=new Forms.ToolStripMenuItem(L.T("Click through"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>SetClickThrough(!config.ClickThrough)))){Name="TrayClickThrough"};
      menu.Items.Insert(2,trayClickThrough);menu.Items.Insert(3,new Forms.ToolStripSeparator());UpdateTrayPersonalization();
    }
    void UpdateTrayPersonalization() {
      if(trayVisibility!=null){trayVisibility.Text=window.IsVisible?L.T("Hide widget"):L.T("Show widget");trayVisibility.ShortcutKeyDisplayString=globalToggleKey?config.HotkeyToggle:"";}
      if(trayClickThrough!=null)trayClickThrough.Checked=config.ClickThrough;
    }
    Action BindNumber(Window dialog,string name,Func<int> value,Func<int> minimum,Func<int> maximum,Action<int> apply) {
      var input=(TextBox)dialog.FindName(name);
      Action sync=()=>{input.Text=value().ToString(CultureInfo.InvariantCulture);input.SetResourceReference(Control.BorderBrushProperty,"ThemeBorderBrush");input.ToolTip=minimum()+"–"+maximum();};
      Action<bool> commit=keepInvalid=>{
        int parsed;
        if(int.TryParse(input.Text.Trim().TrimEnd('%').Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out parsed) && parsed>=minimum() && parsed<=maximum()){apply(parsed);sync();}
        else if(keepInvalid){input.BorderBrush=Brushes.IndianRed;input.ToolTip=L.T("Enter a number from ")+minimum()+L.T(" to ")+maximum()+".";}
        else sync();
      };
      sync();input.GotKeyboardFocus+=(s,e)=>input.SelectAll();
      input.PreviewMouseLeftButtonDown+=(s,e)=>{if(!input.IsKeyboardFocusWithin){e.Handled=true;input.Focus();input.SelectAll();}};
      input.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;commit(true);}};
      input.LostKeyboardFocus+=(s,e)=>commit(false);
      return sync;
    }
    void BindScaleNumber(Window dialog,string sliderName,string inputName,string resetName) {
      var slider=(Slider)dialog.FindName(sliderName);
      var sync=BindNumber(dialog,inputName,()=> (int)Math.Round(slider.Value),()=>75,()=>130,n=>slider.Value=n);
      slider.ValueChanged+=(s,e)=>sync();
      ((Button)dialog.FindName(resetName)).Click+=(s,e)=>{slider.Value=100;sync();};
    }
    void WirePersonalization(Window dialog) {
      Func<string,object> find=name=>dialog.FindName(name);
      BindScaleNumber(dialog,"ScaleSlider","ScaleInput","ScaleReset");
      BindScaleNumber(dialog,"CompactScaleSlider","CompactScaleInput","CompactScaleReset");
      Action<string,bool,Action<bool>> toggle=(name,initial,apply)=>{var control=(CheckBox)find(name);control.IsChecked=initial;control.Click+=(s,e)=>apply(control.IsChecked==true);};
      WireModeBlocks(dialog);
      toggle("ClickThroughSwitch",config.ClickThrough,v=>SetClickThrough(v));
      ((TextBlock)find("ToggleHotkeyLabel")).Text=string.IsNullOrEmpty(config.HotkeyToggle)?L.T("Disabled"):config.HotkeyToggle+(preview || Storage.Isolated || globalToggleKey?"":L.T(" · unavailable"));
      var mode=(ComboBox)find("SanityNoticeMode");mode.Items.Add(L.T("Sanity percentage"));mode.Items.Add(L.T("Until full recovery"));mode.SelectedIndex=config.SanityNoticeMode=="minutes"?1:0;
      Action unit=()=>{((TextBlock)find("SanityNoticeUnit")).Text=config.SanityNoticeMode=="minutes"?Clock.Unit(config.SanityLeadMinutes,L.T("duration.minute.accusative"),L.T("duration.minute.few"),L.T("duration.minute.many")):"%";((TextBlock)find("MaintenanceNoticeUnit")).Text=Clock.Unit(config.MaintenanceLeadMinutes,L.T("duration.minute.accusative"),L.T("duration.minute.few"),L.T("duration.minute.many"));};
      var syncSanity=BindNumber(dialog,"SanityNoticeValue",()=>config.SanityNoticeMode=="minutes"?config.SanityLeadMinutes:config.SanityThreshold,()=>config.SanityNoticeMode=="minutes"?5:50,()=>config.SanityNoticeMode=="minutes"?240:100,n=>{if(config.SanityNoticeMode=="minutes")config.SanityLeadMinutes=n;else config.SanityThreshold=n;unit();nextNotificationCheck=DateTimeOffset.MinValue;SavePosition();});
      unit();mode.SelectionChanged+=(s,e)=>{config.SanityNoticeMode=mode.SelectedIndex==1?"minutes":"percent";syncSanity();unit();nextNotificationCheck=DateTimeOffset.MinValue;SavePosition();};
      BindNumber(dialog,"MaintenanceNoticeValue",()=>config.MaintenanceLeadMinutes,()=>5,()=>180,n=>{config.MaintenanceLeadMinutes=n;unit();nextNotificationCheck=DateTimeOffset.MinValue;SavePosition();});
      var sanitySwitch=(CheckBox)find("SanityNotifySwitch");var maintenanceSwitch=(CheckBox)find("MaintenanceNotifySwitch");
      Action noticeRows=()=>{((FrameworkElement)find("SanityNoticeOptions")).Visibility=sanitySwitch.IsChecked==true?Visibility.Visible:Visibility.Collapsed;((FrameworkElement)find("MaintenanceNoticeOptions")).Visibility=maintenanceSwitch.IsChecked==true?Visibility.Visible:Visibility.Collapsed;};
      sanitySwitch.Checked+=(s,e)=>noticeRows();sanitySwitch.Unchecked+=(s,e)=>noticeRows();maintenanceSwitch.Checked+=(s,e)=>noticeRows();maintenanceSwitch.Unchecked+=(s,e)=>noticeRows();noticeRows();
    }
  }
}
