using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EndfieldWidget {
  partial class Widget {
    T RegisterControl<T>(Window dialog,string name,T control) where T:FrameworkElement {control.Name=name;dialog.RegisterName(name,control);return control;}
    TextBlock SettingsLabel(string text){var label=new TextBlock{Text=text,FontSize=12,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.ForegroundProperty,"ThemeTextBrush");return label;}
    StackPanel SettingsSection(StackPanel parent,string title,bool expanded=false) {
      var content=new StackPanel {Margin=new Thickness(0,10,0,0)};
      var header=SettingsLabel(title);header.SetResourceReference(TextBlock.ForegroundProperty,"ThemeSoftAccentBrush");
      var expander=new Expander{Header=header,Content=content,IsExpanded=expanded,Margin=new Thickness(0,13,0,0)};expander.SetResourceReference(Control.ForegroundProperty,"ThemeTextBrush");parent.Children.Add(expander);return content;
    }
    CheckBox StudioSwitch(Window dialog,StackPanel parent,string name,string title,bool initial,Action<bool> changed) {
      var row=new Grid{Margin=new Thickness(0,8,0,4),MinHeight=24};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var label=SettingsLabel(title);label.TextWrapping=TextWrapping.Wrap;label.Margin=new Thickness(0,0,14,0);row.Children.Add(label);
      var control=RegisterControl(dialog,name,new CheckBox{IsChecked=initial,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center,Style=(Style)dialog.Resources["SettingsSwitch"]});
      Grid.SetColumn(control,1);System.Windows.Automation.AutomationProperties.SetName(control,title);control.Click+=(s,e)=>changed(control.IsChecked==true);row.Children.Add(control);parent.Children.Add(row);return control;
    }
    Slider StudioSlider(Window dialog,StackPanel parent,string name,string title,double value,double min,double max,Action<double> changed,string unit="%") {
      var row=new Grid{Margin=new Thickness(0,10,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var label=SettingsLabel(title);label.TextWrapping=TextWrapping.Wrap;label.Margin=new Thickness(0,0,14,0);row.Children.Add(label);var text=SettingsLabel(Math.Round(value)+unit);Grid.SetColumn(text,1);text.SetResourceReference(TextBlock.ForegroundProperty,"ThemeAccentBrush");row.Children.Add(text);parent.Children.Add(row);
      var slider=RegisterControl(dialog,name,new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,SmallChange=1,LargeChange=10});System.Windows.Automation.AutomationProperties.SetName(slider,title);
      slider.ValueChanged+=(s,e)=>{text.Text=Math.Round(slider.Value)+unit;changed(slider.Value);};parent.Children.Add(slider);return slider;
    }
    ComboBox StudioChoice(Window dialog,StackPanel parent,string name,string title,string[] choices,int selected,Action<int> changed) {
      var label=SettingsLabel(title);label.Margin=new Thickness(0,10,0,6);parent.Children.Add(label);
      var combo=RegisterControl(dialog,name,new ComboBox{Height=33,FontSize=11});foreach(string choice in choices)combo.Items.Add(choice);combo.SelectedIndex=Math.Max(0,selected);
      System.Windows.Automation.AutomationProperties.SetName(combo,title);combo.SelectionChanged+=(s,e)=>{if(combo.SelectedIndex>=0)changed(combo.SelectedIndex);};parent.Children.Add(combo);return combo;
    }
    void WireStudioSettings(Window dialog) {
      var appearance=(StackPanel)dialog.FindName("AppearanceSettingsPanel");
      var extra=SettingsSection(appearance,L.T("Modes and appearance"));
      var compact=(CheckBox)dialog.FindName("CompactSwitch");CheckBox micro=null;
      micro=StudioSwitch(dialog,(StackPanel)dialog.FindName("LayoutModesPanel"),"MicroSwitch",L.T("Mini mode"),config.MicroMode,v=>{SetWidgetMode(v?"micro":"full");compact.IsChecked=config.Compact;});
      compact.Click+=(s,e)=>micro.IsChecked=config.MicroMode;
      StudioSlider(dialog,(StackPanel)dialog.FindName("MiniScalePanel"),"MicroScaleSlider",L.T("Mini mode scale"),config.MicroScale*100,75,130,v=>{config.MicroScale=v/100;if(config.MicroMode)ApplyLayout(true);SavePosition();});
      string[] presets={"soft","strict","minimal"};StudioChoice(dialog,extra,"PresetSelector",L.T("Style"),new[]{L.T("Soft"),L.T("Strict"),L.T("Minimal")},Array.IndexOf(presets,config.DesignPreset),i=>{config.DesignPreset=presets[i];ApplyDesignPreset();ApplyLayout(true);SavePosition();});
      string[] indicators={"bar","ring","numbers"};StudioChoice(dialog,extra,"SanityStyleSelector",L.T("Sanity indicator"),new[]{L.T("Bar"),L.T("Ring"),L.T("Numbers only")},Array.IndexOf(indicators,config.SanityStyle),i=>{config.SanityStyle=indicators[i];UpdateSanityStyle();ApplyLayout(false);SavePosition();});
      StudioSlider(dialog,extra,"TextScaleSlider",L.T("Text size"),config.TextScale*100,85,120,v=>{config.TextScale=v/100;ApplyTextScale();ApplyLayout(true);SavePosition();});
      Slider idle=null;StudioSwitch(dialog,extra,"HoverFadeSwitch",L.T("Dim when not hovered"),config.FadeWhenIdle,v=>{config.FadeWhenIdle=v;if(idle!=null)idle.IsEnabled=v;ApplyHoverOpacity();SavePosition();});
      idle=StudioSlider(dialog,extra,"IdleOpacitySlider",L.T("Idle opacity"),config.IdleOpacity*100,30,100,v=>{config.IdleOpacity=v/100;ApplyHoverOpacity();SavePosition();});idle.IsEnabled=config.FadeWhenIdle;
      var framing=SettingsSection(appearance,L.T("Operator framing"));
      foreach(string name in new[]{"PortraitOpacityRow","PortraitOpacitySlider","WatermarkSettingRow"}){var control=(FrameworkElement)dialog.FindName(name);appearance.Children.Remove(control);framing.Children.Add(control);}
      WireCropSettings(dialog,framing);
      var game=(StackPanel)dialog.FindName("GameSettingsPanel");
      StudioSwitch(dialog,game,"SessionTimeSwitch",L.T("Time in game"),config.ShowSessionTime,v=>{config.ShowSessionTime=v;PaintSession(DateTimeOffset.UtcNow);SavePosition();});
      var calendar=RegisterControl(dialog,"OpenCalendarButton",new Button{Content=L.T("Open calendar"),FontSize=11,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,10,0,0),HorizontalAlignment=HorizontalAlignment.Left});calendar.Click+=(s,e)=>ShowCalendar();game.Children.Add(calendar);
      var placement=(StackPanel)dialog.FindName("DesktopSettingsPanel");
      WirePlacementSettings(dialog,SettingsSection(placement,L.T("Precise placement")));WireShortcutSettings(dialog,SettingsSection(placement,L.T("Keyboard shortcuts")));
    }
    void WireCropSettings(Window dialog,StackPanel parent) {
      var operatorName=RegisterControl(dialog,"CropOperatorName",SettingsLabel(Find<TextBlock>("OperatorName").Text));operatorName.FontWeight=FontWeights.SemiBold;operatorName.Margin=new Thickness(0,12,0,0);parent.Children.Add(operatorName);
      bool compact=false,updating=false;Slider zoom=null,x=null,y=null;
      Action sync=()=>{
        if(updating || string.IsNullOrEmpty(currentArtKey))return;updating=true;
        try{operatorName.Text=Find<TextBlock>("OperatorName").Text;var frame=config.Frame(currentArtKey,compact);if(zoom!=null)zoom.Value=frame.Zoom*100;if(x!=null)x.Value=frame.X;if(y!=null)y.Value=frame.Y;}finally{updating=false;}
      };
      StudioChoice(dialog,parent,"CropModeSelector",L.T("Mode"),new[]{L.T("Normal"),L.T("Compact")},0,i=>{compact=i==1;sync();});
      Action<double,int> change=(value,axis)=>{if(updating || string.IsNullOrEmpty(currentArtKey))return;var frame=config.Frame(currentArtKey,compact);if(axis==0)frame.Zoom=value/100;else if(axis==1)frame.X=value;else frame.Y=value;ApplyPortraitCrop();SavePosition();};
      zoom=StudioSlider(dialog,parent,"CropZoomSlider",L.T("Zoom"),100,60,160,v=>change(v,0));x=StudioSlider(dialog,parent,"CropXSlider",L.T("Horizontal"),0,-100,100,v=>change(v,1),"");y=StudioSlider(dialog,parent,"CropYSlider",L.T("Vertical"),0,-100,100,v=>change(v,2),"");
      var reset=RegisterControl(dialog,"CropResetButton",new Button{Content=L.T("Default"),HorizontalAlignment=HorizontalAlignment.Left,FontSize=11,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,10,0,0)});
      reset.Click+=(s,e)=>{if(string.IsNullOrEmpty(currentArtKey))return;var frame=config.Frame(currentArtKey,compact);frame.Zoom=1;frame.X=frame.Y=0;ApplyPortraitCrop();SavePosition();};parent.Children.Add(reset);
      syncCropSettings=sync;sync();dialog.Closed+=(s,e)=>{if(syncCropSettings==sync)syncCropSettings=null;};
    }
    void WirePlacementSettings(Window dialog,StackPanel parent) {
      var displays=CurrentDisplays().ToList();int selected=displays.FindIndex(d=>d.Id==config.MonitorId);
      var monitor=StudioChoice(dialog,parent,"MonitorSelector",L.T("Display"),displays.Select((d,i)=>L.T("Display ")+(i+1)+(d.Primary?L.T(" · Primary"):"")+" · "+(int)d.Work.Width+"×"+(int)d.Work.Height).ToArray(),Math.Max(0,selected),i=>{});
      string[] anchors={"free","tl","tr","bl","br"};var anchor=StudioChoice(dialog,parent,"AnchorSelector",L.T("Position"),new[]{L.T("Free"),L.T("Top left"),L.T("Top right"),L.T("Bottom left"),L.T("Bottom right")},Array.IndexOf(anchors,config.PositionAnchor),i=>{});
      var row=new Grid{Margin=new Thickness(0,13,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition());
      var horizontal=new StackPanel();horizontal.Children.Add(SettingsLabel(L.T("X margin, pixels")));var vertical=new StackPanel{Margin=new Thickness(12,0,0,0)};vertical.Children.Add(SettingsLabel(L.T("Y margin, pixels")));Grid.SetColumn(vertical,1);
      var x=RegisterControl(dialog,"MarginXInput",new TextBox{Text=config.EdgeMarginX.ToString(),Height=31,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,6,0,0),FontSize=12});var y=RegisterControl(dialog,"MarginYInput",new TextBox{Text=config.EdgeMarginY.ToString(),Height=31,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,6,0,0),FontSize=12});
      foreach(var input in new[]{x,y}){input.SetResourceReference(Control.BackgroundProperty,"ThemeInputBrush");input.SetResourceReference(Control.ForegroundProperty,"ThemeTextBrush");input.SetResourceReference(Control.BorderBrushProperty,"ThemeBorderBrush");}
      horizontal.Children.Add(x);vertical.Children.Add(y);row.Children.Add(horizontal);row.Children.Add(vertical);parent.Children.Add(row);
      var feedback=RegisterControl(dialog,"PlacementFeedback",SettingsLabel(""));feedback.TextWrapping=TextWrapping.Wrap;feedback.FontSize=10;feedback.Margin=new Thickness(0,7,0,0);parent.Children.Add(feedback);
      var apply=RegisterControl(dialog,"ApplyPlacementButton",new Button{Content=L.T("Apply placement"),FontSize=11,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left});parent.Children.Add(apply);
      apply.Click+=(s,e)=>{int mx,my;if(!int.TryParse(x.Text,out mx) || !int.TryParse(y.Text,out my) || mx<0 || mx>300 || my<0 || my>300){feedback.Text=L.T("Enter margins from 0 to 300 pixels.");return;}if(monitor.SelectedIndex<0 || monitor.SelectedIndex>=displays.Count)return;config.MonitorId=displays[monitor.SelectedIndex].Id;config.PositionAnchor=anchors[anchor.SelectedIndex];config.EdgeMarginX=mx;config.EdgeMarginY=my;ApplyLayout(true);SavePosition();feedback.Text=L.T("Placement saved.");};
    }
    void WireShortcutSettings(Window dialog,StackPanel parent) {
      string[] labels={L.T("Show / hide"),L.T("Widget menu"),L.T("Launch / open game"),L.T("Switch mode"),L.T("Refresh data")};string[] values=ShortcutValues();var inputs=new List<TextBox>();
      for(int i=0;i<labels.Length;i++) {
        var label=SettingsLabel(labels[i]);label.Margin=new Thickness(0,9,0,5);parent.Children.Add(label);
        var input=RegisterControl(dialog,"ShortcutInput"+i,new TextBox{Text=string.IsNullOrEmpty(values[i])?L.T("Disabled"):values[i],IsReadOnly=true,Height=32,FontSize=11,Padding=new Thickness(8,5,8,5),ToolTip=L.T("Press a shortcut. Backspace disables it.")});
        input.SetResourceReference(Control.BackgroundProperty,"ThemeInputBrush");input.SetResourceReference(Control.ForegroundProperty,"ThemeTextBrush");input.SetResourceReference(Control.BorderBrushProperty,"ThemeBorderBrush");System.Windows.Automation.AutomationProperties.SetName(input,labels[i]);
        string previous=input.Text;input.GotKeyboardFocus+=(s,e)=>previous=input.Text;
        input.PreviewKeyDown+=(s,e)=>{
          if(e.Key==Key.Tab)return;e.Handled=true;Key key=e.Key==Key.System?e.SystemKey:e.Key;
          if(key==Key.Escape){input.Text=previous;return;}if(key==Key.Back || key==Key.Delete){input.Text=L.T("Disabled");return;}
          if(key==Key.LeftCtrl || key==Key.RightCtrl || key==Key.LeftAlt || key==Key.RightAlt || key==Key.LeftShift || key==Key.RightShift || key==Key.LWin || key==Key.RWin)return;
          try{var gesture=new KeyGesture(key,Keyboard.Modifiers);string text=gesture.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture);ShortcutSpec parsed;if(ShortcutSpec.TryCreate(0,text,out parsed))input.Text=parsed.Text;}catch{}
        };
        inputs.Add(input);parent.Children.Add(input);
      }
      var feedback=RegisterControl(dialog,"ShortcutFeedback",SettingsLabel(UnavailableShortcuts()));feedback.FontSize=10;feedback.TextWrapping=TextWrapping.Wrap;feedback.Margin=new Thickness(0,9,0,0);parent.Children.Add(feedback);
      var apply=RegisterControl(dialog,"ApplyShortcutsButton",new Button{Content=L.T("Save shortcuts"),FontSize=11,HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,9,0,0)});parent.Children.Add(apply);
      apply.Click+=(s,e)=>{string message;SaveShortcuts(inputs.Select(c=>c.Text==L.T("Disabled")?"":c.Text).ToArray(),out message);feedback.Text=message;((TextBlock)dialog.FindName("ToggleHotkeyLabel")).Text=string.IsNullOrEmpty(config.HotkeyToggle)?L.T("Disabled"):config.HotkeyToggle;};
    }
  }
}
