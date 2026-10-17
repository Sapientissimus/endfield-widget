using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EndfieldWidget {
  partial class Widget {
    AppearanceHistory appearanceHistory;bool applyingAppearance;Action syncAppearanceSettings;
    void InitializeAppearanceHistory(){appearanceHistory=new AppearanceHistory();appearanceHistory.Reset(AppearanceState.Capture(config));}
    void TrackAppearanceChange(){if(appearanceHistory==null || applyingAppearance)return;appearanceHistory.Observe(AppearanceState.Capture(config),DateTimeOffset.UtcNow);if(syncUndoButton!=null)syncUndoButton();}
    Action syncUndoButton;
    void ApplyAppearanceState(AppearanceState state,bool undo) {
      applyingAppearance=true;
      try {
        state.Apply(config);ApplyDesignPreset();ApplyTextScale();ApplyArtworkAppearance();ApplyPortraitCrop();ApplyHoverOpacity();UpdateSanityStyle();ApplyLayout(true);Paint();
        if(syncAppearanceSettings!=null)syncAppearanceSettings();if(syncModeBlocks!=null)syncModeBlocks();
      }finally{applyingAppearance=false;}
      if(undo)appearanceHistory.Reset(AppearanceState.Capture(config));else {appearanceHistory.EndGesture();appearanceHistory.Observe(AppearanceState.Capture(config),DateTimeOffset.UtcNow);}
      SavePosition();if(syncUndoButton!=null)syncUndoButton();
    }
    bool UndoAppearance(){if(appearanceHistory==null)return false;var state=appearanceHistory.Take();if(state==null)return false;ApplyAppearanceState(state,true);return true;}
    bool RestorePreviousAppearance(){var saved=Storage.ReadBackup();if(saved==null)return false;saved.NormalizeScales();saved.NormalizePreferences();saved.NormalizeStudio();ApplyAppearanceState(AppearanceState.Capture(saved),false);return true;}
    static bool Within<T>(object source) where T:DependencyObject {
      var item=source as DependencyObject;
      while(item!=null){if(item is T)return true;var content=item as FrameworkContentElement;item=content!=null?content.Parent:VisualTreeHelper.GetParent(item);}
      return false;
    }
    void WireContinuitySettings(Window dialog) {
      var appearance=(StackPanel)((Slider)dialog.FindName("OpacitySlider")).Parent;
      var actions=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,13,0,0)};
      var undo=RegisterControl(dialog,"UndoAppearanceButton",new Button{Content=L.T("Undo change"),Padding=new Thickness(11,8,11,8),FontSize=11,ToolTip=L.T("Undo the last appearance change · Ctrl+Z")});
      var restore=RegisterControl(dialog,"RestoreAppearanceButton",new Button{Content=L.T("Previous appearance"),Padding=new Thickness(11,8,11,8),FontSize=11,Margin=new Thickness(8,0,0,0),ToolTip=L.T("Restore appearance from backup")});
      actions.Children.Add(undo);actions.Children.Add(restore);appearance.Children.Insert(1,actions);
      var feedback=RegisterControl(dialog,"AppearanceFeedback",SettingsLabel(""));feedback.TextWrapping=TextWrapping.Wrap;feedback.FontSize=10;feedback.Margin=new Thickness(0,5,0,0);appearance.Children.Insert(2,feedback);
      settingsBackupAvailable=Storage.ReadBackup()!=null;
      Action buttons=()=>{undo.IsEnabled=appearanceHistory!=null && appearanceHistory.CanUndo;restore.IsEnabled=settingsBackupAvailable;};syncUndoButton=buttons;buttons();
      undo.Click+=(s,e)=>{feedback.Text=UndoAppearance()?L.T("Change undone."):"";buttons();};
      restore.Click+=(s,e)=>{feedback.Text=RestorePreviousAppearance()?L.T("Previous appearance restored."):L.T("Backup unavailable.");buttons();};
      Action sync=()=>{
        string[] sliders={"ScaleSlider","CompactScaleSlider","MicroScaleSlider","OpacitySlider","PortraitOpacitySlider","TextScaleSlider","IdleOpacitySlider"};
        double[] values={config.Scale,config.CompactScale,config.MicroScale,config.BackgroundOpacity,config.PortraitOpacity,config.TextScale,config.IdleOpacity};
        for(int i=0;i<sliders.Length;i++)((Slider)dialog.FindName(sliders[i])).Value=values[i]*100;
        ((CheckBox)dialog.FindName("CompactSwitch")).IsChecked=config.Compact;((CheckBox)dialog.FindName("MicroSwitch")).IsChecked=config.MicroMode;((CheckBox)dialog.FindName("WatermarkSwitch")).IsChecked=config.ShowWatermark;
        ((CheckBox)dialog.FindName("AnimationSwitch")).IsChecked=config.AnimateTransitions;((CheckBox)dialog.FindName("HoverFadeSwitch")).IsChecked=config.FadeWhenIdle;((Slider)dialog.FindName("IdleOpacitySlider")).IsEnabled=config.FadeWhenIdle;
        ((ComboBox)dialog.FindName("PresetSelector")).SelectedIndex=Array.IndexOf(new[]{"soft","strict","minimal"},config.DesignPreset);
        ((ComboBox)dialog.FindName("SanityStyleSelector")).SelectedIndex=Array.IndexOf(new[]{"bar","ring","numbers"},config.SanityStyle);buttons();
      };
      syncAppearanceSettings=sync;
      dialog.PreviewMouseLeftButtonDown+=(s,e)=>{if(appearanceHistory!=null && Within<Slider>(e.OriginalSource))appearanceHistory.BeginGesture();};
      dialog.PreviewMouseLeftButtonUp+=(s,e)=>{if(appearanceHistory!=null)appearanceHistory.EndGesture();FlushSettingsCommit();};
      dialog.PreviewKeyDown+=(s,e)=>{
        if(e.Key==Key.Z && Keyboard.Modifiers==ModifierKeys.Control && !Within<TextBoxBase>(e.OriginalSource) && !Within<PasswordBox>(e.OriginalSource)){e.Handled=true;feedback.Text=UndoAppearance()?L.T("Change undone."):"";}
      };
      var root=(StackPanel)((ScrollViewer)dialog.FindName("SettingsScroll")).Content;
      var card=new Border{CornerRadius=new CornerRadius(11),BorderThickness=new Thickness(1),Padding=new Thickness(16),Margin=new Thickness(0,12,0,3)};card.SetResourceReference(Border.BackgroundProperty,"ThemeCardBrush");card.SetResourceReference(Border.BorderBrushProperty,"ThemeBorderBrush");
      var about=new StackPanel();card.Child=about;root.Children.Add(card);var title=SettingsLabel(L.T("About"));title.FontSize=16;title.FontWeight=FontWeights.SemiBold;about.Children.Add(title);
      var version=RegisterControl(dialog,"BuildVersionLabel",SettingsLabel("Endfield  "+BuildInfo.Version));version.Margin=new Thickness(0,10,0,0);about.Children.Add(version);
      var save=RegisterControl(dialog,"SaveReportButton",new Button{Content=L.T("Save report"),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(11,8,11,8),FontSize=11,Margin=new Thickness(0,10,0,0)});about.Children.Add(save);
      var status=RegisterControl(dialog,"ReportFeedback",SettingsLabel(""));status.FontSize=10;status.TextWrapping=TextWrapping.Wrap;status.Margin=new Thickness(0,6,0,0);about.Children.Add(status);
      save.Click+=(s,e)=>{try{status.Text=ExportDiagnosticReport(dialog)?L.T("Report saved."):"";}catch{status.Text=L.T("Could not save the report. Try another folder.");}};
      dialog.Closed+=(s,e)=>{if(appearanceHistory!=null)appearanceHistory.EndGesture();FlushSettingsCommit();if(syncAppearanceSettings==sync)syncAppearanceSettings=null;if(syncUndoButton==buttons)syncUndoButton=null;};
    }
  }
}
