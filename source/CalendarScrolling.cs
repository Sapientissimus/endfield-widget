using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EndfieldWidget {
  static class CalendarScrolling {
    public static void Attach(ListBox list) {list.PreviewMouseWheel+=ForwardAtBoundary;}

    static void ForwardAtBoundary(object sender,MouseWheelEventArgs e) {
      var list=(ListBox)sender;var scroll=FindScroll(list);
      if(scroll==null || e.Delta==0 || CanScroll(scroll,e.Delta))return;
      // A short/narrow calendar has an outer viewport. Let the same wheel
      // gesture continue there only after the card list reaches its end.
      for(var parent=VisualTreeHelper.GetParent(list);parent!=null;parent=VisualTreeHelper.GetParent(parent)) {
        var outer=parent as ScrollViewer;
        if(outer==null || !CanScroll(outer,e.Delta))continue;
        e.Handled=true;
        outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice,e.Timestamp,e.Delta){RoutedEvent=Mouse.MouseWheelEvent,Source=outer});
        return;
      }
    }
    static bool CanScroll(ScrollViewer scroll,int delta) {
      return delta>0?scroll.VerticalOffset>0.5:scroll.VerticalOffset<scroll.ScrollableHeight-0.5;
    }
    static ScrollViewer FindScroll(DependencyObject root) {
      var scroll=root as ScrollViewer;if(scroll!=null)return scroll;
      for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
        scroll=FindScroll(VisualTreeHelper.GetChild(root,i));if(scroll!=null)return scroll;
      }
      return null;
    }
  }
}
