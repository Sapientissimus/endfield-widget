using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EndfieldWidget {
  public class NotificationRecord {public string Key {get;set;} public DateTimeOffset Sent {get;set;} public int SanityValue {get;set;} public int SanityMaximum {get;set;} public long SanityFullAt {get;set;} }
  public class NotificationHistory {public List<NotificationRecord> Sent {get;set;} public NotificationHistory(){Sent=new List<NotificationRecord>();}}
  sealed class WidgetNotice {public string Key,Title,Message;public int SanityValue,SanityMaximum;public long SanityFullAt;}
  sealed class NotificationPolicy {
    public readonly NotificationHistory History;
    public NotificationPolicy(NotificationHistory saved){History=saved??new NotificationHistory();History.Sent=(History.Sent??new List<NotificationRecord>()).Where(n=>n!=null && !string.IsNullOrEmpty(n.Key) && n.Key.Length<=600).Take(4096).ToList();}
    static bool Fresh(DateTimeOffset checkedAt,DateTimeOffset now,double minutes){return checkedAt<=now && (now-checkedAt).TotalMinutes<=minutes;}
    bool Already(string key){return History.Sent.Any(n=>n.Key==key);}
    public WidgetNotice Next(Config config,Sanity sanity,Schedule schedule,DateTimeOffset now,bool liveSanity,bool liveNews,bool connected) {
      if(connected && liveSanity && sanity!=null && sanity.Maximum>0 && sanity.Current>=0 && sanity.RoleId==config.RoleId && Fresh(sanity.Checked,now,10)) {
        string key="sanity:"+sanity.RoleId;
        int value=sanity.Estimate(now);
        var fullAt=sanity.LocalFullAt();double remaining=fullAt.HasValue?(fullAt.Value-now).TotalMinutes:double.NaN;
        bool timed=config.SanityNoticeMode=="minutes";
        var last=History.Sent.LastOrDefault(n=>n.Key==key);
        // Rearm on a fresh recovery cycle, independently of edits to notification preferences.
        bool rearm=last!=null && (last.SanityMaximum>0?
          (sanity.Maximum==last.SanityMaximum && value<last.SanityValue-Math.Max(1,sanity.Maximum*.05)) || (last.SanityFullAt>0 && fullAt.HasValue && fullAt.Value.ToUnixTimeSeconds()>last.SanityFullAt+300):
          value<sanity.Maximum*.85);
        if(rearm)History.Sent.RemoveAll(n=>n.Key==key);
        bool due=timed?value>=sanity.Maximum || (remaining>=0 && remaining<=config.SanityLeadMinutes):value>=sanity.Maximum*config.SanityThreshold/100.0;
        if(config.NotifySanity && due && !Already(key))return new WidgetNotice {Key=key,SanityValue=value,SanityMaximum=sanity.Maximum,SanityFullAt=fullAt.HasValue?fullAt.Value.ToUnixTimeSeconds():0,Title=value>=sanity.Maximum?L.T("Sanity is fully restored"):timed?L.T("Sanity will recover soon"):L.T("Sanity — ")+Math.Round(value*100.0/sanity.Maximum)+"%",Message=value+" / "+sanity.Maximum+(timed && value<sanity.Maximum && fullAt.HasValue?L.T(" · full Sanity recovery in ")+Clock.After(fullAt.Value-now):"")};
      }
      if(config.NotifyMaintenance && liveNews && schedule!=null && !schedule.Bundled && Fresh(schedule.Checked,now,60) && now<schedule.Start && (schedule.Start-now).TotalMinutes<=config.MaintenanceLeadMinutes && schedule.End>schedule.Start && schedule.Source!=null && schedule.Source.StartsWith(NewsClient.NewsUrl+"/",StringComparison.Ordinal)) {
        string key="maintenance:"+schedule.Start.ToUniversalTime().Ticks;
        if(!Already(key))return new WidgetNotice {Key=key,Title=L.T("Maintenance soon"),Message=schedule.Version+" · "+Clock.Date(schedule.Start)+", "+Clock.Time(schedule.Start)+"–"+Clock.Time(schedule.End)+""};
      }
      return null;
    }
    public bool Deliver(WidgetNotice notice,DateTimeOffset now,bool quiet,Func<WidgetNotice,bool> send) {
      if(notice==null || quiet || Already(notice.Key) || !send(notice))return false;
      History.Sent.Add(new NotificationRecord {Key=notice.Key,Sent=now,SanityValue=notice.SanityValue,SanityMaximum=notice.SanityMaximum,SanityFullAt=notice.SanityFullAt});
      // Sanity cycle markers stay until a fresh low reading rearms them.
      History.Sent.RemoveAll(n=>n.Key.StartsWith("maintenance:",StringComparison.Ordinal) && (now-n.Sent).TotalDays>90);
      return true;
    }
  }
  static class QuietState {
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    public static bool ShouldPause(bool detectFullscreen,IntPtr widget,IntPtr settings) {
      int state;
      try {if(SHQueryUserNotificationState(out state)!=0 || state!=5)return true;}catch{return true;}
      if(!detectFullscreen)return false;
      IntPtr active=GetForegroundWindow();if(active==IntPtr.Zero || active==widget || active==settings || active==DesktopNative.FindContainer())return false;
      uint process;GetWindowThreadProcessId(active,out process);
      try {using(var target=Process.GetProcessById((int)process))if(target.Id!=Process.GetCurrentProcess().Id && target.ProcessName.StartsWith("Endfield",StringComparison.OrdinalIgnoreCase))return true;}catch{}
      DesktopNative.Rectangle bounds;
      if(!DesktopNative.GetWindowRect(active,out bounds))return false;
      var rect=new System.Windows.Rect(bounds.Left,bounds.Top,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
      return Placement.Displays().Any(d=>Covers(rect,d.Bounds));
    }
    public static bool Covers(System.Windows.Rect window,System.Windows.Rect monitor){return window.Left<=monitor.Left+2 && window.Top<=monitor.Top+2 && window.Right>=monitor.Right-2 && window.Bottom>=monitor.Bottom-2;}
  }
  interface IStartupRegistration {bool Enabled {get;} void Set(bool enabled);}
  sealed class MemoryStartupRegistration:IStartupRegistration {
    public bool Enabled {get;private set;}
    public int Writes;
    public void Set(bool enabled){Enabled=enabled;Writes++;}
  }
}
