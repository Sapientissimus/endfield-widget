using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    static CalendarDetailRecord CalendarLongScrollDetail(CalendarEntry entry) {
      return new CalendarDetailRecord{Key=CalendarDetailsData.Key(entry),Source=entry.Source,Checked=DateTimeOffset.UtcNow,
        Description=string.Join("\n",Enumerable.Range(1,32).Select(i=>"Line "+i+". Detailed event description."))+"\nEnd of description.",
        Participation="Complete the specified objective.\nRead the participation requirements to the end.\nEnd of participation requirements."};
    }
    async Task CalendarScrollLayout() {
      calendarDialog.UpdateLayout();await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);calendarDialog.UpdateLayout();
    }
    static void CalendarWheel(UIElement source,int delta) {
      var preview=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent,Source=source};source.RaiseEvent(preview);
      if(!preview.Handled)source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=Mouse.MouseWheelEvent,Source=source});
    }
    async Task CheckCalendarScrolling(Action<bool,string> check,string folder) {
      string directory=Storage.DirectoryPath;var oldNow=calendarNow;var oldFeed=calendarFeed;var oldBanners=bannerState;var oldSchedule=schedule;var oldCache=calendarDetailCache;var oldMotion=CalendarMotion.Override;var oldZone=Clock.ZoneOverride;var oldBounds=calendarLastBounds;
      try {
        Storage.DirectoryPath=Path.Combine(folder,"scroll-isolated-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Storage.DirectoryPath);
        Clock.ZoneOverride=()=>TimeZoneInfo.Utc;CalendarMotion.Override=false;
        var now=new DateTimeOffset(2029,3,1,12,0,0,TimeSpan.Zero);var end=now.AddDays(4);
        calendarNow=()=>now;calendarFeed=null;schedule=null;bannerState=new BannerState{GameEvents=new List<GameEvent>{CalendarEvent("Event with a long description","81001",now.AddDays(-1),now.AddDays(1),end)}};
        calendarRemembered=false;calendarQuery="";calendarFilterMask=15;calendarDetailCache=new Dictionary<string,CalendarDetailRecord>();calendarLastBounds=null;
        var pending=new TaskCompletionSource<CalendarDetailRecord>();calendarDetailFetchOverride=e=>pending.Task;
        ShowCalendar();calendarDialog.Width=960;calendarDialog.Height=790;SelectCalendarDay(end.Date);await CalendarScrollLayout();
        check(calendarDialog.ResizeMode==ResizeMode.CanResize && CalendarVisualElements<ResizeGrip>(calendarDialog).Count(g=>g.IsVisible)==1 && CalendarFind<ResizeGrip>("CalendarResizeGrip").IsVisible,"The calendar shows one resize grip instead of overlapping native-template and custom grips");
        DesktopNative.Rectangle bounds;var handle=new WindowInteropHelper(calendarDialog).Handle;
        DesktopNative.GetWindowRect(handle,out bounds);int packed=((bounds.Bottom-3)<<16)|((bounds.Right-3)&0xffff);
        check(CalendarNativeMessage(handle,0x84,IntPtr.Zero,new IntPtr(packed)).ToInt32()==17,"The single calendar corner still exposes the native bottom-right resize hit target");
        var day=calendarDayModels.Single();day.Details.IsExpanded=true;await CalendarScrollLayout();
        check(day.Details.Busy,"The scroll regression starts with the real asynchronous expanded-card loading state");
        pending.SetResult(CalendarLongScrollDetail(day.Entry));await Task.Delay(60);await CalendarScrollLayout();
        check(!day.Details.Busy && day.Details.Description.Contains("End of description."),"A description arriving after expansion updates the scrollable card");
        foreach(string name in new[]{"CalendarEvents","CalendarAgenda"}) {
          var list=CalendarFind<ListBox>(name);var scroll=FindCalendarVisual<ScrollViewer>(list);
          check(ScrollViewer.GetCanContentScroll(list) && VirtualizingPanel.GetIsVirtualizing(list) && VirtualizingPanel.GetScrollUnit(list)==ScrollUnit.Pixel,name+" keeps recycling virtualization with pixel scrolling");
          var reveal=CalendarVisualElements<CalendarReveal>(list).First();
          check(reveal.ActualHeight>scroll.ViewportHeight && !CalendarVisualElements<ScrollViewer>(reveal).Any(),name+" flows an oversized description into the list without an inner scrollbar or height cap");
          scroll.ScrollToTop();await CalendarScrollLayout();var text=CalendarVisualElements<TextBlock>(list).First(t=>t.Text==day.Name);
          CalendarWheel(text,-120);await CalendarScrollLayout();
          check(scroll.VerticalOffset>0 && scroll.VerticalOffset<reveal.ActualHeight,name+" wheel scrolling moves inside one tall card instead of skipping the entire item");
          scroll.ScrollToVerticalOffset(197);await CalendarScrollLayout();
          check(Math.Abs(scroll.VerticalOffset-197)<1,name+" supports a precise intermediate position inside one expanded card");
          scroll.ScrollToBottom();await CalendarScrollLayout();
          var tail=CalendarVisualElements<TextBlock>(list).First(t=>t.Text==day.Details.Participation);var tailBounds=tail.TransformToAncestor(scroll).TransformBounds(new Rect(tail.RenderSize));
          check(scroll.ScrollableHeight>400 && Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<1 && tailBounds.Bottom<=scroll.ActualHeight+2 && tailBounds.Bottom>0,name+" reaches the final participation line at the end of the full description");
          SaveImage(calendarDialog,Path.Combine(folder,name+"-details-bottom.png"));
          scroll.ScrollToTop();await CalendarScrollLayout();
        }
        CalendarMotion.Override=true;day.Details.IsExpanded=false;await Task.Delay(180);day.Details.IsExpanded=true;await Task.Delay(220);await CalendarScrollLayout();
        var dayScroll=CalendarDayScroller();check(dayScroll.ScrollableHeight>400,"Animated expansion also recalculates the full description's scroll extent");
        CalendarMotion.Override=false;dayScroll.ScrollToBottom();await CalendarScrollLayout();day.Details.IsExpanded=false;await CalendarScrollLayout();
        check(dayScroll.VerticalOffset<=dayScroll.ScrollableHeight+1 && dayScroll.ScrollableHeight<60,"Collapsing a tall card clamps the offset without leaving a blank viewport");
        day.Details.IsExpanded=true;await CalendarScrollLayout();bannerState.GameEvents.Add(CalendarEvent("Another event","81002",now.AddDays(-1),now.AddDays(1),end));RenderCalendar();await CalendarScrollLayout();
        dayScroll=CalendarDayScroller();dayScroll.ScrollToBottom();await CalendarScrollLayout();
        check(calendarDayModels.Count==2 && CalendarVisualElements<TextBlock>(CalendarFind<ListBox>("CalendarEvents")).Any(t=>t.Text=="Another event"),"Scrolling past an expanded first card reaches the next event on the selected day");
        calendarDialog.Width=420;calendarDialog.Height=400;await CalendarScrollLayout();LayoutCalendar();await CalendarScrollLayout();
        var body=CalendarFind<ScrollViewer>("CalendarBodyScroll");var listDay=CalendarFind<ListBox>("CalendarEvents");dayScroll=CalendarDayScroller();var wheelSurface=CalendarVisualElements<ScrollContentPresenter>(listDay).First();body.ScrollToTop();dayScroll.ScrollToTop();await CalendarScrollLayout();
        CalendarWheel(wheelSurface,-120);await CalendarScrollLayout();
        check(dayScroll.VerticalOffset>0 && body.VerticalOffset==0,"In a small window the card consumes the wheel while its own text can scroll");
        dayScroll.ScrollToBottom();await CalendarScrollLayout();CalendarWheel(wheelSurface,-120);await CalendarScrollLayout();
        check(body.VerticalOffset>0 && Math.Abs(dayScroll.VerticalOffset-dayScroll.ScrollableHeight)<1,"At the card-list end the same wheel gesture continues down the small calendar body");
        body.ScrollToVerticalOffset(120);dayScroll.ScrollToTop();await CalendarScrollLayout();double before=body.VerticalOffset;CalendarWheel(wheelSurface,120);await CalendarScrollLayout();
        check(body.VerticalOffset<before && dayScroll.VerticalOffset==0,"At the card-list beginning an upward wheel gesture returns to the calendar above");
        dayScroll.ScrollToBottom();body.ScrollToVerticalOffset(220);await CalendarScrollLayout();SaveImage(calendarDialog,Path.Combine(folder,"calendar-small-details-scroll.png"));
      }finally {
        if(calendarDialog!=null)calendarDialog.Close();calendarDetailFetchOverride=null;calendarDetailCache=oldCache;Storage.DirectoryPath=directory;calendarNow=oldNow;calendarFeed=oldFeed;bannerState=oldBanners;schedule=oldSchedule;CalendarMotion.Override=oldMotion;Clock.ZoneOverride=oldZone;calendarLastBounds=oldBounds;calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;calendarQuery="";calendarFilterMask=15;
      }
    }
  }
}
