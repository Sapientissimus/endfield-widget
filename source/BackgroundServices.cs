using System;
using System.Net.NetworkInformation;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace EndfieldWidget {
  sealed class RefreshRecovery {
    public int Epoch,Attempts;
    public bool Pending,Suspended,Online=true;
    public DateTimeOffset Due=DateTimeOffset.MaxValue,LastTick=DateTimeOffset.MinValue;
    public void Request(DateTimeOffset now) {
      if(!Pending){Epoch++;Due=now.AddSeconds(2);}else if(Due>now.AddSeconds(2))Due=now.AddSeconds(2);
      Pending=true;Attempts=0;
    }
    public void Suspend(){Epoch++;Suspended=true;Pending=true;Due=DateTimeOffset.MaxValue;}
    public void SetOnline(bool value,DateTimeOffset now) {
      Online=value;
      if(value)Request(now);else{Epoch++;Pending=true;Due=DateTimeOffset.MaxValue;Attempts=0;}
    }
    public bool Accept(int epoch){return epoch==Epoch && !Suspended && Online;}
    public void Finished(int epoch,DateTimeOffset now,bool success) {
      if(!Accept(epoch) || Pending)return;
      if(success){Attempts=0;Due=DateTimeOffset.MaxValue;return;}
      int seconds=(int)Math.Min(300,15*Math.Pow(2,Math.Min(Attempts,5)));Attempts++;
      Pending=true;Due=now.AddSeconds(seconds);
    }
    public bool ObserveTick(DateTimeOffset now) {
      bool gap=LastTick!=DateTimeOffset.MinValue && ((now-LastTick).TotalSeconds>45 || now<LastTick.AddSeconds(-1));LastTick=now;return gap;
    }
    public TimeSpan Interval(bool secondClock,DateTimeOffset now,bool busy){return TimeSpan.FromSeconds(secondClock?1:Pending && Online && !Suspended && !busy?Math.Max(1,Math.Min(15,(Due-now).TotalSeconds)):15);}
  }
  partial class Widget {
    readonly RefreshRecovery recovery=new RefreshRecovery();
    bool runtimeStarted,runtimeSubscribed,awaitingFreshSanity;
    bool retrySanity,retryNews,retryBanners;
    Func<bool,Task> backgroundRefreshOverride;
    bool CanApplyResponse(int epoch){return !closing && !extrasDisposed && recovery.Accept(epoch);}
    static bool IsTransientFailure(Exception error) {
      var bulletin=error as BulletinException;if(bulletin!=null)return bulletin.Status>=500;
      var skportError=error as SkportException;
      if(skportError!=null)return skportError.Problem==SkportProblem.Network || skportError.Problem==SkportProblem.Timeout || skportError.HttpStatus>=500;
      return error is HttpRequestException || error is TaskCanceledException || error is TimeoutException;
    }
    void InitializeBackgroundLoop() {
      Clock.RefreshSystemZone();
      runtimeStarted=true;awaitingFreshSanity=!string.IsNullOrEmpty(accountToken);recovery.LastTick=DateTimeOffset.UtcNow;
      timer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher);
      timer.Tick+=async(s,e)=>await BackgroundTick(DateTimeOffset.UtcNow);
      if(!preview && !Storage.Isolated) {
        SystemEvents.PowerModeChanged+=PowerChanged;NetworkChange.NetworkAvailabilityChanged+=NetworkChanged;NetworkChange.NetworkAddressChanged+=AddressChanged;
        SystemEvents.TimeChanged+=TimeSettingsChanged;SystemEvents.UserPreferenceChanged+=RegionalSettingsChanged;runtimeSubscribed=true;
        try{if(!NetworkInterface.GetIsNetworkAvailable())ApplyNetworkState(false,DateTimeOffset.UtcNow);}catch{}
      }
      AdjustBackgroundTimer();if(!recovery.Suspended)timer.Start();UpdateTrayStatus(DateTimeOffset.UtcNow);
    }
    void AdjustBackgroundTimer(){if(runtimeStarted && timer!=null){var interval=BackgroundInterval(DateTimeOffset.UtcNow);if(timer.Interval!=interval)timer.Interval=interval;}}
    void UpdateSurfaceState() {
      var now=DateTimeOffset.UtcNow;
      if(window.IsVisible)Paint();else{PrepareOperatorArt(now);UpdateTrayStatus(now);}
      TickCalendar(now);
    }
    async Task BackgroundTick(DateTimeOffset now) {
      if(closing || extrasDisposed || recovery.Suspended)return;
      if(recovery.ObserveTick(now))RequestRecovery(now);
      UpdateSurfaceState();CheckNotifications();
      if(!refreshBusy && recovery.Online) {
        bool forced=recovery.Pending && now>=recovery.Due;
        bool due=!recovery.Pending && (now>=nextNews || now>=nextSanity || now>=nextBanner);
        if(forced || due){if(backgroundRefreshOverride!=null)await backgroundRefreshOverride(forced);else await Refresh(forced);}
      }
      AdjustBackgroundTimer();
    }
    void InvalidateLiveReadings(){awaitingFreshSanity=!string.IsNullOrEmpty(accountToken);liveSanity=false;liveNews=false;nextNotificationCheck=DateTimeOffset.MinValue;banners.InvalidateReads();banners.CancelDownloads();if(runtimeStarted)try{http.CancelPendingRequests();}catch(ObjectDisposedException){}}
    void RequestRecovery(DateTimeOffset now) {
      recovery.Request(now);InvalidateLiveReadings();nextNews=nextSanity=nextBanner=DateTimeOffset.MinValue;
      UpdateSurfaceState();AdjustBackgroundTimer();
    }
    void ApplyPowerState(PowerModes mode,DateTimeOffset now) {
      if(mode==PowerModes.Suspend){recovery.Suspend();InvalidateLiveReadings();if(timer!=null && runtimeStarted)timer.Stop();UpdateSurfaceState();}
      else if(mode==PowerModes.Resume){ApplyLocalTimeSettings();recovery.Suspended=false;recovery.LastTick=now;RequestRecovery(now);if(timer!=null && runtimeStarted)timer.Start();}
    }
    void ApplyNetworkState(bool available,DateTimeOffset now) {
      recovery.SetOnline(available,now);InvalidateLiveReadings();if(available)nextNews=nextSanity=nextBanner=DateTimeOffset.MinValue;
      UpdateSurfaceState();AdjustBackgroundTimer();
    }
    void PostBackground(Action action) {
      if(closing || extrasDisposed || window.Dispatcher.HasShutdownStarted)return;
      try{window.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!closing && !extrasDisposed)action();}));}catch(InvalidOperationException){}
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e){PostBackground(()=>ApplyPowerState(e.Mode,DateTimeOffset.UtcNow));}
    void ApplyLocalTimeSettings(){Clock.RefreshSystemZone();UpdateSurfaceState();if(calendarDialog!=null)RenderCalendar();AdjustBackgroundTimer();}
    void TimeSettingsChanged(object sender,EventArgs e){PostBackground(ApplyLocalTimeSettings);}
    void RegionalSettingsChanged(object sender,UserPreferenceChangedEventArgs e){if(e.Category==UserPreferenceCategory.Locale)PostBackground(ApplyLocalTimeSettings);}
    void NetworkChanged(object sender,NetworkAvailabilityEventArgs e){PostBackground(()=>ApplyNetworkState(e.IsAvailable,DateTimeOffset.UtcNow));}
    void AddressChanged(object sender,EventArgs e){PostBackground(()=>{bool available=true;try{available=NetworkInterface.GetIsNetworkAvailable();}catch{}ApplyNetworkState(available,DateTimeOffset.UtcNow);});}
    void DisposeBackgroundLoop() {
      if(runtimeSubscribed){SystemEvents.PowerModeChanged-=PowerChanged;NetworkChange.NetworkAvailabilityChanged-=NetworkChanged;NetworkChange.NetworkAddressChanged-=AddressChanged;SystemEvents.TimeChanged-=TimeSettingsChanged;SystemEvents.UserPreferenceChanged-=RegionalSettingsChanged;runtimeSubscribed=false;}
      runtimeStarted=false;
    }
  }
}
