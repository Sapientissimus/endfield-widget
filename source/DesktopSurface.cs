using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  static class DesktopNative {
    public const int StyleIndex=-16,ExtendedStyleIndex=-20,OwnerIndex=-8;
    public const long Child=0x40000000,Popup=0x80000000,ToolWindow=0x80,AppWindow=0x40000,NoActivate=0x08000000;
    public const long Transparent=0x20,Layered=0x80000;
    public const uint NoSize=1,NoMove=2,NoZOrder=4,NoActivatePosition=16,FrameChanged=32,NoOwnerZOrder=512;
    public delegate bool EnumCallback(IntPtr window,IntPtr argument);
    [StructLayout(LayoutKind.Sequential)] public struct Rectangle {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] public struct PixelPoint {public int X,Y;public PixelPoint(int x,int y){X=x;Y=y;}}
    [StructLayout(LayoutKind.Sequential)] public struct WindowPosition {public IntPtr Window,After;public int X,Y,Width,Height;public uint Flags;}
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumCallback callback,IntPtr argument);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rectangle rectangle);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rectangle rectangle);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr window,ref PixelPoint point);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLong",SetLastError=true)] static extern int GetWindowLong32(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtr",SetLastError=true)] static extern IntPtr GetWindowLong64(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLong",SetLastError=true)] static extern int SetWindowLong32(IntPtr window,int index,int value);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtr",SetLastError=true)] static extern IntPtr SetWindowLong64(IntPtr window,int index,IntPtr value);
    public static long GetStyle(IntPtr window,int index){return IntPtr.Size==8?GetWindowLong64(window,index).ToInt64():unchecked((uint)GetWindowLong32(window,index));}
    public static void SetStyle(IntPtr window,int index,long value){if(IntPtr.Size==8)SetWindowLong64(window,index,new IntPtr(value));else SetWindowLong32(window,index,unchecked((int)value));}
    public static IntPtr Owner(IntPtr window){return GetWindow(window,4);}
    public static IntPtr FindContainer() {
      IntPtr desktop=IntPtr.Zero;
      EnumWindows((candidate,argument)=>{
        // Use the existing icon layer; no wallpaper windows need to be created or hidden.
        var icons=FindWindowEx(candidate,IntPtr.Zero,"SHELLDLL_DefView",null);Rectangle bounds;
        if(icons!=IntPtr.Zero && IsWindowVisible(candidate) && GetClientRect(candidate,out bounds) && bounds.Right-bounds.Left>100 && bounds.Bottom-bounds.Top>100){desktop=candidate;return false;}
        return true;
      },IntPtr.Zero);
      return desktop;
    }
    public static void HideFromSwitcher(Window window,bool noActivate) {
      Action apply=()=>{
        IntPtr handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
        long style=GetStyle(handle,ExtendedStyleIndex);style=(style|ToolWindow)&~AppWindow;
        if(noActivate)style|=NoActivate;
        SetStyle(handle,ExtendedStyleIndex,style);
        SetWindowPos(handle,IntPtr.Zero,0,0,0,0,NoMove|NoSize|NoZOrder|NoActivatePosition|FrameChanged);
      };
      window.SourceInitialized+=(s,e)=>apply();window.Loaded+=(s,e)=>apply();
      if(new WindowInteropHelper(window).Handle!=IntPtr.Zero)apply();
    }
  }
  sealed class DesktopSurface : IDisposable {
    readonly Window window;
    readonly Func<bool> isLocked;
    readonly Action moved;
    readonly DispatcherTimer recovery;
    HwndSource source;
    IntPtr handle,container;
    FrameworkElement dragHandle;
    readonly List<FrameworkElement> dragHandles=new List<FrameworkElement>();
    public Func<Rect,Point> SnapPosition {get;set;}
    bool dragging,disposed,clickThrough;
    Point pointerStart;
    DesktopNative.Rectangle dragStart;
    public bool IsAttached {get{return handle!=IntPtr.Zero && container!=IntPtr.Zero && DesktopNative.IsWindow(container) && DesktopNative.Owner(handle)==container;}}
    public IntPtr Handle {get{return handle;}}
    public IntPtr Container {get{return container;}}
    public bool AllowDiagnosticActivation {get;set;}
    public DesktopSurface(Window target,Func<bool> locked,Action positionChanged) {
      window=target;isLocked=locked;moved=positionChanged;window.ShowActivated=false;window.Topmost=false;
      DesktopNative.HideFromSwitcher(window,true);
      window.SourceInitialized+=(s,e)=>Initialize();window.Loaded+=(s,e)=>TryAttach();window.Closed+=(s,e)=>Dispose();
      recovery=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromSeconds(5)};
      recovery.Tick+=(s,e)=>{if(!disposed && DesktopNative.IsWindow(handle) && !IsAttached)TryAttach();};recovery.Start();
      if(new WindowInteropHelper(window).Handle!=IntPtr.Zero){Initialize();TryAttach();}
    }
    void Initialize() {
      handle=new WindowInteropHelper(window).Handle;source=HwndSource.FromHwnd(handle);if(source!=null)source.AddHook(Messages);
      ApplyClickThrough();
    }
    public void SetClickThrough(bool enabled) {clickThrough=enabled;if(enabled && dragging)FinishDrag();window.IsHitTestVisible=!enabled;ApplyClickThrough();UpdateLock();}
    void ApplyClickThrough() {
      if(handle==IntPtr.Zero)return;
      long style=DesktopNative.GetStyle(handle,DesktopNative.ExtendedStyleIndex);
      DesktopNative.SetStyle(handle,DesktopNative.ExtendedStyleIndex,clickThrough?style|DesktopNative.Transparent:style&~DesktopNative.Transparent);
      DesktopNative.SetWindowPos(handle,IntPtr.Zero,0,0,0,0,DesktopNative.NoMove|DesktopNative.NoSize|DesktopNative.NoZOrder|DesktopNative.NoActivatePosition|DesktopNative.FrameChanged);
    }
    IntPtr Messages(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled) {
      if(message==0x21 && !AllowDiagnosticActivation){handled=true;return new IntPtr(3);} // MA_NOACTIVATE: buttons still receive the click.
      if(message==0x112 && (wParam.ToInt64()&0xfff0)==0xf010 && isLocked()){handled=true;return IntPtr.Zero;}
      if(message==0x46 && container!=IntPtr.Zero && lParam!=IntPtr.Zero) {
        var position=(DesktopNative.WindowPosition)Marshal.PtrToStructure(lParam,typeof(DesktopNative.WindowPosition));
        if((position.Flags&DesktopNative.NoZOrder)==0){if(!AllowDiagnosticActivation){position.After=new IntPtr(1);position.Flags|=DesktopNative.NoActivatePosition;}position.Flags|=DesktopNative.NoOwnerZOrder;Marshal.StructureToPtr(position,lParam,false);}
      }
      return IntPtr.Zero;
    }
    public bool TryAttach() {
      if(disposed || handle==IntPtr.Zero || !DesktopNative.IsWindow(handle))return false;
      IntPtr desktop=DesktopNative.FindContainer();if(desktop==IntPtr.Zero)return false;
      // Preserve the top-level layered WPF renderer. Only the desktop ownership and Z order change.
      if((DesktopNative.GetStyle(handle,DesktopNative.StyleIndex)&DesktopNative.Child)!=0)return false;
      DesktopNative.SetStyle(handle,DesktopNative.OwnerIndex,desktop.ToInt64());
      if(DesktopNative.Owner(handle)!=desktop)return false;
      container=desktop;
      ApplyClickThrough();
      KeepAtDesktopLayer();
      return true;
    }
    public Point ScreenPosition() {
      DesktopNative.Rectangle rectangle;
      if(handle==IntPtr.Zero || !DesktopNative.GetWindowRect(handle,out rectangle))return new Point(window.Left,window.Top);
      var point=new Point(rectangle.Left,rectangle.Top);
      return source!=null && source.CompositionTarget!=null?source.CompositionTarget.TransformFromDevice.Transform(point):point;
    }
    bool MoveScreen(int left,int top,bool frame) {
      var point=new DesktopNative.PixelPoint(left,top);
      return DesktopNative.SetWindowPos(handle,IntPtr.Zero,point.X,point.Y,0,0,DesktopNative.NoSize|DesktopNative.NoZOrder|DesktopNative.NoActivatePosition|(frame?DesktopNative.FrameChanged:0));
    }
    void KeepAtDesktopLayer(){DesktopNative.SetWindowPos(handle,new IntPtr(1),0,0,0,0,DesktopNative.NoMove|DesktopNative.NoSize|DesktopNative.NoActivatePosition|DesktopNative.NoOwnerZOrder|DesktopNative.FrameChanged);}
    public bool TryUserMove(int left,int top) {
      if(isLocked() || clickThrough || handle==IntPtr.Zero)return false;
      DesktopNative.Rectangle bounds;
      if(SnapPosition!=null && DesktopNative.GetWindowRect(handle,out bounds)){var point=SnapPosition(new Rect(left,top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top));left=(int)Math.Round(point.X);top=(int)Math.Round(point.Y);}
      return MoveScreen(left,top,false);
    }
    public void BindDrag(FrameworkElement element) {
      dragHandles.Add(element);UpdateLock();
      element.MouseLeftButtonDown+=(s,e)=>{
        dragHandle=element;
        if(isLocked() || clickThrough || !(e.OriginalSource is DependencyObject) || IsControl((DependencyObject)e.OriginalSource))return;
        if(!DesktopNative.GetWindowRect(handle,out dragStart))return;
        pointerStart=window.PointToScreen(e.GetPosition(window));dragging=element.CaptureMouse();e.Handled=dragging;
      };
      element.MouseMove+=(s,e)=>{
        if(!dragging)return;if(isLocked() || e.LeftButton!=MouseButtonState.Pressed){FinishDrag();return;}
        Point pointer=window.PointToScreen(e.GetPosition(window));
        TryUserMove(dragStart.Left+(int)Math.Round(pointer.X-pointerStart.X),dragStart.Top+(int)Math.Round(pointer.Y-pointerStart.Y));e.Handled=true;
      };
      element.MouseLeftButtonUp+=(s,e)=>{if(dragging){FinishDrag();e.Handled=true;}};
      element.LostMouseCapture+=(s,e)=>{if(dragging){dragging=false;moved();}};
    }
    bool IsControl(DependencyObject element) {
      for(var current=element;current!=null && current!=dragHandle;current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current)) {
        if(current is ButtonBase)return true;
        var text=current as TextBlock;if(text!=null && text.Cursor==Cursors.Hand)return true;
      }
      return false;
    }
    void FinishDrag(){dragging=false;if(dragHandle!=null)dragHandle.ReleaseMouseCapture();moved();}
    public void UpdateLock(){foreach(var element in dragHandles)element.Cursor=isLocked() || clickThrough?Cursors.Arrow:Cursors.SizeAll;if((isLocked() || clickThrough) && dragging)FinishDrag();}
    public void Show(){window.Show();if(!IsAttached)TryAttach();if(handle!=IntPtr.Zero)KeepAtDesktopLayer();}
    public void Dispose(){if(disposed)return;disposed=true;recovery.Stop();if(source!=null && !source.IsDisposed)source.RemoveHook(Messages);}
  }
}
