using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace EndfieldWidget {
  static class GameWindow {
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window,int command);
    public static DateTimeOffset? Started() {
      Process[] games=null;try {
        games=Process.GetProcessesByName("Endfield");int session;using(var self=Process.GetCurrentProcess())session=self.SessionId;
        DateTimeOffset? earliest=null;foreach(var p in games)try{if(p.SessionId==session){var start=new DateTimeOffset(p.StartTime.ToUniversalTime());if(!earliest.HasValue || start<earliest)earliest=start;}}catch{}
        return earliest;
      }catch{return null;}finally{if(games!=null)foreach(var p in games)p.Dispose();}
    }
    public static bool Focus() {
      Process[] games=null;try {
        games=Process.GetProcessesByName("Endfield");int session;using(var self=Process.GetCurrentProcess())session=self.SessionId;
        foreach(var p in games)try{IntPtr handle=p.MainWindowHandle;if(p.SessionId==session && handle!=IntPtr.Zero && DesktopNative.IsWindowVisible(handle)){if(IsIconic(handle))ShowWindowAsync(handle,9);return SetForegroundWindow(handle);}}catch{}
        return false;
      }catch{return false;}finally{if(games!=null)foreach(var p in games)p.Dispose();}
    }
  }
  partial class Widget {
    Func<DateTimeOffset?> gameStartProbe;
    Func<bool> gameFocuser;
    DateTimeOffset? gameStartedAt;
    bool approximateSession;
    void ObserveSession(bool wasRunning,DateTimeOffset now) {
      if(!gameActivity.Running){gameStartedAt=null;return;}
      DateTimeOffset? actual=null;try{actual=gameStartProbe==null?null:gameStartProbe();}catch{}
      if(actual.HasValue && actual.Value<=now){gameStartedAt=actual;approximateSession=false;}
      else if(!wasRunning || !gameStartedAt.HasValue){gameStartedAt=now;approximateSession=true;}
    }
    string SessionElapsed(DateTimeOffset now) {
      if(!gameStartedAt.HasValue)return "";var span=now-gameStartedAt.Value;if(span<TimeSpan.Zero)span=TimeSpan.Zero;
      return (approximateSession?"≈":"")+(span.TotalHours>=1?((int)span.TotalHours).ToString()+":"+span.Minutes.ToString("00")+":"+span.Seconds.ToString("00"):((int)span.TotalMinutes).ToString("00")+":"+span.Seconds.ToString("00"));
    }
    void PaintSession(DateTimeOffset now) {
      string elapsed=SessionElapsed(now);bool show=config.ShowSessionTime && gameActivity.Running && elapsed.Length>0;
      foreach(string name in new[]{"SessionLabel","CompactSessionLabel"}){var text=Find<TextBlock>(name);text.Text=L.T("In game · ")+elapsed;text.Visibility=show?Visibility.Visible:Visibility.Collapsed;}
      if(gameActivity.Running && gameLaunchError.Length==0) {
        string hint=L.T("Return to game")+(show?" · "+elapsed:"");
        foreach(string name in new[]{"PlayButton","CompactPlayButton","MicroPlayButton"})Find<Button>(name).ToolTip=hint;
        if(trayGamePlay!=null)trayGamePlay.ToolTipText=hint;
      }
    }
  }
}
