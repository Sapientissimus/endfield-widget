using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace EndfieldWidget {
  partial class Widget {
    [DllImport("user32.dll")] static extern IntPtr GetCapture();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr handle,uint flags);
    async Task CheckNativeDesktopInput(Action<bool,string> check) {
      IntPtr desktop=desktopSurface.Container,main=desktopSurface.Handle;
      check(Storage.Isolated,"Desktop input checks use an isolated account and preferences");
      check(DesktopNative.IsWindowEnabled(desktop),"The real Explorer desktop accepts input before opening settings");
      try {
        foreach(string buttonName in new[]{"SettingsButton","CompactSettingsButton","MicroSettingsButton"}) {
          // Use the actual user-facing handler, rather than bypassing it with
          // SettingsWindow().Show(), which previously missed the modal path.
          Find<Button>(buttonName).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
          var dialog=settingsDialog;check(dialog!=null && dialog.IsVisible,"The actual "+buttonName+" action returns with visible modeless settings");
          dialog.Left=-10000;dialog.Top=-10000;dialog.UpdateLayout();
          IntPtr native=new WindowInteropHelper(dialog).Handle;
          check(DesktopNative.IsWindowEnabled(desktop) && DesktopNative.IsWindowEnabled(main),"Explorer and the widget stay enabled while "+buttonName+" settings are open");
          check(dialog.Owner==null && GetAncestor(native,3)!=desktop && GetAncestor(native,3)!=main,"Settings have no native ownership chain back to Explorer or the desktop widget");
          var input=(PasswordBox)dialog.FindName("TokenInput");input.Focus();System.Windows.Input.Keyboard.Focus(input);
          await TestTextInput(input,"isolated-modeless-input");check(input.IsKeyboardFocused && input.Password=="isolated-modeless-input","Modeless settings retain interactive keyboard input");input.Clear();
          Settings();check(settingsDialog==dialog,"Repeated settings requests reuse the existing window");
          ((Button)dialog.FindName(buttonName=="CompactSettingsButton"?"SettingsDoneButton":"SettingsCloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
          check(settingsDialog==null && DesktopNative.IsWindowEnabled(desktop) && DesktopNative.IsWindowEnabled(main),"Closing settings clears the tracked window and preserves desktop input");
        }
        widgetMenu.IsOpen=true;
        Settings();await Task.Delay(200);
        check(!widgetMenu.IsOpen && GetCapture()==IntPtr.Zero,"Opening settings releases the widget context menu and its native mouse capture");
        settingsDialog.Left=-10000;settingsDialog.Top=-10000;settingsDialog.Close();
        for(int cycle=0;cycle<8;cycle++){Settings();settingsDialog.Left=-10000;settingsDialog.Top=-10000;settingsDialog.Close();}
        check(settingsDialog==null && DesktopNative.IsWindowEnabled(desktop) && DesktopNative.IsWindowEnabled(main) && GetCapture()==IntPtr.Zero,"Repeated real settings open/close cycles leave Explorer enabled and no mouse capture");
        window.Hide();desktopSurface.Show();window.UpdateLayout();
        check(desktopSurface.IsAttached && DesktopNative.IsWindowEnabled(desktop),"Hide/show after settings preserves desktop ownership and Explorer input");
      }finally {
        if(settingsDialog!=null){settingsDialog.Close();settingsDialog=null;}
        widgetMenu.IsOpen=false;
      }
    }
  }
}
