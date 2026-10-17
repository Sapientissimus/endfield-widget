using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    static void CheckProcessSampler(Action<bool,string> check) {
      using(var self=Process.GetCurrentProcess()) {
        var actual=GameProcessSampler.Sample(self.ProcessName+".exe");
        DateTimeOffset? first=null;var matches=Process.GetProcessesByName(self.ProcessName);
        try{foreach(var p in matches)try{if(p.SessionId==self.SessionId){var start=new DateTimeOffset(p.StartTime.ToUniversalTime());if(!first.HasValue || start<first)first=start;}}catch{}}finally{foreach(var p in matches)p.Dispose();}
        check(actual.Running==true && actual.Started.HasValue && first.HasValue && Math.Abs((actual.Started.Value-first.Value).TotalMilliseconds)<1,"Native process sampling agrees with Windows on the current session and earliest process start time");
        var lower=GameProcessSampler.Sample((self.ProcessName+".exe").ToLowerInvariant());
        check(lower.Running==true && lower.Started==actual.Started,"The native executable comparison is case insensitive and stable across samples");
        check(GameProcessSampler.Sample("EndfieldWidgetMissingFixture123456.exe").Running==false,"A complete native snapshot reports a missing executable without opening unrelated processes");
        check(!GameProcessSampler.Sample("").Running.HasValue && !GameProcessSampler.Sample(new string('x',260)).Running.HasValue,"Invalid executable names produce an unknown state rather than a false game exit");
        int handles=self.HandleCount;for(int i=0;i<100;i++)GameProcessSampler.Sample(self.ProcessName+".exe");self.Refresh();
        check(self.HandleCount<=handles+2,"A hundred native snapshots release their process handles, snapshot handles and entry buffers");
      }
    }
    async Task CheckOptimizationFeatures(Action<bool,string> check,string folder) {
      CheckProcessSampler(check);
      string original=Json.Encode(config);var oldHistory=appearanceHistory;var oldSanity=sanity;var oldSchedule=schedule;var oldBanners=bannerState;string oldToken=accountToken,oldError=sanityError;bool oldAwaiting=awaitingFreshSanity;
      var oldNews=nextNews;var oldNextSanity=nextSanity;var oldBanner=nextBanner;var oldActivity=gameActivity;bool oldOnline=recovery.Online,oldPending=recovery.Pending;var oldDue=recovery.Due;string oldDirectory=Storage.DirectoryPath;
      try {
        Find<ProgressBar>("SanityBar").Value=37;config.SanityStyle="ring";UpdateSanityStyle();
        var full=Find<System.Windows.Shapes.Path>("FullRingArc");var compact=Find<System.Windows.Shapes.Path>("CompactRingArc");var fullData=full.Data;var compactData=compact.Data;
        UpdateSanityStyle();check(full.Data==fullData && compact.Data==compactData && fullData.IsFrozen && compactData.IsFrozen,"Unchanged Sanity rings reuse frozen geometry in both modes");
        config.SanityStyle="bar";Find<ProgressBar>("SanityBar").Value=63;UpdateSanityStyle();
        check(full.Data==fullData && compact.Data==compactData,"Hidden rings allocate no replacement geometry when the visible bar changes");
        config.SanityStyle="ring";UpdateSanityStyle();check(full.Data!=fullData && compact.Data!=compactData,"Re-enabling rings immediately draws the latest percentage");
        Find<ProgressBar>("SanityBar").Value=100;UpdateSanityStyle();check(full.Data is EllipseGeometry && full.Data.IsFrozen,"A full ring uses immutable circle geometry");
        Find<ProgressBar>("SanityBar").Value=0;UpdateSanityStyle();check(full.Data==Geometry.Empty && compact.Data==Geometry.Empty,"Empty rings clear their previous progress in both modes");
        config.Frame("sample",false).Zoom=1.23;var snapshot=AppearanceState.Capture(config);config.Frame("sample",false).Zoom=1.31;
        check(snapshot.PortraitFrames["sample|full"].Zoom==1.23,"Appearance snapshots own independent portrait frames without serializing the profile");
        snapshot.Apply(config);config.Frame("sample",false).Zoom=1.42;
        check(snapshot.PortraitFrames["sample|full"].Zoom==1.23,"Applying an appearance does not share mutable portrait frames with undo history");

        var now=new DateTimeOffset(2026,10,15,12,0,10,TimeSpan.Zero);
        config.ShowSessionTime=false;config.BannerRegion="Europe";config.MicroMode=false;gameActivity=new GameActivity();accountToken="";sanity=null;schedule=null;bannerState=new BannerState{Debuts=new List<Debut>()};recovery.Online=true;recovery.Pending=false;nextNews=nextSanity=nextBanner=DateTimeOffset.MaxValue;
        check(BackgroundInterval(now)==TimeSpan.FromSeconds(15),"An idle visible widget sleeps for up to fifteen seconds instead of repainting every second");
        check(BackgroundInterval(now.AddSeconds(49.8))==TimeSpan.FromSeconds(15),"Removing the wall clock avoids an otherwise unnecessary wake at each minute boundary");
        schedule=new Schedule{Start=now.AddMilliseconds(230),End=now.AddHours(1).AddSeconds(30)};
        check(Math.Abs(BackgroundInterval(now).TotalMilliseconds-230)<.01,"Maintenance starts wake the widget at the announced instant despite relaxed idle polling");schedule=null;
        bannerState.Debuts.Add(new Debut{Name="Fixture",Key="fixture",Source=NewsClient.NewsUrl+"/9560",EuropeStart=now.AddMilliseconds(270),AsiaStart=now.AddMilliseconds(270)});
        check(Math.Abs(BackgroundInterval(now).TotalMilliseconds-270)<.01,"A debut start triggers an immediate art transition without advancing it early");bannerState.Debuts.Clear();
        bannerState.Debuts.Add(new Debut{Name="Fixture",Key="fixture",Source=NewsClient.NewsUrl+"/9560",EuropeStart=now.AddMinutes(-5),AsiaStart=now.AddMinutes(-5),EuropeEndUnix=now.AddSeconds(2).ToUnixTimeSeconds()});
        check(BackgroundInterval(now)==TimeSpan.FromSeconds(2),"The confirmed current banner end also keeps its exact transition deadline");bannerState.Debuts.Clear();
        accountToken="isolated-fixture";sanityError="";awaitingFreshSanity=false;
        sanity=new Sanity{Current=9,Maximum=10,ServerTime=1000,FullAt=1100,Checked=now.AddMilliseconds(-99700)};
        check(Math.Abs(BackgroundInterval(now).TotalMilliseconds-300)<1,"The next estimated Sanity point is scheduled at its actual recovery time");
        sanity.Checked=now.AddMinutes(-15).AddMilliseconds(200);
        check(Math.Abs(BackgroundInterval(now).TotalMilliseconds-200)<1,"A cached reading becomes stale at the fifteen-minute validity boundary");
        sanity=new Sanity{Current=9,Maximum=10,Checked=DateTimeOffset.MaxValue};
        check(BackgroundInterval(now)==TimeSpan.FromSeconds(15),"A corrupt future cache timestamp cannot overflow the new timer calculation");
        sanity=null;gameActivity.Observe(true);config.ShowSessionTime=true;
        check(BackgroundInterval(now)==TimeSpan.FromSeconds(1),"A displayed game session keeps its one-second counter");
        config.MicroMode=true;check(BackgroundInterval(now)==TimeSpan.FromSeconds(15),"The micro mode skips second ticks because it has no session counter");config.MicroMode=false;gameActivity.Observe(false);gameActivity.Observe(false);config.ShowSessionTime=false;
        recovery.Pending=true;recovery.Due=now.AddSeconds(2);check(BackgroundInterval(now)==TimeSpan.FromSeconds(2),"Reconnect recovery retains its prompt two-second deadline");recovery.Pending=false;
        foreach(double remaining in new[]{35.0,60,61,3599,3600,3601,86399,86400,86401,172800.4}) {
          var span=TimeSpan.FromSeconds(remaining);var delay=RefreshTiming.CountdownDelay(span);string value=Clock.Remaining(span);
          check(delay>TimeSpan.Zero && (delay>=span || Clock.Remaining(span-delay)!=value && (delay<=TimeSpan.FromMilliseconds(1) || Clock.Remaining(span-delay+TimeSpan.FromMilliseconds(2))==value)),"Countdown wake matches its displayed minute/hour boundary for "+remaining+" seconds");
        }
        check(RefreshTiming.Bound(TimeSpan.Zero)==TimeSpan.FromMilliseconds(50) && RefreshTiming.Bound(TimeSpan.FromHours(1))==TimeSpan.FromSeconds(15),"Timer bounds prevent busy loops while preserving regular recovery and notification checks");

        Storage.DirectoryPath=Path.Combine(folder,"commit-fixture-"+Guid.NewGuid().ToString("N"));InitializeAppearanceHistory();SavePosition();int commits=settingsCommitCount;appearanceHistory.BeginGesture();
        for(int i=0;i<100;i++){config.BackgroundOpacity=.61+(i%30)/100.0;SavePosition();}
        check(settingsCommitCount==commits && settingsCommitPending && appearanceHistory.CanUndo,"A hundred slider updates redraw immediately while queuing a single durable save and one undo gesture");
        appearanceHistory.EndGesture();FlushSettingsCommit();var saved=Storage.Read<Config>("settings.json");
        check(settingsCommitCount==commits+1 && !settingsCommitPending && saved.BackgroundOpacity==config.BackgroundOpacity,"Releasing a slider commits its final value once to the atomic settings store");
        commits=settingsCommitCount;appearanceHistory.BeginGesture();config.BackgroundOpacity=.73;SavePosition();await Task.Delay(450);
        check(settingsCommitCount==commits+1 && !settingsCommitPending && Storage.Read<Config>("settings.json").BackgroundOpacity==.73,"A paused or lost-capture gesture is saved automatically after the debounce delay");
        config.BackgroundOpacity=.74;SavePosition();appearanceHistory.EndGesture();FlushSettingsCommit();
        check(Storage.Read<Config>("settings.json").BackgroundOpacity==.74 && !settingsCommitTimer.IsEnabled,"Flushing at settings close preserves the last edit and stops the trailing timer");
      } finally {
        if(appearanceHistory!=null)appearanceHistory.EndGesture();FlushSettingsCommit();Storage.DirectoryPath=oldDirectory;
        var restored=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Config>(original);foreach(var p in typeof(Config).GetProperties())if(p.CanWrite)p.SetValue(config,p.GetValue(restored,null),null);appearanceHistory=oldHistory;
        sanity=oldSanity;schedule=oldSchedule;bannerState=oldBanners;accountToken=oldToken;sanityError=oldError;awaitingFreshSanity=oldAwaiting;nextNews=oldNews;nextSanity=oldNextSanity;nextBanner=oldBanner;gameActivity=oldActivity;recovery.Online=oldOnline;recovery.Pending=oldPending;recovery.Due=oldDue;
        ApplyLayout(true);Paint();
      }
    }
  }
}
