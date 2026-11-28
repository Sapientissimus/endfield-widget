using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    internal void ReleaseTest(string reportPath, bool gallery = false) {
      window.Left=window.Top=-10000;window.Show();
      window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var report=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);report.Add("PASS: "+label);};
        string folder=Path.GetDirectoryName(Path.GetFullPath(reportPath));
        try {
          check(Storage.Isolated && startup is MemoryStartupRegistration && accountToken.Length==0,"Release checks use an isolated profile, no account and no real startup backend");
          var executable = FileVersionInfo.GetVersionInfo(typeof(BuildInfo).Assembly.Location);
          check(executable.ProductVersion == BuildInfo.Version
            && executable.FileVersion == BuildInfo.Version + "." + BuildInfo.Build
            && executable.FilePrivatePart > 0,
            "The tested executable identifies the same version and build as the widget");
          check(L.Code=="en" && config.InterfaceLanguage=="en" && L.Languages[0].Code=="en","A fresh installation starts in English and lists English first");
          if (gallery) {
            await CapturePublicGallery(folder);
            report.Add("PASS: Captured six English views with the current official banner: " + Find<TextBlock>("OperatorName").Text);
            return;
          }
          check(L.Normalize(null)=="en" && L.Normalize("../../outside")=="en" && L.Normalize("uk")=="uk","Missing/invalid choices safely fall back to English; explicit supported choices remain valid");
          check(L.Catalog("en").Count>=335 && L.Catalog("en").Keys.All(k=>!Regex.IsMatch(k,@"[\u0400-\u04ff]")),"All message keys use the English base catalog");
          check(Path.GetFileName(Storage.DirectoryPath)!="EndfieldWidget" && InstanceActivation.Name=="Local\\EndfieldWidget.Show","Diagnostic profile is isolated and the application identifier is neutral");
          DemoReading();Paint();window.UpdateLayout();
          check(Find<TextBlock>("PlayLabel").Text==L.T("Play"),"The fresh widget displays its English game action");
          SaveImage(window,Path.Combine(folder,"release-widget.png"));
          var originalBanners = bannerState;
          var originalAccent = currentTheme.Accent;
          var alternate = originalBanners.Debuts.Single(d => d.Key == "typhoeus");
          bannerState = new BannerState { Debuts = new List<Debut> { alternate } };
          sanity.Current = 298;
          sanity.Maximum = 360;
          Paint();
          await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
          check(Find<TextBlock>("OperatorName").Text == alternate.Name.ToUpper(L.Culture)
            && currentArtKey == alternate.Key && currentTheme.Accent != originalAccent,
            "Switching to the other official portrait updates the operator name and accent on screen");
          check(new[] { "OperatorName", "BannerLabel", "SanityNumber", "SanityMaximum", "SanityPercent", "RecoveryLabel" }
            .All(name => TextFitsLayout(Find<TextBlock>(name))), "Live banner and Sanity updates retain the full rendered labels");
          SaveImage(window, Path.Combine(folder, "release-widget-alternate.png"));
          bannerState = originalBanners;
          Paint();
          Settings();settingsDialog.Left=settingsDialog.Top=-10000;((Expander)settingsDialog.FindName("TokenHelp")).IsExpanded=true;settingsDialog.UpdateLayout();
          check(settingsDialog.Title==L.T("Settings · Endfield"),"Settings use the English release title");
          check(((ComboBox)settingsDialog.FindName("LanguageSelector")).Items.Count==15,"All fifteen languages remain available without a preferred regional variant");
          check(((CheckBox)settingsDialog.FindName("StartupSwitch")).IsChecked==false,"A fresh installation leaves Windows startup disabled");
          SaveImage(settingsDialog,Path.Combine(folder,"release-settings.png"));
          var versionLabel = (TextBlock)settingsDialog.FindName("BuildVersionLabel");
          check(versionLabel.Text == "Endfield " + BuildInfo.Version + " (build " + BuildInfo.Build + ")",
            "About displays the version and build of the running executable");
          ((ScrollViewer)settingsDialog.FindName("SettingsScroll")).ScrollToBottom();
          await settingsDialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
          SaveImage(settingsDialog, Path.Combine(folder, "release-about.png"));
          ChangeInterfaceLanguage("uk",settingsDialog);await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          check(L.T("Paste your login token.")=="Вставте токен входу.","The login message follows the selected translation");
          versionLabel = (TextBlock)settingsDialog.FindName("BuildVersionLabel");
          check(versionLabel.Text == "Endfield " + BuildInfo.Version + " (збірка " + BuildInfo.Build + ")",
            "Switching to Ukrainian also localizes the visible build label");
          foreach(string code in new[]{"fr","es-419","pt-BR","it","id","vi","th","ja","ko","zh-Hans","zh-Hant"})check(L.Catalog(code).Count==L.Catalog("en").Count,"Complete release catalog: "+code);
          ChangeInterfaceLanguage("en",settingsDialog);await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          var saved=Storage.Read<Config>("settings.json");check(saved!=null && saved.InterfaceLanguage=="en" && string.IsNullOrEmpty(saved.ProtectedToken),"Language selection persists without adding an account to the release");
          if(settingsDialog!=null)settingsDialog.Close();
          ShowCalendar();calendarDialog.Left=calendarDialog.Top=-10000;calendarDialog.UpdateLayout();
          check(calendarDialog.Title==L.T("Calendar · Endfield"),"The calendar uses the English release title");
          SaveImage(calendarDialog,Path.Combine(folder,"release-calendar.png"));
          report.Add("ALL RELEASE CHECKS PASSED. Offline and isolated; no personal account, clipboard, real startup, game or notifications.");
        }catch(Exception error){report.Add("FAILED: "+error);Environment.ExitCode=1;}
        finally{if(settingsDialog!=null)settingsDialog.Close();if(calendarDialog!=null)calendarDialog.Close();File.WriteAllLines(reportPath,report,new UTF8Encoding(false));Exit();}
      }));
    }

    async Task CapturePublicGallery(string folder) {
      if (!Storage.Isolated || Publications.Locale != "en-us")
        throw new InvalidOperationException("Public gallery capture requires an isolated English profile.");
      var calendarRequest = banners.FetchCalendar(null);
      schedule = await news.Fetch();
      bannerState = await banners.Fetch(bannerState, schedule, false);
      calendarFeed = await calendarRequest;
      DemoReading();
      // Keep the sample reading's recovery interval and use the larger Sanity cap.
      sanity.Current = 298;
      sanity.Maximum = 360;
      Paint();
      var featured = BannerClient.Select(bannerState, DateTimeOffset.UtcNow, false);
      if (featured == null || currentArtKey != BannerClient.ArtKey(featured.Key) || Find<Image>("OperatorImage").Source == null)
        throw new InvalidOperationException("Could not capture the current featured operator.");

      foreach (var mode in new[] { "full", "compact", "micro" }) {
        SetWidgetMode(mode);
        Paint();
        Keyboard.ClearFocus();
        window.Left = window.Top = -10000;
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        SaveImage(window, Path.Combine(folder, mode == "full" ? "widget.png" : mode + ".png"));
      }
      SetWidgetMode("full");
      Paint();
      await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
      var settings = SettingsWindow();
      try {
        settings.WindowStartupLocation = WindowStartupLocation.Manual;
        settings.Left = settings.Top = -10000;
        settings.Show();
        await settings.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        SaveImage(settings, Path.Combine(folder, "settings.png"));
      } finally { settings.Close(); }

      globalToggleKey = globalMenuKey = true;
      using (var menu = CreateTrayMenu()) {
        UpdateTrayPersonalization();
        UpdateTrayShortcuts(menu);
        SaveTrayMenu(menu, Path.Combine(folder, "tray.png"));
      }

      calendarArtworkTransportOverride = http;
      ShowCalendar();
      calendarDialog.Width = 1160;
      calendarDialog.Height = 790;
      var now = calendarNow();
      var today = Clock.Local(now).Date;
      var bannerDay = Clock.Local(featured.EuropeStart).Date;
      var selectedDay = bannerDay.Year == today.Year && bannerDay.Month == today.Month
        && calendarSnapshot.OnDay(bannerDay).Any(entry => entry.Kind == CalendarKind.Debut && entry.Name == featured.Name)
        ? bannerDay : calendarSnapshot.NearestDay(now);
      if (selectedDay.HasValue) SelectCalendarDay(selectedDay.Value);
      calendarDialog.UpdateLayout();
      LayoutCalendar();
      var hero = CalendarFind<Image>("CalendarHeroImage");
      var watch = Stopwatch.StartNew();
      while (watch.Elapsed < TimeSpan.FromSeconds(20)) {
        await Task.Delay(120);
        // Each thumbnail is collapsed until its bitmap has finished loading.
        var cards = CalendarVisualElements<CalendarArtworkImage>(calendarDialog)
          .Where(image => image.IsLoaded && image.DataContext is CalendarCardModel
              && !string.IsNullOrEmpty(((CalendarCardModel)image.DataContext).ArtworkRequestedUrl)).ToList();
        if (hero.Source != null && cards.Count > 0 && cards.All(image => image.Source != null)) {
          SaveImage(calendarDialog, Path.Combine(folder, "calendar.png"));
          return;
        }
      }
      throw new InvalidOperationException("Official artwork was unavailable for the English calendar screenshot.");
    }

  }
}
