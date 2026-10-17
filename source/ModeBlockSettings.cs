using System;
using System.Windows;
using System.Windows.Controls;

namespace EndfieldWidget {
  partial class Widget {
    Action syncModeBlocks;
    void WireModeBlocks(Window dialog) {
      var panel=(StackPanel)((Expander)dialog.FindName("VisibleBlocks")).Content;
      var selector=RegisterControl(dialog,"BlockModeSelector",new ComboBox{Height=33,FontSize=11,Margin=new Thickness(0,0,0,12)});
      foreach(string title in new[]{L.T("Normal mode"),L.T("Compact mode"),L.T("Mini mode")})selector.Items.Add(title);
      System.Windows.Automation.AutomationProperties.SetName(selector,L.T("Visible blocks for this mode"));panel.Children.Insert(0,selector);
      string[] modes={"full","compact","micro"},names={"ShowPlaySwitch","ShowNextSwitch","ShowUpdateSwitch"};
      CheckBox[] controls=new CheckBox[names.Length];for(int i=0;i<names.Length;i++)controls[i]=(CheckBox)dialog.FindName(names[i]);
      Action sync=()=>{
        if(selector.SelectedIndex<0)return;string mode=modes[selector.SelectedIndex];if(mode==WidgetMode())config.RememberMode();var p=config.Mode(mode);
        controls[0].IsChecked=p.ShowPlayButton;controls[1].IsChecked=p.ShowNextDebut;controls[2].IsChecked=p.ShowUpdates;
        for(int i=1;i<controls.Length;i++)((FrameworkElement)controls[i].Parent).Visibility=mode=="micro"?Visibility.Collapsed:Visibility.Visible;
      };
      for(int i=0;i<controls.Length;i++) {
        int index=i;controls[i].Click+=(s,e)=>{
          string mode=modes[selector.SelectedIndex];var p=config.Mode(mode);bool show=controls[index].IsChecked==true;
          if(index==0)p.ShowPlayButton=show;else if(index==1)p.ShowNextDebut=show;else p.ShowUpdates=show;
          if(mode==WidgetMode()){p.Apply(config);ApplyLayout(false);Paint();}SavePosition();
        };
      }
      selector.SelectedIndex=Array.IndexOf(modes,WidgetMode());selector.SelectionChanged+=(s,e)=>sync();sync();syncModeBlocks=sync;
      dialog.Closed+=(s,e)=>{if(syncModeBlocks==sync)syncModeBlocks=null;};
    }
  }
}
