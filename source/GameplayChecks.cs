using System;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckGameActivity(Action<bool,string> check) {
      var oldActivity=gameActivity;var oldProbe=gameProbe;var oldOverride=backgroundRefreshOverride;
      bool oldBusy=refreshBusy,oldStarted=runtimeStarted,oldVisible=window.IsVisible,oldAwaiting=awaitingFreshSanity,oldSanityLive=liveSanity,oldNewsLive=liveNews;
      string oldToken=accountToken;var oldNews=nextNews;var oldSanity=nextSanity;var oldBanner=nextBanner;var oldCheck=nextNotificationCheck;
      var previous=new RefreshRecovery {Epoch=recovery.Epoch,Attempts=recovery.Attempts,Pending=recovery.Pending,Suspended=recovery.Suspended,Online=recovery.Online,Due=recovery.Due,LastTick=recovery.LastTick};
      try {
        gameActivity=new GameActivity();bool? sample=false;gameProbe=()=>sample;runtimeStarted=false;
        var now=DateTimeOffset.UtcNow;recovery.Pending=false;recovery.Online=true;recovery.Suspended=false;recovery.LastTick=now;recovery.Due=DateTimeOffset.MaxValue;
        nextNews=nextSanity=nextBanner=now.AddHours(1);accountToken="isolated-session-fixture";
        int refreshes=0;bool force=false;
        backgroundRefreshOverride=forced=>{refreshes++;force=forced;recovery.Pending=false;recovery.Finished(recovery.Epoch,now,true);nextNews=nextSanity=nextBanner=now.AddHours(1);return Task.FromResult(0);};
        int epoch=recovery.Epoch;PollGameActivity(now);
        check(!gameActivity.Running && !recovery.Pending && recovery.Epoch==epoch,"Starting the widget with no game running does not fabricate a game-exit refresh");
        sample=true;PollGameActivity(now);
        check(gameActivity.Running && Find<Button>("PlayButton").IsEnabled && Find<Button>("CompactPlayButton").IsEnabled && Find<TextBlock>("PlayLabel").Text=="Game is running","An observed Endfield process updates both launch buttons to the running state with return-to-game available");
        sample=false;PollGameActivity(now);sample=true;PollGameActivity(now);
        check(gameActivity.Running && !recovery.Pending,"A single missing process sample followed by a running process does not report an exit");
        sample=false;PollGameActivity(now);sample=null;PollGameActivity(now);sample=false;PollGameActivity(now);
        check(gameActivity.Running && !recovery.Pending,"Unknown process observations do not confirm an exit or enable duplicate launches");
        gameProbe=()=>{throw new InvalidOperationException("isolated process-enumeration error");};PollGameActivity(now);
        check(gameActivity.Running && !recovery.Pending,"A process-enumeration failure leaves the last confirmed game state intact");gameProbe=()=>sample;
        window.Hide();Text("RecoveryLabel","game-exit-sentinel");sample=true;PollGameActivity(now);sample=false;PollGameActivity(now.AddSeconds(1));
        refreshBusy=true;var retained=sanity;epoch=recovery.Epoch;PollGameActivity(now.AddSeconds(2));
        check(!gameActivity.Running && recovery.Pending && awaitingFreshSanity && nextSanity==DateTimeOffset.MinValue && !CanApplyResponse(epoch) && object.ReferenceEquals(retained,sanity),"A confirmed game exit queues fresh Sanity, keeps the cached reading and rejects pre-exit responses during an active request");
        check(!window.IsVisible && Find<TextBlock>("RecoveryLabel").Text=="game-exit-sentinel","Game-exit detection works while hidden without revealing or repainting the widget");
        await BackgroundTick(now.AddSeconds(5));check(refreshes==0 && recovery.Pending,"An in-flight refresh does not consume or overlap the queued game-exit refresh");
        refreshBusy=false;await BackgroundTick(now.AddSeconds(6));PollGameActivity(now.AddSeconds(7));await BackgroundTick(now.AddSeconds(7));
        check(refreshes==1 && force && !recovery.Pending,"The queued game-exit refresh runs once through the existing recovery path when the active request is free");
        sample=true;PollGameActivity(now.AddSeconds(8));ApplyNetworkState(false,now.AddSeconds(9));
        sample=false;PollGameActivity(now.AddSeconds(10));PollGameActivity(now.AddSeconds(11));await BackgroundTick(now.AddSeconds(14));
        check(recovery.Pending && refreshes==1,"Closing the game while offline retains the refresh request without attempting network work");
        ApplyNetworkState(true,now.AddSeconds(15));await BackgroundTick(now.AddSeconds(18));
        check(refreshes==2 && force,"Returning online coalesces the queued game-exit refresh with network recovery");
        accountToken="";sample=true;PollGameActivity(now.AddSeconds(19));epoch=recovery.Epoch;
        sample=false;PollGameActivity(now.AddSeconds(20));PollGameActivity(now.AddSeconds(21));
        check(!gameActivity.Running && !recovery.Pending && recovery.Epoch==epoch,"Game exit without a connected account does not trigger an unnecessary resource request");
      }finally {
        gameActivity=oldActivity;gameProbe=oldProbe;backgroundRefreshOverride=oldOverride;refreshBusy=oldBusy;runtimeStarted=oldStarted;accountToken=oldToken;
        awaitingFreshSanity=oldAwaiting;liveSanity=oldSanityLive;liveNews=oldNewsLive;nextNews=oldNews;nextSanity=oldSanity;nextBanner=oldBanner;nextNotificationCheck=oldCheck;
        recovery.Epoch=previous.Epoch;recovery.Attempts=previous.Attempts;recovery.Pending=previous.Pending;recovery.Suspended=previous.Suspended;recovery.Online=previous.Online;recovery.Due=previous.Due;recovery.LastTick=previous.LastTick;
        if(oldVisible)ShowWidget();else window.Hide();UpdateGameLaunchControls();AdjustBackgroundTimer();
      }
    }
    void CheckArtworkAppearance(Action<bool,string> check,Window dialog,string folder) {
      var serializer=new JavaScriptSerializer();var old=serializer.Deserialize<Config>("{}");old.NormalizePreferences();
      check(old.PortraitOpacity==1 && old.ShowWatermark,"Older profiles retain their fully visible portrait and existing EF decoration");
      var hidden=serializer.Deserialize<Config>("{\"PortraitOpacity\":0,\"ShowWatermark\":false}");hidden.NormalizePreferences();
      check(hidden.PortraitOpacity==0 && !hidden.ShowWatermark,"Explicit zero portrait opacity and hidden EF survive preference normalization");
      var invalid=new Config {PortraitOpacity=double.NaN};invalid.NormalizePreferences();
      check(invalid.PortraitOpacity==1,"Invalid artwork opacity is normalized before being applied to WPF");
      double previousOpacity=config.PortraitOpacity,background=config.BackgroundOpacity;bool previousWatermark=config.ShowWatermark,compact=config.Compact;
      var slider=(Slider)dialog.FindName("PortraitOpacitySlider");
      var backgroundColor=((SolidColorBrush)window.Resources["WidgetBackgroundBrush"]).Color;var accent=currentTheme.Accent;
      try {
        slider.Value=35;SetSwitch(dialog,"WatermarkSwitch",false);
        check(Find<Grid>("PortraitFade").Opacity==.35 && Find<Grid>("CompactPortraitFade").Opacity==.35 && Find<TextBlock>("ArtWatermark").Visibility==Visibility.Collapsed,"The artwork slider and EF switch update both portrait surfaces immediately");
        check(config.BackgroundOpacity==background && ((SolidColorBrush)window.Resources["WidgetBackgroundBrush"]).Color==backgroundColor && Find<TextBlock>("SanityNumber").Opacity==1 && currentTheme.Accent==accent,"Artwork opacity leaves the background, Sanity text and automatic palette unchanged");
        var saved=Storage.Read<Config>("settings.json");
        check(saved.PortraitOpacity==.35 && !saved.ShowWatermark,"Artwork opacity and EF visibility are persisted in the isolated profile");
        config.Compact=false;ApplyLayout(true);window.UpdateLayout();SaveImage(window,Path.Combine(folder,"widget-soft-art.png"));
        config.Compact=true;ApplyLayout(true);Find<Button>("CompactPlayButton").Focus();window.UpdateLayout();SaveImage(window,Path.Combine(folder,"compact-soft-art.png"));
        check(Find<Button>("CompactPlayButton").Visibility==Visibility.Visible && Find<Button>("CompactPlayButton").Width==25 && Find<Button>("CompactPlayButton").Height==25,"Compact launch fits the existing footer without adding a row");
        ((ScrollViewer)dialog.FindName("SettingsScroll")).ScrollToTop();dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-artwork.png"));
        slider.Value=0;check(Find<Grid>("PortraitFade").Opacity==0 && Find<Grid>("CompactPortraitFade").Opacity==0 && Find<Grid>("PortraitFade").OpacityMask!=null,"The portrait can be fully hidden while retaining the smooth edge masks");
        var reopened=SettingsWindow();
        check(((Slider)reopened.FindName("PortraitOpacitySlider")).Value==0 && ((CheckBox)reopened.FindName("WatermarkSwitch")).IsChecked==false,"Reopening settings reflects the saved artwork and EF choices");reopened.Close();
      }finally {slider.Value=previousOpacity*100;SetSwitch(dialog,"WatermarkSwitch",previousWatermark);config.Compact=compact;ApplyLayout(true);SavePosition();}
    }
  }
}
