using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  partial class Widget {
    Window calendarDialog;
    DateTime calendarMonth,calendarDay;
    Func<DateTimeOffset> calendarNow=()=>DateTimeOffset.UtcNow;
    CalendarSnapshot calendarSnapshot;
    List<CalendarEntry> calendarRecords;
    object calendarScheduleReference,calendarBannerReference,calendarFeedReference;
    bool calendarServerAsia,calendarRemembered,calendarRendering,calendarPositioning;
    int calendarFilterMask=CalendarData.AllKinds;
    string calendarQuery="",calendarSelectedId,calendarZone="",calendarSnapshotQuery="",calendarGridStamp="",calendarDayStamp="",calendarAgendaStamp="";
    int calendarSnapshotMask=-1;
    DateTimeOffset calendarNextPaint=DateTimeOffset.MinValue;
    Rect? calendarLastBounds;
    DispatcherTimer calendarSearchTimer;
    EventHandler calendarDisplayChanged;
    CalendarSourceCommand calendarSourceCommand;
    DataTemplate calendarCardTemplate,calendarAgendaTemplate;
    ResourceDictionary calendarStyles;
    string calendarMarkup;
    readonly List<CalendarDayVisual> calendarDayVisuals=new List<CalendarDayVisual>();
    List<CalendarCardModel> calendarDayModels=new List<CalendarCardModel>(),calendarAgendaModels=new List<CalendarCardModel>();
    Action<string> calendarOpenOverride;
    internal int CalendarGridBuilds,CalendarCardBuilds,CalendarAgendaBuilds;
    sealed class CalendarDayVisual {
      public Button Button;
      public TextBlock Number;
      public StackPanel Markers;
      public string MarkerStamp="";
    }
    T CalendarFind<T>(string name) where T:class{return calendarDialog==null?null:calendarDialog.FindName(name) as T;}
    void SelectCalendarDay(DateTime day) {
      if(day.Year>9998)return;
      var previousMonth=calendarMonth;
      calendarDay=day.Date;calendarMonth=new DateTime(day.Year,day.Month,1);
      if(calendarSelectedId!=null && calendarSnapshot!=null && !calendarSnapshot.OnDay(day).Any(e=>e.Id==calendarSelectedId))calendarSelectedId=null;
      RenderCalendar(false);
      if(previousMonth!=calendarMonth)AnimateCalendarChange(calendarMonth>previousMonth?1:-1);
    }
    void PaintBannerEnd(DateTimeOffset now) {
      var current=BannerClient.Select(bannerState,now,config.BannerRegion=="Asia");var end=BannerClient.End(current,config.BannerRegion=="Asia");bool show=end.HasValue && end>now;
      foreach(string name in new[]{"BannerEndLabel","CompactBannerEnd"}) {
        var text=Find<TextBlock>(name);text.Visibility=show?Visibility.Visible:Visibility.Collapsed;text.Text=show?L.T("Time left · ")+Clock.Remaining(end.Value-now):"";
        text.ToolTip=show?Clock.Date(end.Value)+" · "+Clock.Time(end.Value)+"":null;
      }
    }

    void ShowCalendar() {
      if(calendarDialog!=null && calendarDialog.IsVisible){calendarDialog.Activate();return;}
      var dialog=CreateCalendarWindow();dialog.Language=System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.Name);calendarDialog=dialog;CalendarChrome.Apply(dialog);
      if(!calendarRemembered){calendarDay=Clock.Local(calendarNow()).Date;calendarMonth=new DateTime(calendarDay.Year,calendarDay.Month,1);calendarRemembered=true;}
      calendarGridStamp=calendarDayStamp=calendarAgendaStamp="";
      calendarDayVisuals.Clear();calendarDayModels=new List<CalendarCardModel>();calendarAgendaModels=new List<CalendarCardModel>();
      if(calendarSourceCommand==null)calendarSourceCommand=new CalendarSourceCommand(OpenCalendarSource);
      InitializeCalendarTemplates();
      CalendarFind<Image>("CalendarOperatorImage").SetBinding(Image.SourceProperty,new Binding("Source"){Source=Find<Image>("OperatorImage"),Mode=BindingMode.OneWay});
      CalendarFind<ListBox>("CalendarAgenda").ItemTemplate=calendarAgendaTemplate;
      CalendarFind<ListBox>("CalendarEvents").ItemTemplate=calendarCardTemplate;
      CalendarScrolling.Attach(CalendarFind<ListBox>("CalendarEvents"));
      CalendarScrolling.Attach(CalendarFind<ListBox>("CalendarAgenda"));
      CalendarFind<TextBox>("CalendarSearch").Text=calendarQuery;
      CalendarFind<TextBox>("CalendarSearch").TextChanged+=(s,e)=>{
        UpdateCalendarSearchHint();if(calendarSearchTimer==null)return;calendarSearchTimer.Stop();calendarSearchTimer.Start();
      };
      calendarSearchTimer=new DispatcherTimer(DispatcherPriority.Background,dialog.Dispatcher){Interval=TimeSpan.FromMilliseconds(150)};
      calendarSearchTimer.Tick+=CalendarSearchElapsed;
      CalendarFind<Button>("CalendarClearSearch").Click+=(s,e)=>{CalendarFind<TextBox>("CalendarSearch").Clear();ApplyCalendarSearch();};
      CalendarFind<Button>("CalendarClose").Click+=(s,e)=>dialog.Close();
      CalendarFind<Button>("CalendarPrevious").Click+=(s,e)=>MoveCalendarMonth(-1);
      CalendarFind<Button>("CalendarNext").Click+=(s,e)=>MoveCalendarMonth(1);
      CalendarFind<Button>("CalendarToday").Click+=(s,e)=>{calendarSelectedId=null;SelectCalendarDay(Clock.Local(calendarNow()).Date);};
      CalendarFind<Button>("CalendarUpcoming").Click+=(s,e)=>{
        EnsureCalendarSnapshot(false);var next=calendarSnapshot.NearestDay(calendarNow());
        if(next.HasValue){calendarSelectedId=null;SelectCalendarDay(next.Value);}
      };
      CalendarFind<Button>("CalendarMonth").Click+=(s,e)=>OpenCalendarPicker();
      CalendarFind<Button>("CalendarPickerApply").Click+=(s,e)=>ApplyCalendarPicker();
      var monthPicker=CalendarFind<ComboBox>("CalendarPickerMonth");var culture=L.Culture;
      monthPicker.ItemsSource=Enumerable.Range(1,12).Select(m=>culture.DateTimeFormat.GetMonthName(m)).ToArray();
      foreach(string name in new[]{"CalendarAll","CalendarMaintenance","CalendarDebuts","CalendarBannerEnds","CalendarEventEnds"})CalendarFind<ToggleButton>(name).Click+=CalendarFilterClicked;
      CalendarFind<UniformGrid>("CalendarDays").PreviewKeyDown+=CalendarDayKey;
      CalendarFind<ListBox>("CalendarAgenda").SelectionChanged+=(s,e)=>{
        if(calendarRendering)return;var model=CalendarFind<ListBox>("CalendarAgenda").SelectedItem as CalendarCardModel;
        if(model!=null){calendarSelectedId=model.Id;SelectCalendarDay(Clock.Local(model.Entry.At<calendarNow() && model.Entry.End.HasValue && model.Entry.End>calendarNow()?calendarNow():model.Entry.At).Date);}
      };
      CalendarFind<Grid>("CalendarDragHandle").MouseLeftButtonDown+=(s,e)=>{
        DependencyObject current=e.OriginalSource as DependencyObject;bool control=false;
        while(current!=null && current!=s){if(current is ButtonBase || current is TextBox){control=true;break;}current=VisualTreeHelper.GetParent(current);}
        if(!control && e.LeftButton==MouseButtonState.Pressed){try{dialog.DragMove();}catch(InvalidOperationException){}ClampCalendarWindow();e.Handled=true;}
      };
      dialog.PreviewKeyDown+=(s,e)=>{
        if(e.Key==Key.Escape){var picker=CalendarFind<Popup>("CalendarPicker");if(picker.IsOpen)picker.IsOpen=false;else dialog.Close();e.Handled=true;}
      };
      dialog.SizeChanged+=(s,e)=>{LayoutCalendar();if(!calendarPositioning && dialog.IsVisible)ClampCalendarWindow();};
      dialog.Closed+=(s,e)=>ReleaseCalendar(dialog);
      dialog.Loaded+=(s,e)=>{LayoutCalendar();if(!preview && !Storage.Isolated)ClampCalendarWindow();};
      DesktopNative.HideFromSwitcher(dialog,false);
      PositionCalendarWindow(dialog);
      if(!preview && !Storage.Isolated) {
        calendarDisplayChanged=(s,e)=>PostBackground(()=>ClampCalendarWindow());SystemEvents.DisplaySettingsChanged+=calendarDisplayChanged;
      }
      RenderCalendar(false);dialog.Show();LayoutCalendar();
    }
    void ReleaseCalendar(Window dialog) {
      CalendarMotion.Stop(dialog.FindName("CalendarMonthPane") as FrameworkElement);CalendarMotion.Stop(dialog.FindName("CalendarAgendaPane") as FrameworkElement);ReleaseCalendarDetails();
      var search=dialog.FindName("CalendarSearch") as TextBox;if(search!=null)calendarQuery=search.Text;
      var picker=dialog.FindName("CalendarPicker") as Popup;if(picker!=null)picker.IsOpen=false;
      if(calendarSearchTimer!=null){calendarSearchTimer.Stop();calendarSearchTimer.Tick-=CalendarSearchElapsed;calendarSearchTimer=null;}
      if(calendarDisplayChanged!=null){SystemEvents.DisplaySettingsChanged-=calendarDisplayChanged;calendarDisplayChanged=null;}
      if(dialog.Left>-5000 && dialog.Top>-5000 && Placement.Finite(dialog.ActualWidth) && Placement.Finite(dialog.ActualHeight))calendarLastBounds=new Rect(dialog.Left,dialog.Top,dialog.ActualWidth,dialog.ActualHeight);
      foreach(string name in new[]{"CalendarAgenda","CalendarEvents"}){var list=dialog.FindName(name) as ListBox;if(list!=null){list.ItemsSource=null;list.ItemTemplate=null;}}
      calendarDayVisuals.Clear();calendarDayModels.Clear();calendarAgendaModels.Clear();calendarDialog=null;
    }
    Rect CalendarWorkArea() {
      var scale=PixelScale();DesktopNative.Rectangle bounds;var handle=new WindowInteropHelper(window).Handle;
      Forms.Screen screen;
      if(calendarDialog!=null && calendarDialog.IsVisible && calendarDialog.Left>-5000)
        screen=Forms.Screen.FromRectangle(new System.Drawing.Rectangle((int)(calendarDialog.Left*scale.X),(int)(calendarDialog.Top*scale.Y),(int)(Math.Max(1,calendarDialog.ActualWidth)*scale.X),(int)(Math.Max(1,calendarDialog.ActualHeight)*scale.Y)));
      else if(handle!=IntPtr.Zero && DesktopNative.GetWindowRect(handle,out bounds))screen=Forms.Screen.FromRectangle(new System.Drawing.Rectangle(bounds.Left,bounds.Top,Math.Max(1,bounds.Right-bounds.Left),Math.Max(1,bounds.Bottom-bounds.Top)));
      else screen=Forms.Screen.PrimaryScreen;
      var work=screen.WorkingArea;return new Rect(work.Left/scale.X,work.Top/scale.Y,work.Width/scale.X,work.Height/scale.Y);
    }
    static Rect FitCalendar(Rect desired,Rect work) {
      double width=Math.Min(Math.Max(300,desired.Width),Math.Max(300,work.Width-24)),height=Math.Min(Math.Max(260,desired.Height),Math.Max(260,work.Height-24));
      return new Rect(Placement.Clamp(desired.Left,work.Left+12,Math.Max(work.Left+12,work.Right-width-12)),Placement.Clamp(desired.Top,work.Top+12,Math.Max(work.Top+12,work.Bottom-height-12)),width,height);
    }
    void PositionCalendarWindow(Window dialog) {
      var work=CalendarWorkArea();var desired=calendarLastBounds??new Rect(work.Left+(work.Width-960)/2,work.Top+(work.Height-790)/2,960,790);
      var fitted=FitCalendar(desired,work);dialog.WindowStartupLocation=WindowStartupLocation.Manual;
      dialog.MinWidth=Math.Min(420,fitted.Width);dialog.MinHeight=Math.Min(400,fitted.Height);
      dialog.Width=fitted.Width;dialog.Height=fitted.Height;dialog.Left=fitted.Left;dialog.Top=fitted.Top;
      if((preview || Storage.Isolated) && !diagnosticView)dialog.Left=dialog.Top=-10000;
    }
    void ClampCalendarWindow() {
      if(calendarDialog==null || calendarPositioning || ((preview || Storage.Isolated) && !diagnosticView))return;
      calendarPositioning=true;
      try{var fit=FitCalendar(new Rect(calendarDialog.Left,calendarDialog.Top,Math.Max(1,calendarDialog.Width),Math.Max(1,calendarDialog.Height)),CalendarWorkArea());calendarDialog.MinWidth=Math.Min(420,fit.Width);calendarDialog.MinHeight=Math.Min(400,fit.Height);calendarDialog.Width=fit.Width;calendarDialog.Height=fit.Height;calendarDialog.Left=fit.Left;calendarDialog.Top=fit.Top;}finally{calendarPositioning=false;}
    }
    void LayoutCalendar() {
      if(calendarDialog==null)return;
      bool compact=calendarDialog.ActualWidth<790;
      CalendarFind<ColumnDefinition>("CalendarRightColumn").Width=compact?new GridLength(0):new GridLength(1,GridUnitType.Star);
      CalendarFind<ColumnDefinition>("CalendarGap").Width=new GridLength(compact?0:24);
      var agenda=CalendarFind<Grid>("CalendarAgendaPane");Grid.SetColumn(agenda,compact?0:2);Grid.SetRow(agenda,compact?1:0);agenda.Margin=compact?new Thickness(0,22,0,0):new Thickness(0);
      CalendarFind<ListBox>("CalendarAgenda").Height=Math.Max(260,Math.Min(760,calendarDialog.ActualHeight-314));
      CalendarFind<ListBox>("CalendarEvents").Height=Math.Max(120,Math.Min(260,calendarDialog.ActualHeight-640));
      CalendarFind<TextBlock>("CalendarLiveStatus").MaxWidth=calendarDialog.ActualWidth<600?105:180;
      var host=CalendarFind<Grid>("CalendarSearchHost");host.Width=Math.Max(180,Math.Min(430,calendarDialog.ActualWidth-90-(CalendarFind<TextBlock>("CalendarLiveStatus").Text.Length>0?120:0)));
    }
    void MoveCalendarMonth(int offset) {
      if(offset<0 && calendarMonth.Year==1 && calendarMonth.Month==1 || offset>0 && calendarMonth.Year==9998 && calendarMonth.Month==12)return;
      var month=calendarMonth.AddMonths(offset);SelectCalendarDay(new DateTime(month.Year,month.Month,Math.Min(calendarDay.Day,DateTime.DaysInMonth(month.Year,month.Month))));
    }
    void OpenCalendarPicker() {
      CalendarFind<ComboBox>("CalendarPickerMonth").SelectedIndex=calendarMonth.Month-1;CalendarFind<TextBox>("CalendarPickerYear").Text=calendarMonth.Year.ToString(CultureInfo.InvariantCulture);CalendarFind<TextBlock>("CalendarPickerError").Visibility=Visibility.Collapsed;CalendarFind<Popup>("CalendarPicker").IsOpen=true;
    }
    void ApplyCalendarPicker() {
      int year,month=CalendarFind<ComboBox>("CalendarPickerMonth").SelectedIndex+1;
      if(!int.TryParse(CalendarFind<TextBox>("CalendarPickerYear").Text,NumberStyles.None,CultureInfo.InvariantCulture,out year) || year<1 || year>9998 || month<1 || month>12){CalendarFind<TextBlock>("CalendarPickerError").Visibility=Visibility.Visible;return;}
      CalendarFind<Popup>("CalendarPicker").IsOpen=false;SelectCalendarDay(new DateTime(year,month,Math.Min(calendarDay.Day,DateTime.DaysInMonth(year,month))));
    }
    void CalendarSearchElapsed(object sender,EventArgs args){ApplyCalendarSearch();}
    void ApplyCalendarSearch() {
      if(calendarSearchTimer!=null)calendarSearchTimer.Stop();if(calendarDialog==null)return;
      calendarQuery=CalendarFind<TextBox>("CalendarSearch").Text;UpdateCalendarSearchHint();RenderCalendar(false);
    }
    void UpdateCalendarSearchHint() {
      var search=CalendarFind<TextBox>("CalendarSearch");if(search==null)return;bool empty=search.Text.Length==0;CalendarFind<TextBlock>("CalendarSearchHint").Visibility=empty?Visibility.Visible:Visibility.Collapsed;CalendarFind<Button>("CalendarClearSearch").Visibility=empty?Visibility.Collapsed:Visibility.Visible;
    }
    void CalendarFilterClicked(object sender,RoutedEventArgs args) {
      var button=(ToggleButton)sender;int mask=int.Parse((string)button.Tag,CultureInfo.InvariantCulture);
      if(mask==CalendarData.AllKinds)calendarFilterMask=CalendarData.AllKinds;else if(button.IsChecked==true)calendarFilterMask|=mask;else calendarFilterMask&=~mask;
      RenderCalendar(false);
      AnimateCalendarChange(0);
    }
    void CalendarDayKey(object sender,KeyEventArgs args) {
      var button=Keyboard.FocusedElement as Button;if(button==null || !(button.Tag is DateTime))return;
      int days=args.Key==Key.Left?-1:args.Key==Key.Right?1:args.Key==Key.Up?-7:args.Key==Key.Down?7:0;if(days==0)return;
      var day=(DateTime)button.Tag;
      DateTime next;try{next=day.AddDays(days);}catch(ArgumentOutOfRangeException){args.Handled=true;return;}
      if(next.Year>9998){args.Handled=true;return;}
      SelectCalendarDay(next);var target=calendarDayVisuals.FirstOrDefault(v=>v.Button.Tag is DateTime && (DateTime)v.Button.Tag==next);if(target!=null)target.Button.Focus();args.Handled=true;
    }
    void EnsureCalendarSnapshot(bool dataChanged) {
      bool asia=config.BannerRegion=="Asia";string zone=CalendarData.ZoneKey();
      bool rebuild=dataChanged || calendarRecords==null || !ReferenceEquals(calendarScheduleReference,schedule) || !ReferenceEquals(calendarBannerReference,bannerState) || !ReferenceEquals(calendarFeedReference,calendarFeed) || asia!=calendarServerAsia;
      if(rebuild){calendarRecords=CalendarData.Entries(schedule,bannerState,asia,calendarFeed);calendarScheduleReference=schedule;calendarBannerReference=bannerState;calendarFeedReference=calendarFeed;calendarServerAsia=asia;}
      if(rebuild || calendarSnapshot==null || calendarSnapshotMask!=calendarFilterMask || calendarSnapshotQuery!=calendarQuery || calendarZone!=zone) {
        bool moved=calendarZone!="" && calendarZone!=zone;calendarSnapshot=new CalendarSnapshot(calendarRecords,calendarFilterMask,calendarQuery);
        calendarSnapshotMask=calendarFilterMask;calendarSnapshotQuery=calendarQuery;calendarZone=zone;
        if(moved && calendarSelectedId!=null){var selected=calendarRecords.FirstOrDefault(e=>e.Id==calendarSelectedId);if(selected!=null){calendarDay=Clock.Local(selected.At).Date;calendarMonth=new DateTime(calendarDay.Year,calendarDay.Month,1);}}
      }
    }
    void RenderCalendar(bool dataChanged=true) {
      if(calendarDialog==null)return;
      calendarRendering=true;
      try {
        EnsureCalendarSnapshot(dataChanged);var now=calendarNow();var today=Clock.Local(now).Date;var culture=L.Culture;
        string month=calendarMonth.ToString(L.Code=="uk"?"MMMM yyyy":culture.DateTimeFormat.YearMonthPattern,culture);CalendarFind<Button>("CalendarMonth").Content=char.ToUpper(month[0],culture)+month.Substring(1);
        CalendarFind<TextBlock>("CalendarSelected").Text=L.DateCaption(calendarDay,true);
        CalendarFind<Button>("CalendarPrevious").IsEnabled=calendarMonth>new DateTime(1,1,1);CalendarFind<Button>("CalendarNext").IsEnabled=calendarMonth<new DateTime(9998,12,1);
        CalendarFind<Button>("CalendarUpcoming").IsEnabled=calendarSnapshot.NearestDay(now).HasValue;
        int available=calendarRecords.Aggregate(0,(mask,e)=>mask|CalendarData.Mask(e.Kind));
        foreach(string name in new[]{"CalendarAll","CalendarMaintenance","CalendarDebuts","CalendarBannerEnds","CalendarEventEnds"}) {
          var filter=CalendarFind<ToggleButton>(name);int bit=int.Parse((string)filter.Tag,CultureInfo.InvariantCulture);
          filter.Visibility=bit==15 || (available&bit)!=0?Visibility.Visible:Visibility.Collapsed;filter.IsChecked=bit==15?calendarFilterMask==15:(calendarFilterMask&bit)!=0;
        }
        CalendarFind<TextBlock>("CalendarLiveStatus").Text=calendarCacheFailed?L.T("Data was not saved"):calendarFeed!=null && calendarFeed.Partial?(calendarFeed.SuccessCount>0?L.T("Some dates were updated"):L.T("Showing saved dates")):!preview && !calendarFeedLive?L.T("Saved dates"):"";
        string gridStamp=calendarSnapshot.Fingerprint+"|"+calendarMonth.Ticks+"|"+calendarDay.Ticks+"|"+today.Ticks;
        if(gridStamp!=calendarGridStamp){RenderCalendarDays(today);calendarGridStamp=gridStamp;CalendarGridBuilds++;}
        var selected=calendarSnapshot.OnDay(calendarDay);CalendarFind<TextBlock>("CalendarSelectedCount").Text=selected.Count==0?"":selected.Count+" "+Clock.Unit(selected.Count,L.T("event"),L.T("event.count.few"),L.T("event.count.many"));
        string dayStamp=CalendarData.Fingerprint(selected)+"|"+calendarDay.Ticks+"|"+calendarZone;
        if(dayStamp!=calendarDayStamp){RenderCalendarCards(selected,now);calendarDayStamp=dayStamp;CalendarCardBuilds++;}
        else foreach(var model in calendarDayModels)model.Refresh(now);
        var upcoming=calendarSnapshot.Agenda(now,30).ToList();
        string agendaStamp=CalendarData.Fingerprint(upcoming)+"|"+calendarZone+"|"+today.Ticks;
        if(agendaStamp!=calendarAgendaStamp){RenderCalendarAgenda(upcoming,now);calendarAgendaStamp=agendaStamp;CalendarAgendaBuilds++;}
        else foreach(var model in calendarAgendaModels)model.Refresh(now);
        ScheduleCalendarPaint(now);UpdateCalendarSearchHint();
      }finally{calendarRendering=false;}
    }
    void RenderCalendarDays(DateTime today) {
      var panel=CalendarFind<UniformGrid>("CalendarDays");
      if(calendarDayVisuals.Count==0)for(int i=0;i<42;i++) {
        var grid=new Grid{Margin=new Thickness(1,4,1,3)};grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(9)});
        var number=new TextBlock{FontSize=13,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};var markers=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetRow(markers,1);grid.Children.Add(number);grid.Children.Add(markers);
        var button=new Button{Content=grid,Style=(Style)calendarDialog.FindResource("CalendarDayStyle")};button.Click+=(s,e)=>SelectCalendarDay((DateTime)((Button)s).Tag);
        panel.Children.Add(button);calendarDayVisuals.Add(new CalendarDayVisual{Button=button,Number=number,Markers=markers});
      }
      var first=CalendarSnapshot.FirstDay(calendarMonth);
      for(int i=0;i<42;i++) {
        var date=first.AddDays(i);var visual=calendarDayVisuals[i];var entries=calendarSnapshot.OnDay(date);bool selected=date==calendarDay,isToday=date==today;
        visual.Button.Tag=date;visual.Number.Text=date.Day.ToString(CultureInfo.InvariantCulture);visual.Button.IsTabStop=selected;visual.Button.IsEnabled=date.Year<=9998;
        visual.Number.FontWeight=isToday || selected?FontWeights.SemiBold:FontWeights.Normal;
        visual.Button.SetResourceReference(Control.BackgroundProperty,selected?"ThemeAccentBrush":entries.Count>0?"ThemeBadgeBrush":"ThemeCardBrush");
        visual.Number.SetResourceReference(TextBlock.ForegroundProperty,selected?"ThemeInkBrush":date.Month!=calendarMonth.Month || date<today?"ThemeMutedBrush":"ThemeTextBrush");
        visual.Button.SetResourceReference(Control.BorderBrushProperty,isToday?"ThemeAccentBrush":"ThemeCardBrush");
        visual.Button.BorderThickness=new Thickness(isToday?1.4:1);
        string markers=string.Join(",",entries.Select(e=>e.Kind).Distinct().OrderBy(k=>k))+"|"+entries.Count+"|"+selected;
        if(visual.MarkerStamp!=markers) {
          visual.Markers.Children.Clear();
          foreach(var kind in entries.Select(e=>e.Kind).Distinct().OrderBy(k=>k).Take(3)) {
            var path=new Path{Data=Geometry.Parse(CalendarPresentation.Icon(kind)),Width=5,Height=5,Stretch=Stretch.Uniform,StrokeThickness=1.5,Margin=new Thickness(1.5,0,1.5,0)};path.SetResourceReference(Shape.StrokeProperty,selected?"ThemeInkBrush":"ThemeAccentBrush");visual.Markers.Children.Add(path);
          }
          if(entries.Count>3){var count=new TextBlock{Text="+"+(entries.Count-3),FontSize=7,Margin=new Thickness(2,-2,0,0)};count.SetResourceReference(TextBlock.ForegroundProperty,selected?"ThemeInkBrush":"ThemeSecondaryTextBrush");visual.Markers.Children.Add(count);}visual.MarkerStamp=markers;
        }
        string accessible=date.ToString("dddd, d MMMM yyyy",L.Culture)+(isToday?L.T(" · today"):"")+(selected?L.T(" · selected"):"")+(entries.Count>0?" · "+entries.Count+" "+Clock.Unit(entries.Count,L.T("event"),L.T("event.count.few"),L.T("event.count.many")):"");
        AutomationProperties.SetName(visual.Button,accessible);visual.Button.ToolTip=entries.Count>0?accessible+"\n"+string.Join("\n",entries.Select(e=>CalendarData.Label(e.Kind)+" · "+e.Name).Take(8)):accessible;
      }
    }
    ScrollViewer CalendarDayScroller(){return FindCalendarVisual<ScrollViewer>(CalendarFind<ListBox>("CalendarEvents"));}
    void RenderCalendarCards(IList<CalendarEntry> entries,DateTimeOffset now) {
      var list=CalendarFind<ListBox>("CalendarEvents");var scroll=CalendarDayScroller();double offset=scroll==null?0:scroll.VerticalOffset;
      calendarDayModels=entries.Select(entry=>new CalendarCardModel(entry,now,calendarSourceCommand,CalendarDetailFor)).ToList();list.ItemsSource=calendarDayModels;
      var empty=CalendarFind<TextBlock>("CalendarDayEmpty");empty.Visibility=entries.Count==0?Visibility.Visible:Visibility.Collapsed;
      empty.Text=CalendarData.Normalize(calendarQuery).Length>0?L.T("No events match this search"):calendarFilterMask!=15?L.T("No events of the selected types on this day"):calendarFeed!=null && calendarFeed.Partial?L.T("Dates are unavailable. We will retry automatically."):L.T("No events announced for this day");
      if(scroll!=null)scroll.ScrollToVerticalOffset(offset);
    }
    void RenderCalendarAgenda(IList<CalendarEntry> entries,DateTimeOffset now) {
      var list=CalendarFind<ListBox>("CalendarAgenda");var scroll=FindCalendarVisual<ScrollViewer>(list);double offset=scroll==null?0:scroll.VerticalOffset;
      calendarAgendaModels=entries.Select(e=>new CalendarCardModel(e,now,calendarSourceCommand,CalendarDetailFor)).ToList();var view=new ListCollectionView(calendarAgendaModels);view.GroupDescriptions.Add(new PropertyGroupDescription("DateHeading"));list.ItemsSource=view;
      if(calendarSelectedId!=null)list.SelectedItem=calendarAgendaModels.FirstOrDefault(m=>m.Id==calendarSelectedId);
      CalendarFind<TextBlock>("CalendarAgendaCount").Text=entries.Count==0?"":entries.Count+" "+Clock.Unit(entries.Count,L.T("event"),L.T("event.count.few"),L.T("event.count.many"));
      var empty=CalendarFind<TextBlock>("CalendarAgendaEmpty");empty.Visibility=entries.Count==0?Visibility.Visible:Visibility.Collapsed;
      empty.Text=CalendarData.Normalize(calendarQuery).Length>0?L.T("No events match this search"):calendarFilterMask!=15?L.T("No upcoming events of the selected types"):calendarFeed!=null && calendarFeed.Partial?L.T("Data is unavailable. Saved dates remain in the calendar."):L.T("No upcoming events announced");
      if(scroll!=null)scroll.ScrollToVerticalOffset(offset);
    }
    static T FindCalendarVisual<T>(DependencyObject root) where T:DependencyObject {
      if(root==null)return null;if(root is T)return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var value=FindCalendarVisual<T>(VisualTreeHelper.GetChild(root,i));if(value!=null)return value;}return null;
    }
    void OpenCalendarSource(CalendarEntry entry) {
      if(!CalendarData.Official(entry.Source))return;calendarSelectedId=entry.Id;
      if(calendarOpenOverride!=null)calendarOpenOverride(entry.Source);else Open(entry.Source);
    }
    void ScheduleCalendarPaint(DateTimeOffset now) {
      var zone=Clock.ZoneOverride==null?TimeZoneInfo.Local:Clock.ZoneOverride();var midnight=Clock.Local(now).Date.AddDays(1);
      try{if(zone.IsInvalidTime(midnight))midnight=midnight.AddHours(1);calendarNextPaint=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight,zone),TimeSpan.Zero);}catch{calendarNextPaint=now.AddHours(1);}
      foreach(var entry in calendarSnapshot.Entries) {
        if(entry.At>now){var delay=RefreshTiming.CountdownDelay(entry.At-now);if(now+delay<calendarNextPaint)calendarNextPaint=now+delay;if(entry.At<calendarNextPaint)calendarNextPaint=entry.At;}
        if(entry.End.HasValue && entry.End>now && entry.End<calendarNextPaint)calendarNextPaint=entry.End.Value;
      }
    }
    void TickCalendar(DateTimeOffset now){if(calendarDialog!=null && calendarDialog.IsVisible && now>=calendarNextPaint)RenderCalendar(false);}
    void InitializeCalendarTemplates() {
      if(calendarCardTemplate!=null)return;
      const string start=@"<DataTemplate xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation"" xmlns:local=""clr-namespace:EndfieldWidget;assembly=EndfieldWidget""><StackPanel>
       <StackPanel Orientation=""Horizontal""><Path Data=""{Binding Icon}"" Width=""10"" Height=""10"" Stretch=""Uniform"" Stroke=""{DynamicResource ThemeAccentBrush}"" StrokeThickness=""1.2"" Margin=""0,0,7,0"" VerticalAlignment=""Center""/><TextBlock Text=""{Binding Category}"" FontSize=""10"" FontWeight=""SemiBold"" Foreground=""{DynamicResource ThemeSoftAccentBrush}"" TextWrapping=""Wrap""/></StackPanel>
       <TextBlock Text=""{Binding Name}"" FontSize=""16"" FontWeight=""SemiBold"" TextWrapping=""Wrap"" Foreground=""{DynamicResource ThemeTextBrush}"" Margin=""0,8,0,0""/>
       <TextBlock Text=""{Binding TimeText}"" FontSize=""14"" FontFamily=""Bahnschrift"" Foreground=""{DynamicResource ThemeSecondaryTextBrush}"" Margin=""0,8,0,0"" TextWrapping=""Wrap""/>
       <TextBlock Text=""{Binding Status}"" FontSize=""11"" Foreground=""{DynamicResource ThemeMutedBrush}"" Margin=""0,5,0,0"" TextWrapping=""Wrap""/>";
      const string source=@"<StackPanel Orientation=""Horizontal""><TextBlock Text=""{DynamicResource Locale.028cd1c8834585ddc0803f30}"" FontSize=""10"" FontWeight=""SemiBold"" Foreground=""{DynamicResource ThemeAccentBrush}""/><Path Data=""M1,8 L8,1 M2,1 L8,1 L8,7"" Width=""8"" Height=""8"" Stretch=""Uniform"" Stroke=""{DynamicResource ThemeAccentBrush}"" StrokeThickness=""1.2"" VerticalAlignment=""Center"" Margin=""7,0,0,0""/></StackPanel>";
      const string details=@"<local:CalendarReveal IsOpen=""{Binding Details.IsExpanded}""><Border Margin=""0,9,0,0"" BorderThickness=""0,1,0,0"" BorderBrush=""{DynamicResource ThemeBorderBrush}"" Padding=""0,12,7,0""><StackPanel>
       <TextBlock Text=""{Binding PeriodLabel}"" FontSize=""9"" FontWeight=""SemiBold"" Foreground=""{DynamicResource ThemeSoftAccentBrush}""/><TextBlock Text=""{Binding PeriodText}"" Foreground=""{DynamicResource ThemeTextBrush}"" TextWrapping=""Wrap"" FontSize=""11"" Margin=""0,5,0,10""/>
       <TextBlock Text=""{Binding Details.Description}"" Visibility=""{Binding Details.DescriptionVisibility}"" Foreground=""{DynamicResource ThemeTextBrush}"" TextWrapping=""Wrap"" FontSize=""12"" LineHeight=""18""/>
       <TextBlock Text=""{DynamicResource Locale.f6e376bf5db74e983dc86da7}"" Visibility=""{Binding Details.ParticipationVisibility}"" FontSize=""9"" FontWeight=""SemiBold"" Foreground=""{DynamicResource ThemeSoftAccentBrush}"" Margin=""0,12,0,5""/><TextBlock Text=""{Binding Details.Participation}"" Visibility=""{Binding Details.ParticipationVisibility}"" Foreground=""{DynamicResource ThemeTextBrush}"" TextWrapping=""Wrap"" FontSize=""12"" LineHeight=""18""/>
       <TextBlock Text=""{Binding Details.Notice}"" Visibility=""{Binding Details.NoticeVisibility}"" Foreground=""{DynamicResource ThemeMutedBrush}"" FontSize=""11"" TextWrapping=""Wrap"" Margin=""0,5,0,0""/><Button Content=""{DynamicResource Locale.d8b8392e2c542950ca648671}"" Visibility=""{Binding Details.RetryVisibility}"" Command=""{Binding Details.Retry}"" HorizontalAlignment=""Left"" Padding=""8,6"" Margin=""0,8,0,0""/>
      </StackPanel></Border></local:CalendarReveal>";
      string actions=@"<Grid Margin=""0,7,0,0""><Grid.ColumnDefinitions><ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""*""/></Grid.ColumnDefinitions><ToggleButton Style=""{DynamicResource CalendarDetailToggle}"" Content=""{Binding Details.Label}"" IsChecked=""{Binding Details.IsExpanded,Mode=TwoWay}"" Margin=""-5,0,10,0"" AutomationProperties.Name=""{DynamicResource Locale.bbc59c7fb3a3860162e30144}""/><Button Grid.Column=""1"" Command=""{Binding OpenSource}"" CommandParameter=""{Binding SourceTarget}"" AutomationProperties.Name=""{Binding AccessibleName}"" Background=""Transparent"" HorizontalAlignment=""Left"" Padding=""0,7,6,2"">"+source+"</Button></Grid>";
      calendarCardTemplate=calendarAgendaTemplate=(DataTemplate)XamlReader.Parse(start+actions+details+"</StackPanel></DataTemplate>");
    }
    Window CreateCalendarWindow() {
      if(calendarMarkup==null) {
        string text;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("CalendarWindow.xaml"))using(var reader=new System.IO.StreamReader(stream))text=reader.ReadToEnd();
        const string opening="<Window.Resources>",closing="</Window.Resources>";int begin=text.IndexOf(opening,StringComparison.Ordinal),end=text.IndexOf(closing,StringComparison.Ordinal);
        string resources=text.Substring(begin+opening.Length,end-begin-opening.Length);
        calendarStyles=(ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:local=\"clr-namespace:EndfieldWidget;assembly=EndfieldWidget\">"+resources+"</ResourceDictionary>");
        calendarMarkup=text.Remove(begin,end+closing.Length-begin);
      }
      var created=(Window)XamlReader.Parse(calendarMarkup);created.Resources.MergedDictionaries.Add(window.Resources);created.Resources.MergedDictionaries.Add(calendarStyles);return created;
    }
  }
}
