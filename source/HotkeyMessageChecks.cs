using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EndfieldWidget {
  partial class Widget {
    [DllImport("user32.dll",EntryPoint="SendMessageW")]
    static extern IntPtr SendHotkeyRegressionMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);

    void CheckNativeHotkeyMessages(Action<bool,string> check,IntPtr handle) {
      IntPtr wide=IntPtr.Size==8?new IntPtr(0x0000000100004e02L):new IntPtr(int.MaxValue);
      bool handled=false;
      HotkeyMessage(handle,0x8355,wide,IntPtr.Zero,ref handled);
      check(!handled,"A non-hotkey Windows message with a pointer-sized parameter passes through without overflow");
      HotkeyMessage(handle,0x312,wide,IntPtr.Zero,ref handled);
      check(!handled,"An unknown 64-bit hotkey ID is ignored without truncating it into a configured action");
      HotkeyMessage(handle,0x312,new IntPtr(-1),IntPtr.Zero,ref handled);
      check(!handled,"An invalid signed hotkey ID cannot trigger an action or an exception");
      handled=true;HotkeyMessage(handle,0x8355,wide,IntPtr.Zero,ref handled);
      check(handled,"Passing through an unrelated message preserves another hook's handled state");

      var source=HwndSource.FromHwnd(handle);string originalMode=WidgetMode();bool originalVisibility=window.IsVisible;
      source.AddHook(HotkeyMessage);
      try {
        for(int i=0;i<2048;i++) {
          IntPtr argument=IntPtr.Size==8?new IntPtr(0x0000000100000000L+i):new IntPtr(int.MaxValue-i);
          SendHotkeyRegressionMessage(handle,(i&1)==0?0x8355:0x312,argument,IntPtr.Zero);
        }
        SendHotkeyRegressionMessage(handle,0x312,wide,IntPtr.Zero);
        check(window.IsVisible==originalVisibility && WidgetMode()==originalMode,"The actual HWND hook survives 2049 wide-parameter messages without dispatching unrelated actions");
        SendHotkeyRegressionMessage(handle,0x312,new IntPtr(ToggleHotkey),IntPtr.Zero);
        check(window.IsVisible!=originalVisibility,"A valid WM_HOTKEY still reaches the actual show/hide action through the native hook");
        SendHotkeyRegressionMessage(handle,0x312,new IntPtr(ToggleHotkey),IntPtr.Zero);
        check(window.IsVisible==originalVisibility && desktopSurface.IsAttached,"The next valid hotkey restores the widget and retains desktop ownership");
        SendHotkeyRegressionMessage(handle,0x312,new IntPtr(ModeHotkey),IntPtr.Zero);
        check(WidgetMode()!=originalMode,"A valid mode shortcut still dispatches through the same protected native hook");
      } finally {
        source.RemoveHook(HotkeyMessage);
        SetWidgetMode(originalMode);
        if(originalVisibility && !window.IsVisible)ShowWidget();else if(!originalVisibility && window.IsVisible)window.Hide();
      }
    }
  }
}
