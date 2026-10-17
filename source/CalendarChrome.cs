using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace EndfieldWidget {
  static class CalendarChrome {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    public static void Apply(Window window) {
      // The calendar is an ordinary focusable window. Only the desktop widget
      // needs per-pixel layering; keep its ownership/rendering path unchanged.
      WindowChrome.SetWindowChrome(window,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(12),UseAeroCaptionButtons=false});
      window.SourceInitialized+=(s,e)=>{
        var handle=new WindowInteropHelper(window).Handle;
        try{int rounded=2,dark=1;DwmSetWindowAttribute(handle,33,ref rounded,4);DwmSetWindowAttribute(handle,20,ref dark,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
      };
    }
  }
}
