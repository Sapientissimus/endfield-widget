using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  partial class Widget {
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process,uint flag);
    static void CheckRecoveryPolicy(Action<bool,string> check) {
      var now=DateTimeOffset.UtcNow;var state=new RefreshRecovery();
      check(IsTransientFailure(new SkportException(SkportStage.Sanity,SkportProblem.Network)) && IsTransientFailure(new SkportException(SkportStage.Sanity,SkportProblem.Timeout)),"Transport failures and timeouts enable connection retries");
      check(!IsTransientFailure(new SkportException(SkportStage.Login,SkportProblem.Rejected,401)) && !IsTransientFailure(new SkportException(SkportStage.Sanity,SkportProblem.Rejected,429)) && !IsTransientFailure(new FormatException()),"Invalid credentials, rate limits and changed article formats do not cause rapid retry loops");
      check(state.Interval(false,now,false).TotalSeconds==15 && state.Interval(true,now,false).TotalSeconds==1,"Idle runtime polling is relaxed while an active session retains its one-second clock");
      int old=state.Epoch;state.Request(now);int epoch=state.Epoch;state.Request(now.AddMilliseconds(600));
      check(state.Pending && epoch!=old && state.Epoch==epoch && state.Due==now.AddSeconds(2),"A burst of recovery events coalesces without postponing the first refresh");
      check(state.Interval(false,now,false).TotalSeconds==2 && state.Interval(false,now,true).TotalSeconds==15,"Recovery starts promptly without polling quickly during an in-flight request");
      state.Pending=false;state.Finished(epoch,now,false);
      check(state.Pending && state.Due==now.AddSeconds(15),"An internet failure schedules a retry even when the network adapter stayed available");
      state.Pending=false;state.Finished(epoch,now,false);
      check(state.Due==now.AddSeconds(30) && state.Interval(false,now,false).TotalSeconds==15,"Repeated connection failures back off while hidden ticking remains slow");
      for(int i=0;i<10;i++){state.Pending=false;state.Finished(epoch,now,false);}
      check(state.Due==now.AddMinutes(5),"Connection retry delays are capped at five minutes");
      state.Pending=false;state.Finished(epoch,now,true);check(!state.Pending && state.Attempts==0,"A successful refresh clears the retry state");
      state.Suspend();check(!state.Accept(epoch) && state.Suspended,"Suspending invalidates responses from requests started before sleep");
      state.Suspended=false;state.Request(now);state.Pending=false;int running=state.Epoch;state.Request(now.AddSeconds(1));state.Finished(running,now,true);
      check(state.Pending && state.Epoch!=running,"A wake or reconnect event during a request cannot be cleared by that request finishing");
      state.SetOnline(false,now);check(!state.Accept(state.Epoch) && !state.Online,"A disconnected network rejects every response until reconnection");
      state.SetOnline(true,now);check(state.Pending && state.Due==now.AddSeconds(2),"Reconnecting requests a fresh update without waiting for the normal polling interval");
      state.LastTick=now.AddMinutes(-2);check(state.ObserveTick(now),"A long timer gap detects a resume even if the power event was missed");
      check(!state.ObserveTick(now.AddSeconds(15)),"A normal hidden tick is not mistaken for sleep");
      check(state.ObserveTick(now.AddMinutes(-1)),"A backwards clock adjustment invalidates timing assumptions");
    }
    static void CheckTrayReadings(Action<bool,string> check,string folder) {
      var now=new DateTimeOffset(2026,10,15,8,0,0,TimeSpan.Zero);var accent=Color.FromArgb(228,242,91);
      var reading=new Sanity {RoleId="fixture",Current=178,Maximum=240,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddHours(2).ToUnixTimeSeconds()};
      var normal=TrayReading.From(reading,"fixture",true,false,"",now,accent);
      check(normal.Percent==74 && !normal.Stale && !normal.Unknown && normal.Text.Contains("178 / 240") && normal.Text.Contains("\nFull Sanity recovery\n") && normal.Text.EndsWith("10/15/2026 13:00"),"Tray hover text spells out full Sanity recovery and retains its complete Eastern European date and time");
      var estimate=TrayReading.From(reading,"fixture",true,false,"",now.AddMinutes(5),accent);
      check(estimate.Text.Contains("≈") && estimate.Text.Contains("Full Sanity recovery") && estimate.Text.EndsWith("10/15/2026 13:00") && estimate.Text.Length<=63,"Tray estimates retain their full recovery label, complete date and estimate marker within the tooltip length limit");
      foreach(int maximum in new[]{360,9999,int.MaxValue}) {
        var large=new Sanity{RoleId="fixture",Current=maximum/2,Maximum=maximum,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddHours(2).ToUnixTimeSeconds()};
        var text=TrayReading.From(large,"fixture",true,false,"",now.AddMinutes(5),accent).Text;
        using(var icon=new Forms.NotifyIcon()){icon.Text=text;check(icon.Text==text && text.ToLowerInvariant().Contains("sanity recovery") && !text.Contains("Full:") && !text.EndsWith("…") && text.EndsWith("10/15/2026 13:00"),"The native tooltip setter accepts a complete recovery date without truncation for maximum "+maximum);}
      }
      var stale=TrayReading.From(reading,"fixture",true,false,"offline",now.AddHours(1),accent);
      check(stale.Stale && stale.Text.Contains("178 / 240") && stale.Text.Contains("Last reading") && !stale.Text.Contains("10/15/2026 13:00") && !stale.Text.Contains("Sanity recovery"),"Offline tray readings are muted and never advertise an outdated recovery deadline");
      var pending=TrayReading.From(reading,"fixture",true,true,"",now,accent);
      check(pending.Stale && pending.Text.Contains("Refreshing data"),"Wake recovery marks cached readings as waiting for an update");
      check(TrayReading.From(reading,"another-role",true,false,"",now,accent).Unknown && TrayReading.From(reading,"fixture",false,false,"",now,accent).Unknown,"Wrong-role cached data and disconnected accounts never appear as valid tray readings");
      check(TrayReading.From(new Sanity {Maximum=0},null,true,false,"",now,accent).Unknown,"A malformed cached maximum cannot cause division by zero in the tray");
      check(TrayReading.From(reading,"fixture",true,false,"",now.AddSeconds(-1),accent).Stale,"A future-dated cache is not extrapolated in the tray");
      var fullReading=new Sanity {RoleId="fixture",Current=240,Maximum=240,Checked=now};var full=TrayReading.From(fullReading,"fixture",true,false,"",now,accent);
      check(full.Percent==100 && full.Text.Contains("Fully restored"),"The full tray state displays complete recovery without requiring a future deadline");
      var winter=TrayReading.From(new Sanity {RoleId="fixture",Current=100,Maximum=360,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero).ToUnixTimeSeconds()},"fixture",true,false,"",now,accent);
      check(winter.Text.Contains("11/5/2026 19:00"),"Tray recovery dates use Eastern European daylight-saving rules for their own date");
      var unknown=TrayReading.From(null,null,false,false,"",now,accent);
      var states=new[]{normal,new TrayReading {Percent=94,Accent=accent},full,stale,unknown};
      using(var strip=new Bitmap(520,148))using(var graphics=Graphics.FromImage(strip))using(var font=new Font("Segoe UI",10)) {
        graphics.Clear(Color.FromArgb(22,23,26));string[] labels={"74%","94%","100%","cached","no account"};
        for(int i=0;i<states.Length;i++)using(var icon=TrayGlyph.Draw(states[i])) {
          icon.Save(Path.Combine(folder,"tray-"+i+".png"),ImageFormat.Png);
          graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(icon,new Rectangle(36+i*104,18,32,32));graphics.DrawImage(icon,new Rectangle(44+i*104,63,16,16));
          using(var text=new SolidBrush(Color.White))graphics.DrawString(labels[i],font,text,12+i*104,101);
        }
        strip.Save(Path.Combine(folder,"tray-preview.png"),ImageFormat.Png);
      }
      using(var warmup=TrayGlyph.Create(normal)){}
      using(var process=Process.GetCurrentProcess()) {
        uint gdi=GetGuiResources(process.Handle,0),user=GetGuiResources(process.Handle,1);
        for(int i=0;i<150;i++)using(var icon=TrayGlyph.Create(new TrayReading {Accent=accent,Percent=i%101})){}
        check(GetGuiResources(process.Handle,0)<=gdi+4 && GetGuiResources(process.Handle,1)<=user+4,"Repeated tray icon replacement releases bitmap, drawing and HICON resources");
      }
    }
    async Task CheckBackgroundFeatures(Action<bool,string> check,string folder) {
      CheckRecoveryPolicy(check);CheckTrayReadings(check,folder);
      var oldTimer=timer;bool oldStarted=runtimeStarted,oldAwaiting=awaitingFreshSanity,oldVisible=window.IsVisible,oldLiveSanity=liveSanity,oldLiveNews=liveNews,oldVerified=bannersVerified;
      var oldNews=nextNews;var oldSanity=nextSanity;var oldBanner=nextBanner;
      var oldRecovery=new RefreshRecovery {Epoch=recovery.Epoch,Attempts=recovery.Attempts,Pending=recovery.Pending,Suspended=recovery.Suspended,Online=recovery.Online,Due=recovery.Due,LastTick=recovery.LastTick};
      var previousTray=tray;var previousIcon=trayStatusIcon;string previousKey=trayIconKey;
      try {
        InitializeBackgroundLoop();timer.Stop();window.Hide();
        check(!runtimeSubscribed && timer.Interval.TotalSeconds==15,"The isolated hidden runtime uses the slower timer without subscribing to real power or network changes");
        var now=DateTimeOffset.UtcNow;nextNews=nextSanity=nextBanner=now.AddHours(1);recovery.LastTick=now;
        int refreshes=0;bool forced=false;
        backgroundRefreshOverride=force=>{refreshes++;forced=force;recovery.Pending=false;recovery.Finished(recovery.Epoch,now,true);nextNews=nextSanity=nextBanner=now.AddHours(1);return Task.FromResult(0);};
        Text("RecoveryLabel","background-sentinel");await BackgroundTick(now.AddSeconds(15));
        check(Find<System.Windows.Controls.TextBlock>("RecoveryLabel").Text=="background-sentinel" && refreshes==0,"Hidden ticks leave the widget drawing untouched and do not fetch data before it is due");
        nextSanity=now;await BackgroundTick(now.AddSeconds(16));
        check(refreshes==1 && !forced && Find<System.Windows.Controls.TextBlock>("RecoveryLabel").Text=="background-sentinel","Due background data is fetched while hidden without repainting the widget");
        int beforeDates=refreshes;nextBanner=now;nextNews=nextSanity=now.AddHours(1);await BackgroundTick(now.AddSeconds(17));
        check(refreshes==beforeDates+1 && !forced && !window.IsVisible && Find<System.Windows.Controls.TextBlock>("RecoveryLabel").Text=="background-sentinel","The hidden background loop automatically refreshes due official dates without opening the widget");
        ShowWidget();
        check(timer.Interval>=TimeSpan.FromMilliseconds(50) && timer.Interval<=TimeSpan.FromSeconds(15) && Find<System.Windows.Controls.TextBlock>("RecoveryLabel").Text!="background-sentinel","Showing the widget paints current data immediately and schedules the next meaningful visible change");
        int oldEpoch=recovery.Epoch;liveSanity=liveNews=true;ApplyPowerState(PowerModes.Suspend,now);
        check(!timer.IsEnabled && !liveSanity && !liveNews && !CanApplyResponse(oldEpoch),"A suspend event stops ticking and invalidates pre-sleep response data");
        ApplyPowerState(PowerModes.Resume,now);timer.Stop();
        check(recovery.Pending && awaitingFreshSanity && nextSanity==DateTimeOffset.MinValue,"A resume queues fresh Sanity and official dates while preserving the cached reading");
        refreshBusy=true;int count=refreshes;await BackgroundTick(now.AddSeconds(3));
        check(recovery.Pending && refreshes==count,"A resume queued during an in-flight refresh is retained without starting a duplicate request");refreshBusy=false;
        await BackgroundTick(now.AddSeconds(4));check(refreshes==count+1 && forced,"The queued wake refresh runs as soon as the previous operation is free");
        int active=recovery.Epoch;ApplyNetworkState(false,now);count=refreshes;await BackgroundTick(now.AddSeconds(5));
        check(refreshes==count && !CanApplyResponse(active),"Network loss prevents requests and rejects results from the previous connection");
        ApplyNetworkState(true,now);ApplyNetworkState(true,now.AddMilliseconds(200));await BackgroundTick(now.AddSeconds(6));
        check(refreshes==count+1 && forced,"A reconnect event burst triggers one fresh update");
        recovery.LastTick=now.AddMinutes(-2);await BackgroundTick(now);
        check(recovery.Pending && awaitingFreshSanity,"The real tick path also recovers after a long unreported timer gap");
        recovery.Pending=false;recovery.Online=true;awaitingFreshSanity=false;
        tray=new Forms.NotifyIcon {Visible=false};trayStatusIcon=null;trayIconKey="";
        UpdateTrayStatus(DateTimeOffset.UtcNow);var icon=trayStatusIcon;UpdateTrayStatus(DateTimeOffset.UtcNow);
        check(icon!=null && object.ReferenceEquals(icon,trayStatusIcon) && tray.Text.Length<=63,"The real NotifyIcon accepts its hover text and reuses an unchanged native icon");
      } finally {
        if(timer!=null && timer!=oldTimer)timer.Stop();DisposeBackgroundLoop();timer=oldTimer;runtimeStarted=oldStarted;backgroundRefreshOverride=null;refreshBusy=false;awaitingFreshSanity=oldAwaiting;liveSanity=oldLiveSanity;liveNews=oldLiveNews;bannersVerified=oldVerified;
        nextNews=oldNews;nextSanity=oldSanity;nextBanner=oldBanner;recovery.Epoch=oldRecovery.Epoch;recovery.Attempts=oldRecovery.Attempts;recovery.Pending=oldRecovery.Pending;recovery.Suspended=oldRecovery.Suspended;recovery.Online=oldRecovery.Online;recovery.Due=oldRecovery.Due;recovery.LastTick=oldRecovery.LastTick;
        if(tray!=previousTray){if(tray!=null)tray.Dispose();DisposeTrayStatus();}tray=previousTray;trayStatusIcon=previousIcon;trayIconKey=previousKey;
        if(oldVisible)ShowWidget();else window.Hide();Paint();
      }
    }
  }
}
