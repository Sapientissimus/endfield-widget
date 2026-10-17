using System;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckNativeStudio(Action<bool,string> check) {
      var previous=Json.Encode(config);bool oldDiagnostic=diagnosticView;var handle=desktopSurface.Handle;
      DesktopNative.Rectangle original;DesktopNative.GetWindowRect(handle,out original);
      try {
        config.TextScale=1;config.MicroScale=1;config.PositionLocked=false;desktopSurface.UpdateLock();SetWidgetMode("micro");window.UpdateLayout();
        long flags=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);
        check(desktopSurface.IsAttached && (flags&DesktopNative.ToolWindow)!=0 && (flags&DesktopNative.AppWindow)==0 && (flags&DesktopNative.NoActivate)!=0,"The ultra-small native window retains desktop ownership and remains outside Alt+Tab");
        DesktopNative.Rectangle start,moved;DesktopNative.GetWindowRect(handle,out start);
        check(desktopSurface.TryUserMove(start.Left-29,start.Top+35),"The ultra-small drag surface accepts unlocked native movement");DesktopNative.GetWindowRect(handle,out moved);
        check(moved.Left!=start.Left || moved.Top!=start.Top,"The ultra-small view actually changes its screen coordinates");
        config.PositionLocked=true;desktopSurface.UpdateLock();check(!desktopSurface.TryUserMove(start.Left,start.Top),"The existing position lock also protects the ultra-small view");
        desktopSurface.SetClickThrough(true);check((DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex)&DesktopNative.Transparent)!=0,"Ultra-small mode preserves native click-through support");desktopSurface.SetClickThrough(false);
        diagnosticView=true;config.AnimateTransitions=true;SetWidgetMode("full");await Task.Delay(300);window.UpdateLayout();
        flags=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);check(window.Width>0 && window.Height>0 && !window.HasAnimatedProperties && desktopSurface.IsAttached && (flags&DesktopNative.NoActivate)!=0,"A real animated mode transition completes without changing the desktop layer or activation flags");
        int first=0x5e21,second=0x5e22;
        var native=new ShortcutRegistry((id,m,k)=>RegisterHotKey(handle,id,m,k),id=>UnregisterHotKey(handle,id));
        try {
          var spec=TestShortcut(first,"Ctrl+Alt+Shift+F23");native.Initial(new[]{spec});
          if(native.Active.Count==0)native.Initial(new[]{TestShortcut(first,"Ctrl+Alt+Shift+F24")});
          check(native.Active.Count==1,"A configured shortcut registers against the real widget HWND");
          var active=native.Active[first];check(!RegisterHotKey(handle,second,active.Modifiers|0x4000,active.Key),"Windows rejects a duplicate native chord while the working registration stays active");
        }finally {native.Clear();UnregisterHotKey(handle,second);}
        CheckNativeHotkeyMessages(check,handle);
        await CheckNativeContinuity(check);
      }finally {
        diagnosticView=oldDiagnostic;var restored=new JavaScriptSerializer().Deserialize<Config>(previous);foreach(var property in typeof(Config).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        foreach(var property in new[]{Window.WidthProperty,Window.HeightProperty,Window.LeftProperty,Window.TopProperty})window.BeginAnimation(property,null);
        ApplyTextScale();ApplyLayout(true);desktopSurface.SetClickThrough(config.ClickThrough);desktopSurface.UpdateLock();
        DesktopNative.SetWindowPos(handle,IntPtr.Zero,original.Left,original.Top,0,0,DesktopNative.NoSize|DesktopNative.NoZOrder|DesktopNative.NoActivatePosition);UserMoved();
      }
    }
  }
}
