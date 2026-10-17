using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EndfieldWidget {
  sealed class TrayReading {
    public int Percent;
    public bool Unknown,Stale;
    public string Text;
    public Color Accent;
    public string IconKey {get{return Percent+":"+Unknown+":"+Stale+":"+Accent.ToArgb();}}
    public static TrayReading From(Sanity reading,string role,bool connected,bool pending,string error,DateTimeOffset now,Color accent) {
      var state=new TrayReading {Unknown=true,Accent=accent};
      if(!connected){state.Text=L.T("Endfield\nConnect SKPORT in settings");return state;}
      if(reading==null || reading.Maximum<=0 || reading.Current<0 || (!string.IsNullOrEmpty(role) && reading.RoleId!=role)) {
        state.Text=L.T("Sanity: —\n")+(string.IsNullOrEmpty(error)?L.T("Refreshing data…"):L.T("Data is temporarily unavailable"));return state;
      }
      state.Unknown=false;state.Stale=pending || !string.IsNullOrEmpty(error) || now<reading.Checked || (now-reading.Checked).TotalMinutes>15;
      int value=state.Stale?reading.Current:reading.Estimate(now);
      state.Percent=(int)Math.Round(Math.Max(0,Math.Min(100,value*100.0/reading.Maximum)));
      string counts=(!state.Stale && value!=reading.Current?"≈":"")+value+" / "+reading.Maximum;
      string summary=counts+" · "+state.Percent+"%";
      state.Text=L.T("Sanity: ")+summary+"\n";
      if(pending && string.IsNullOrEmpty(error))state.Text+=L.T("Refreshing data…");
      else if(state.Stale)state.Text+=reading.Checked==DateTimeOffset.MinValue?L.T("Refresh needed"):L.T("Last reading: ")+Clock.Local(reading.Checked).ToString("dd.MM HH:mm")+"";
      else if(value>=reading.Maximum)state.Text+=L.T("Fully restored");
      else {
        DateTimeOffset? full=null;try{full=reading.LocalFullAt();}catch(ArgumentOutOfRangeException){}
        if(full.HasValue) {
          string at=Clock.Date(full.Value)+" "+Clock.Time(full.Value)+"";
          state.Text=summary+L.T("\nFull Sanity recovery\n")+at;
          // Framework NotifyIcon.Text allows 63 characters. Keep the date and
          // clear wording intact; omit the redundant percentage for long counts.
          if(state.Text.Length>63)state.Text=counts+L.T("\nFull Sanity recovery\n")+at;
          if(state.Text.Length>63)state.Text=counts.Replace(" / ","/")+L.T("\nSanity recovery\n")+at;
        }else state.Text+=L.T("Recovery time unknown");
      }
      if(state.Text.Length>63)state.Text=state.Text.Substring(0,62)+"…";
      return state;
    }
  }
  static class TrayGlyph {
    [DllImport("user32.dll",SetLastError=true)] static extern bool DestroyIcon(IntPtr icon);
    public static Bitmap Draw(TrayReading state) {
      var bitmap=new Bitmap(32,32,PixelFormat.Format32bppArgb);
      using(var g=Graphics.FromImage(bitmap)) {
        g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        var accent=state.Stale || state.Unknown?Color.FromArgb(190,164,171,180):state.Accent;
        using(var background=new Pen(Color.FromArgb(180,65,70,78),4))g.DrawEllipse(background,3,3,26,26);
        using(var progress=new Pen(accent,4)) {
          progress.StartCap=progress.EndCap=LineCap.Round;
          if(!state.Unknown && state.Percent>=100)g.DrawEllipse(progress,3,3,26,26);
          else if(!state.Unknown && state.Percent>0)g.DrawArc(progress,3,3,26,26,-90,state.Percent*3.6f);
        }
        using(var center=new SolidBrush(accent)) {
          if(state.Unknown)g.FillRectangle(center,11,14,10,4);
          else g.FillPolygon(center,new[]{new PointF(16,9),new PointF(21,16),new PointF(16,23),new PointF(11,16)});
        }
        if(state.Stale)using(var badge=new SolidBrush(Color.FromArgb(255,112,120,131)))g.FillEllipse(badge,23,23,8,8);
      }
      return bitmap;
    }
    public static Icon Create(TrayReading state) {
      using(var bitmap=Draw(state)) {
        IntPtr handle=bitmap.GetHicon();
        try{using(var borrowed=Icon.FromHandle(handle))return (Icon)borrowed.Clone();}finally{DestroyIcon(handle);}
      }
    }
  }
  partial class Widget {
    Icon trayStatusIcon;
    string trayIconKey="";
    void UpdateTrayStatus(DateTimeOffset now) {
      if(tray==null || closing || extrasDisposed)return;
      var color=currentTheme.Accent;
      var state=TrayReading.From(sanity,config.RoleId,!string.IsNullOrEmpty(accountToken),awaitingFreshSanity,recovery.Online?sanityError:"offline",now,Color.FromArgb(color.R,color.G,color.B));
      if(tray.Text!=state.Text)tray.Text=state.Text;
      if(trayIconKey==state.IconKey)return;
      var icon=TrayGlyph.Create(state);var previous=trayStatusIcon;
      tray.Icon=icon;trayStatusIcon=icon;trayIconKey=state.IconKey;if(previous!=null)previous.Dispose();
    }
    void DisposeTrayStatus(){if(trayStatusIcon!=null){trayStatusIcon.Dispose();trayStatusIcon=null;}trayIconKey="";}
  }
}
