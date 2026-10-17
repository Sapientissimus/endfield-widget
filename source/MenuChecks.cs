using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EndfieldWidget {
  partial class Widget {
    void CheckMenuInput(Action<bool,string> check) {
      check(widgetMenu.Items.OfType<MenuItem>().All(item=>string.IsNullOrEmpty(item.InputGestureText)),"The context menu contains action names without shortcut hints");
      bool oldBusy=refreshBusy,visible=window.IsVisible;var oldDialog=settingsDialog;
      try {
        refreshBusy=true;widgetMenu.IsOpen=false;window.Show();
        foreach(var key in new[]{Key.F5,Key.OemComma,Key.Escape}) {
          var input=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,key){RoutedEvent=Keyboard.PreviewKeyDownEvent};
          window.RaiseEvent(input);
          check(!input.Handled && window.IsVisible && settingsDialog==oldDialog && !widgetMenu.IsOpen,"A local key does not refresh, hide, open settings or open the widget menu: "+key);
        }
        var hide=widgetMenu.Items.OfType<MenuItem>().Single(item=>object.Equals(item.Header,"Hide"));
        hide.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(!window.IsVisible,"The menu Hide action still works through its actual click handler");
      }finally {refreshBusy=oldBusy;if(visible)window.Show();else window.Hide();}
    }
  }
}
