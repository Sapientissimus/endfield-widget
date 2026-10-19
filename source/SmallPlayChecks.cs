using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    void CheckVisibleWidgetControl(Action<bool,string> check,string view,string name,string label) {
      var button=Find<Button>(name);
      check(button.IsVisible && button.Opacity==1 && button.IsHitTestVisible,"The control remains visible and interactive without hover: "+name+", "+label);
      check(ReadingContains(view,name),"The control stays inside its layout: "+name+", "+label);
      Point center=button.TranslatePoint(new Point(button.ActualWidth/2,button.ActualHeight/2),window);
      DependencyObject hit=window.InputHitTest(center) as DependencyObject;while(hit!=null && hit!=button)hit=VisualTreeHelper.GetParent(hit);
      check(hit==button,"A pointer hit at the control center reaches its actual button: "+name+", "+label);
    }
    async Task CheckSmallPlayButtons(Action<bool,string> check,string folder) {
      string original=Json.Encode(config);var originalLauncher=gameLauncher;var originalProbe=gameProbe;var originalActivity=gameActivity;
      try {
        gameProbe=()=>false;gameActivity=new GameActivity();int launches=0;gameLauncher=()=>{launches++;return Task.FromResult(0);};UpdateGameLaunchControls();
        foreach(string mode in new[]{"full","compact","micro"}) {
          string buttonName=mode=="full"?"PlayButton":mode=="compact"?"CompactPlayButton":"MicroPlayButton",view=mode=="full"?"FullView":mode=="compact"?"CompactView":"MicroView";
          string[] utilities=mode=="full"?new[]{"FullCalendarButton","RefreshButton","SettingsButton"}:mode=="compact"?new[]{"CompactCalendarButton","CompactRefreshButton","CompactSettingsButton"}:new[]{"MicroSettingsButton"};
          config.Compact=mode=="compact";config.MicroMode=mode=="micro";config.ShowPlayButton=true;
          foreach(double scale in new[]{.75,1.3})foreach(double text in new[]{.85,1.2}) {
            config.Scale=config.CompactScale=config.MicroScale=scale;config.TextScale=text;ApplyTextScale();Paint();ApplyLayout(true);Keyboard.ClearFocus();window.Left=window.Top=-10000;
            await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);window.UpdateLayout();
            string label=mode+", scale "+scale+", text "+text;
            check(!window.IsMouseOver && !window.IsKeyboardFocusWithin,"Visibility checks run with the pointer and keyboard focus away: "+label);
            CheckVisibleWidgetControl(check,view,buttonName,label);
            foreach(string utility in utilities)CheckVisibleWidgetControl(check,view,utility,label);
            if(mode!="micro") {
              var close=Find<Button>(mode=="full"?"CloseButton":"CompactCloseButton");
              check(close.Opacity==0 && !close.IsHitTestVisible,"Only the close control remains hidden and cannot intercept idle clicks: "+label);
            }
            check(!window.HasAnimatedProperties && !Find<Image>("OperatorImage").HasAnimatedProperties,"Layout and operator artwork have no animation clocks: "+label);
          }
          config.ShowPlayButton=false;ApplyLayout(true);window.UpdateLayout();
          check(Find<Button>(buttonName).Visibility==Visibility.Collapsed,"An explicit per-mode hidden-play preference is still respected: "+mode);
          config.ShowPlayButton=true;config.Scale=config.CompactScale=config.MicroScale=1;config.TextScale=1;ApplyTextScale();Paint();ApplyLayout(true);window.Left=window.Top=-10000;window.UpdateLayout();
          SaveImage(window,Path.Combine(folder,"play-visible-"+mode+".png"));
          int before=launches;Find<Button>(buttonName).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await gameLaunchTask;
          check(launches==before+1,"The visible play control dispatches the shared launch action exactly once: "+mode);
        }
        foreach(string mode in new[]{"full","micro","compact","full"}) {
          SetWidgetMode(mode);window.UpdateLayout();
          check(WidgetMode()==mode && window.Width>0 && window.Height>0 && !window.HasAnimatedProperties,"Rapid mode changes apply their final state immediately without animation clocks: "+mode);
        }
        Keyboard.ClearFocus();window.Left=window.Top=-10000;await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        config.FadeWhenIdle=true;config.IdleOpacity=.5;ApplyHoverOpacity();check(window.Opacity==.5 && !window.HasAnimatedProperties,"Idle dimming applies the selected opacity immediately without a fade animation");
        config.FadeWhenIdle=false;ApplyHoverOpacity();check(window.Opacity==1 && !window.HasAnimatedProperties,"Disabling idle dimming immediately restores full opacity");
      } finally {
        var saved=new JavaScriptSerializer().Deserialize<Config>(original);foreach(var property in typeof(Config).GetProperties())if(property.CanWrite)property.SetValue(config,property.GetValue(saved,null),null);
        gameLauncher=originalLauncher;gameProbe=originalProbe;gameActivity=originalActivity;
        ApplyTextScale();Paint();ApplyLayout(true);UpdateGameLaunchControls();SavePosition();
      }
    }
  }
}
