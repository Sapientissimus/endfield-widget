using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  partial class Widget {
    void WireLanguageSettings(Window dialog) {
      var parent=(StackPanel)dialog.FindName("GameSettingsPanel");
      var section=new StackPanel{Margin=new Thickness(0,12,0,0)};var label=SettingsLabel(L.T("Interface language"));label.Margin=new Thickness(0,0,0,7);section.Children.Add(label);
      var choice=RegisterControl(dialog,"LanguageSelector",new ComboBox{ItemsSource=L.Languages,DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue=config.InterfaceLanguage,MinHeight=32,FontSize=12,HorizontalAlignment=HorizontalAlignment.Stretch});
      System.Windows.Automation.AutomationProperties.SetName(choice,L.T("Interface language"));section.Children.Add(choice);parent.Children.Insert(1,section);
      choice.SelectionChanged+=(s,e)=>{
        var selected=choice.SelectedItem as LanguageChoice;string next=selected==null?null:selected.Code;if(next==null || next==config.InterfaceLanguage)return;
        if(refreshBusy){choice.SelectedValue=config.InterfaceLanguage;var feedback=(TextBlock)dialog.FindName("SettingsFeedback");feedback.Text=L.T("Wait for the current refresh to finish.");feedback.Visibility=Visibility.Visible;return;}
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.Normal,new Action(()=>{
          var latest=choice.SelectedItem as LanguageChoice;
          if(closing || !dialog.IsVisible || settingsDialog!=dialog || latest==null || latest.Code!=next)return;
          if(refreshBusy){choice.SelectedValue=config.InterfaceLanguage;var feedback=(TextBlock)dialog.FindName("SettingsFeedback");feedback.Text=L.T("Wait for the current refresh to finish.");feedback.Visibility=Visibility.Visible;return;}
          ChangeInterfaceLanguage(next,dialog);
        }));
      };
    }
    static IEnumerable<FrameworkElement> LanguageElements(DependencyObject root) {
      var element=root as FrameworkElement;if(element!=null)yield return element;
      foreach(object child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject)foreach(var descendant in LanguageElements((DependencyObject)child))yield return descendant;
    }
    void TranslateTray(Forms.ToolStripItemCollection items,string previous) {
      foreach(Forms.ToolStripItem item in items){item.Text=L.ReText(item.Text,previous);item.ToolTipText=L.ReText(item.ToolTipText,previous);var drop=item as Forms.ToolStripDropDownItem;if(drop!=null)TranslateTray(drop.DropDownItems,previous);}
    }
    void ChangeInterfaceLanguage(string code,Window dialog) {
      code=L.Normalize(code);if(code==config.InterfaceLanguage || closing)return;
      string previous=L.Code,disabled=L.T("Disabled"),monitorId=null;double scroll=0;string draft="";Rect? bounds=null;var edits=new Dictionary<string,string>();var choices=new Dictionary<string,int>();var sections=new List<bool>();
      if(dialog!=null && dialog.IsVisible) {
        draft=((PasswordBox)dialog.FindName("TokenInput")).Password;scroll=((ScrollViewer)dialog.FindName("SettingsScroll")).VerticalOffset;
        foreach(var element in LanguageElements(dialog)){var text=element as TextBox;if(text!=null && text.Name.Length>0 && (!text.IsReadOnly || text.Name.StartsWith("ShortcutInput",StringComparison.Ordinal)))edits[text.Name]=text.Text;var expander=element as Expander;if(expander!=null)sections.Add(expander.IsExpanded);}
        foreach(string name in new[]{"BlockModeSelector","CropModeSelector","AnchorSelector"}){var combo=dialog.FindName(name) as ComboBox;if(combo!=null)choices[name]=combo.SelectedIndex;}
        var monitor=dialog.FindName("MonitorSelector") as ComboBox;var displays=CurrentDisplays();if(monitor!=null && monitor.SelectedIndex>=0 && monitor.SelectedIndex<displays.Count)monitorId=displays[monitor.SelectedIndex].Id;
        bounds=new Rect(dialog.Left,dialog.Top,dialog.Width,dialog.Height);dialog.Close();
      }
      if(widgetMenu!=null)widgetMenu.IsOpen=false;if(tray!=null && tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Close();
      config.InterfaceLanguage=code;L.Set(code);window.Language=XmlLanguage.GetLanguage(L.Culture.Name);
      sanityError=L.ReText(sanityError,previous);newsError=L.ReText(newsError,previous);bannerError=L.ReText(bannerError,previous);gameLaunchError=L.ReText(gameLaunchError,previous);
      recovery.Epoch++;retryNews=retryBanners=false;liveNews=false;bannersVerified=false;
      using(Publications.Enter(Publications.ForLanguage(code))) {
        schedule=CachedData.Schedule();bannerState=CachedData.Banners();
        calendarFeed=CalendarFeedStore.Load(bannerState);banners=new BannerClient(http);news=new NewsClient(http,banners);calendarDetailCache=null;
      }
      ReleaseCalendarDetails();calendarSnapshot=null;calendarRecords=null;calendarGridStamp=calendarDayStamp=calendarAgendaStamp="";
      BuildWidgetMenu();if(tray!=null && tray.ContextMenuStrip!=null)TranslateTray(tray.ContextMenuStrip.Items,previous);
      Find<TextBlock>("VersionLabel").ToolTip=L.T("Update details");Find<TextBlock>("BannerLabel").ToolTip=L.T("Open banner");
      if(BannerClient.Select(bannerState,DateTimeOffset.UtcNow,config.BannerRegion=="Asia")==null){Find<TextBlock>("OperatorName").Text=L.T("OPERATOR");Find<TextBlock>("BannerLabel").Text="";}
      cardLayoutDirty=true;Paint();ApplyLayout(true);UpdateGameLaunchControls();UpdateTrayStatus(DateTimeOffset.UtcNow);
      if(calendarDialog!=null){calendarDialog.Language=XmlLanguage.GetLanguage(L.Culture.Name);var months=CalendarFind<ComboBox>("CalendarPickerMonth");months.ItemsSource=Enumerable.Range(1,12).Select(m=>L.Culture.DateTimeFormat.GetMonthName(m)).ToArray();RenderCalendar();}
      nextNews=nextBanner=DateTimeOffset.MinValue;SavePosition();
      if(bounds.HasValue) {
        Settings();var replacement=settingsDialog;replacement.Left=bounds.Value.Left;replacement.Top=bounds.Value.Top;replacement.Height=Math.Min(bounds.Value.Height,SystemParameters.WorkArea.Height-40);
        ((PasswordBox)replacement.FindName("TokenInput")).Password=draft;draft="";
        foreach(var pair in choices){var combo=replacement.FindName(pair.Key) as ComboBox;if(combo!=null && pair.Value>=0 && pair.Value<combo.Items.Count)combo.SelectedIndex=pair.Value;}
        if(monitorId!=null){var monitor=replacement.FindName("MonitorSelector") as ComboBox;int selected=CurrentDisplays().ToList().FindIndex(d=>d.Id==monitorId);if(monitor!=null && selected>=0 && selected<monitor.Items.Count)monitor.SelectedIndex=selected;}
        foreach(var pair in edits){var text=replacement.FindName(pair.Key) as TextBox;if(text!=null && (!text.IsReadOnly || pair.Key.StartsWith("ShortcutInput",StringComparison.Ordinal)))text.Text=pair.Key.StartsWith("ShortcutInput",StringComparison.Ordinal) && pair.Value==disabled?L.T("Disabled"):pair.Value;}
        int i=0;foreach(var expander in LanguageElements(replacement).OfType<Expander>()){if(i<sections.Count)expander.IsExpanded=sections[i++];}
        replacement.Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{replacement.UpdateLayout();((ScrollViewer)replacement.FindName("SettingsScroll")).ScrollToVerticalOffset(scroll);}));
      }
      if(!preview && !Storage.Isolated)window.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(async()=>{if(!refreshBusy && !closing)await Refresh(false);}));
    }
  }
}
