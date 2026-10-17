using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    public void DesktopView(string reportPath) {
      window.Title="Endfield · Display test";InstallDesktopSurface();window.Show();window.UpdateLayout();
      Find<Button>("CloseButton").Click+=(s,e)=>Exit();
      window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
        // Expose only this offline diagnostic window to screenshot tools. Rendering and desktop ownership stay unchanged.
        long metadata=DesktopNative.GetStyle(desktopSurface.Handle,DesktopNative.ExtendedStyleIndex);
        desktopSurface.AllowDiagnosticActivation=true;
        DesktopNative.SetStyle(desktopSurface.Handle,DesktopNative.ExtendedStyleIndex,(metadata|DesktopNative.AppWindow)&~(DesktopNative.ToolWindow|DesktopNative.NoActivate));
        DesktopNative.Rectangle bounds;DesktopNative.GetWindowRect(desktopSurface.Handle,out bounds);
        File.WriteAllText(reportPath,"Desktop display test ready.\nVisible="+DesktopNative.IsWindowVisible(desktopSurface.Handle)+"\nDesktopOwner="+desktopSurface.IsAttached+"\nBounds="+bounds.Left+","+bounds.Top+","+bounds.Right+","+bounds.Bottom+"\nOffline preview profile; no account or network requests.\nOnly the diagnostic window exposes taskbar metadata and permits foreground activation for OS capture.\n",Encoding.UTF8);
      }));
      timer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(5)};timer.Tick+=(s,e)=>Exit();timer.Start();
    }
    public void DesktopTest(string reportPath,string imagePath) {
      window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-10000;window.Top=-10000;
      InstallDesktopSurface();window.Show();window.UpdateLayout();
      window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var report=new List<string>();Window dialog=null;
        Action<bool,string> check=(condition,label)=>{if(!condition)throw new Exception(label);report.Add("PASS: "+label);};
        try {
          IntPtr handle=desktopSurface.Handle;long style=DesktopNative.GetStyle(handle,DesktopNative.StyleIndex);
          long extended=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);
          check(desktopSurface.IsAttached && desktopSurface.Container==DesktopNative.FindContainer(),"Native window is owned by the real desktop container");
          check((style&DesktopNative.Child)==0,"WPF window preserves its original top-level rendering mode");
          bool topLevel=false;DesktopNative.EnumWindows((candidate,argument)=>{if(candidate==handle)topLevel=true;return true;},IntPtr.Zero);
          check(topLevel,"Renderer remains available for real window capture and composition");
          int widgetOrder=-1,desktopOrder=-1,order=0;
          for(IntPtr current=DesktopNative.GetTopWindow(IntPtr.Zero);current!=IntPtr.Zero && order<1000;current=DesktopNative.GetWindow(current,2),order++) {
            if(current==handle)widgetOrder=order;if(current==desktopSurface.Container)desktopOrder=order;
          }
          check(widgetOrder>=0 && desktopOrder>widgetOrder,"Desktop ownership keeps the widget above the desktop background");
          check((extended&DesktopNative.ToolWindow)!=0 && (extended&DesktopNative.AppWindow)==0,"Tool window style excludes the widget from Alt+Tab and the taskbar");
          check((extended&DesktopNative.NoActivate)!=0 && !window.Topmost && (extended&8)==0,"Desktop widget neither activates nor overlays normal application windows");
          await CheckNativeDesktopInput(check);
          DesktopNative.Rectangle initial,moved,locked;DesktopNative.GetWindowRect(handle,out initial);
          check(desktopSurface.TryUserMove(initial.Left+47,initial.Top+31),"Unlocked widget accepts movement");
          DesktopNative.GetWindowRect(handle,out moved);
          check(moved.Left==initial.Left+47 && moved.Top==initial.Top+31,"Moving the widget preserves screen coordinates");
          UserMoved();
          dialog=SettingsWindow();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-10000;dialog.Top=-10000;dialog.Show();dialog.UpdateLayout();
          check(dialog.Owner==null,"Settings use an independent focusable window above the desktop");
          long dialogExtended=DesktopNative.GetStyle(new WindowInteropHelper(dialog).Handle,DesktopNative.ExtendedStyleIndex);
          check((dialogExtended&DesktopNative.ToolWindow)!=0 && (dialogExtended&DesktopNative.NoActivate)==0,"Settings stay out of Alt+Tab while accepting keyboard focus");
          var input=(PasswordBox)dialog.FindName("TokenInput");input.Focus();Keyboard.Focus(input);
          check(input.IsKeyboardFocused,"Token editor receives keyboard focus with the widget embedded");
          await TestTextInput(input,"dummy-desktop-input");check(input.Password=="dummy-desktop-input","Token editor remains interactive after desktop attachment");input.Clear();
          var positionSwitch=(CheckBox)dialog.FindName("PositionLockSwitch");positionSwitch.IsChecked=true;positionSwitch.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
          check(config.PositionLocked && Find<Grid>("DragHandle").Cursor==Cursors.Arrow,"Settings enable the position lock and update the drag cursor");
          check(!desktopSurface.TryUserMove(moved.Left+90,moved.Top+90),"Position lock rejects movement");DesktopNative.GetWindowRect(handle,out locked);
          check(locked.Left==moved.Left && locked.Top==moved.Top,"Locked widget stays at the same screen position");
          CheckNativePersonalization(check,dialog);
          await CheckNativeStudio(check);
          var saved=Storage.Read<Config>("settings.json");var expected=desktopSurface.ScreenPosition();
          check(saved!=null && saved.PositionLocked && saved.HasPosition && Math.Abs(saved.Left-expected.X)<0.1 && Math.Abs(saved.Top-expected.Y)<0.1,"Position and lock are persisted in the isolated test profile");
          dialog.UpdateLayout();
          if(!string.IsNullOrEmpty(imagePath)) {
            var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(imagePath))encoder.Save(stream);
          }
          dialog.Close();dialog=SettingsWindow();
          check(((CheckBox)dialog.FindName("PositionLockSwitch")).IsChecked==true,"Reopening settings retains the lock state");dialog.Close();dialog=null;
          window.Hide();desktopSurface.Show();window.UpdateLayout();DesktopNative.GetWindowRect(handle,out locked);
          check(desktopSurface.IsAttached && (DesktopNative.GetStyle(handle,DesktopNative.StyleIndex)&DesktopNative.Child)==0,"Tray hide/show retains desktop ownership and the layered renderer");
          check(locked.Left==moved.Left && locked.Top==moved.Top,"Tray hide/show preserves the locked position");
          report.Add("ALL CHECKS PASSED; native desktop ownership, rendering mode, and stacking tested on this Windows installation with an isolated profile.");
        }catch(Exception error){report.Add("FAILED: "+error.ToString());Environment.ExitCode=1;}
        finally{if(dialog!=null)dialog.Close();File.WriteAllLines(reportPath,report,Encoding.UTF8);Exit();}
      }));
    }
  }
}
