using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace EndfieldWidget {
  partial class Widget {
    Task gameSettingsTask=Task.FromResult(0);
    void WireGameLaunchSettings(Window dialog) {
      var parent=(StackPanel)dialog.FindName("SettingsSections");
      var content=new StackPanel();
      var card=RegisterControl(dialog,"GameLaunchSettingsCard",new Border{Child=content,CornerRadius=new CornerRadius(11),BorderThickness=new Thickness(1),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12)});
      card.SetResourceReference(Border.BackgroundProperty,"ThemeCardBrush");card.SetResourceReference(Border.BorderBrushProperty,"ThemeBorderBrush");
      var title=SettingsLabel(L.T("Game launch"));title.FontSize=16;title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);
      var status=RegisterControl(dialog,"GameLaunchStatus",SettingsLabel(""));status.FontSize=11;status.TextWrapping=TextWrapping.Wrap;status.Margin=new Thickness(0,8,0,12);status.SetResourceReference(TextBlock.ForegroundProperty,"ThemeSecondaryTextBrush");content.Children.Add(status);
      var pathLabel=SettingsLabel(L.T("Game or launcher path"));pathLabel.FontSize=11;pathLabel.Margin=new Thickness(0,0,0,6);content.Children.Add(pathLabel);
      var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
      var input=RegisterControl(dialog,"GamePathInput",new TextBox{Style=(Style)dialog.Resources["NumberInput"],Width=double.NaN,Height=36,TextAlignment=TextAlignment.Left,Text=config.GameExecutablePath??"",MinWidth=0,MinHeight=36,Padding=new Thickness(9,5,9,5),FontSize=12,VerticalContentAlignment=VerticalAlignment.Center,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden});
      input.SetResourceReference(Control.BackgroundProperty,"ThemeInputBrush");input.SetResourceReference(Control.ForegroundProperty,"ThemeTextBrush");input.SetResourceReference(Control.BorderBrushProperty,"ThemeBorderBrush");AutomationProperties.SetName(input,L.T("Game or launcher path"));row.Children.Add(input);
      var browse=RegisterControl(dialog,"GamePathBrowse",new Button{Content=L.T("Browse…"),FontSize=11,Padding=new Thickness(11,7,11,7),Margin=new Thickness(8,0,0,0),MinHeight=36});Grid.SetColumn(browse,1);row.Children.Add(browse);content.Children.Add(row);
      var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};
      var save=RegisterControl(dialog,"GamePathSave",new Button{Content=L.T("Save path"),FontSize=11,Padding=new Thickness(12,7,12,7),MinHeight=32,Margin=new Thickness(0,0,8,4)});
      var automatic=RegisterControl(dialog,"GamePathAutomatic",new Button{Content=L.T("Use automatic detection"),FontSize=11,Padding=new Thickness(12,7,12,7),MinHeight=32,Margin=new Thickness(0,0,0,4)});
      actions.Children.Add(save);actions.Children.Add(automatic);content.Children.Add(actions);
      var message=RegisterControl(dialog,"GamePathMessage",SettingsLabel(""));message.Visibility=Visibility.Collapsed;message.FontSize=11;message.TextWrapping=TextWrapping.Wrap;message.Margin=new Thickness(0,7,0,0);message.SetResourceReference(TextBlock.ForegroundProperty,"ThemeSoftAccentBrush");content.Children.Add(message);
      parent.Children.Insert(parent.Children.IndexOf((Border)dialog.FindName("AccountSettingsCard"))+1,card);
      int generation=0;bool closed=false,busy=false;dialog.Closed+=(s,e)=>{closed=true;generation++;};
      Func<Task> refresh=async()=>{
        int request=++generation;string path=config.GameExecutablePath??"";status.Text=L.T("Searching for the game…");status.ToolTip=null;
        try {
          var target=await Task.Run(()=>gameTargetResolver(path));if(closed || request!=generation)return;
          status.Text=target==null?L.T("Game not found. Choose its executable or launcher below."):target.Kind==GameLaunchKind.Manual?L.T("Manual path"):target.Kind==GameLaunchKind.Steam?L.T("Automatic: Steam"):L.T("Automatic: launcher");
          status.ToolTip=target==null?null:target.Executable;
        }catch {if(!closed && request==generation)status.Text=L.T("The saved game path is unavailable. Choose another executable in Settings.");}
      };
      Func<Task> apply=async()=>{
        if(busy || closed)return;busy=true;browse.IsEnabled=save.IsEnabled=automatic.IsEnabled=false;input.IsReadOnly=true;
        try {
          string entered=input.Text;var target=string.IsNullOrWhiteSpace(entered)?null:await Task.Run(()=>GameInstallations.Manual(entered));if(closed)return;
          string old=config.GameExecutablePath;config.GameExecutablePath=target==null?"":target.Executable;
          try{FlushSettingsCommit();Storage.Write("settings.json",config);}catch{config.GameExecutablePath=old;throw;}
          input.Text=config.GameExecutablePath;gameLaunchError="";UpdateGameLaunchControls();message.Text="";message.Visibility=Visibility.Collapsed;await refresh();
        }catch(GamePathException){if(!closed){message.Text=L.T("Choose an existing .exe file using its full path.");message.Visibility=Visibility.Visible;}}
        catch{if(!closed){message.Text=L.T("Could not save the game path. Try again.");message.Visibility=Visibility.Visible;}}
        finally{busy=false;if(!closed){browse.IsEnabled=save.IsEnabled=automatic.IsEnabled=true;input.IsReadOnly=false;}}
      };
      save.Click+=(s,e)=>gameSettingsTask=apply();
      input.KeyDown+=(s,e)=>{if(e.Key==Key.Enter){e.Handled=true;gameSettingsTask=apply();}};
      automatic.Click+=(s,e)=>{input.Text="";gameSettingsTask=apply();};
      browse.Click+=(s,e)=>{
        var picker=new OpenFileDialog{Title=L.T("Choose the game or launcher"),Filter=L.T("Executable files (*.exe)|*.exe"),DefaultExt=".exe",CheckFileExists=true,Multiselect=false};
        try{if(!string.IsNullOrWhiteSpace(input.Text)){string directory=Path.GetDirectoryName(input.Text.Trim('"'));if(Directory.Exists(directory))picker.InitialDirectory=directory;}if(picker.ShowDialog(dialog)==true){input.Text=picker.FileName;gameSettingsTask=apply();}}
        catch{message.Text=L.T("Choose an existing .exe file using its full path.");message.Visibility=Visibility.Visible;}
      };
      gameSettingsTask=refresh();
    }
  }
}
