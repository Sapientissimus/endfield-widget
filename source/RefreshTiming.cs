using System;

namespace EndfieldWidget {
  static class RefreshTiming {
    // Clock.Remaining shows whole hours above a day, whole minutes above an
    // hour, and rounded-up minutes below it. Wake at the next displayed change.
    public static TimeSpan CountdownDelay(TimeSpan remaining) {
      if(remaining<=TimeSpan.Zero)return TimeSpan.FromSeconds(15);
      long unit=remaining.TotalDays>=1?TimeSpan.TicksPerHour:TimeSpan.TicksPerMinute;
      long ticks=remaining.Ticks%unit;
      if(remaining.TotalHours<1 && ticks==0)ticks=unit;
      else ticks+=TimeSpan.TicksPerMillisecond;
      return TimeSpan.FromTicks(ticks);
    }
    public static TimeSpan Bound(TimeSpan delay){return TimeSpan.FromMilliseconds(Math.Max(50,Math.Min(15000,delay.TotalMilliseconds)));}
  }
  partial class Widget {
    TimeSpan BackgroundInterval(DateTimeOffset now) {
      bool visible=window.IsVisible;
      bool seconds=visible && !config.MicroMode && config.ShowSessionTime && gameActivity.Running;
      var delay=recovery.Interval(seconds,now,refreshBusy);
      Action<DateTimeOffset> deadline=at=>{if(at>now && at-now<delay)delay=at-now;};
      Action<DateTimeOffset> countdown=at=>{if(at<=now)return;deadline(at);var next=RefreshTiming.CountdownDelay(at-now);if(next<delay)delay=next;};
      if(recovery.Online && !refreshBusy) {
        if(recovery.Pending)deadline(recovery.Due);
        else {deadline(nextNews);deadline(nextSanity);deadline(nextBanner);}
      }
      if(!visible)return RefreshTiming.Bound(delay);
      if(sanity!=null && accountToken.Length>0) {
        deadline(sanity.Checked);try{deadline(sanity.Checked.AddMinutes(15).AddTicks(1));}catch(ArgumentOutOfRangeException){}
        if(!awaitingFreshSanity && sanityError.Length==0 && now>=sanity.Checked && now-sanity.Checked<=TimeSpan.FromMinutes(15)) {
          var full=sanity.LocalFullAt();if(full.HasValue)countdown(full.Value);
          int missing=sanity.Maximum-sanity.Current;
          double perPoint=full.HasValue && missing>0?((double)sanity.FullAt-sanity.ServerTime)/missing:0;
          if(perPoint>0 && sanity.Estimate(now)<sanity.Maximum) {
            double points=Math.Floor((now-sanity.Checked).TotalSeconds/perPoint)+1;
            try{deadline(points>=missing?full.Value:sanity.Checked.AddSeconds(points*perPoint));}catch(ArgumentOutOfRangeException){}
          }
        }
      }
      if(schedule!=null){countdown(schedule.Start);countdown(schedule.End);}
      bool asia=config.BannerRegion=="Asia";
      var upcoming=BannerClient.Next(bannerState,now,asia);
      if(upcoming!=null)countdown(asia?upcoming.AsiaStart:upcoming.EuropeStart);
      var end=BannerClient.End(BannerClient.Select(bannerState,now,asia),asia);
      if(end.HasValue)countdown(end.Value);
      return RefreshTiming.Bound(delay);
    }
  }
}
