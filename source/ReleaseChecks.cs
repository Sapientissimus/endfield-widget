using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    internal void ReleaseTest(string reportPath) {
      window.Left=window.Top=-10000;window.Show();
      window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var report=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);report.Add("PASS: "+label);};
        string folder=Path.GetDirectoryName(Path.GetFullPath(reportPath));
        try {
          check(Storage.Isolated && startup is MemoryStartupRegistration && accountToken.Length==0,"Release checks use an isolated profile, no account and no real startup backend");
          check(BuildInfo.Version=="1.2.0" && BuildInfo.Build=="4","User-facing version matches release 1.2.0");
          check(L.Code=="en" && config.InterfaceLanguage=="en" && L.Languages[0].Code=="en","A fresh installation starts in English and lists English first");
          check(L.Normalize(null)=="en" && L.Normalize("../../outside")=="en" && L.Normalize("uk")=="uk","Missing/invalid choices safely fall back to English; explicit supported choices remain valid");
          check(L.Catalog("en").Count>=335 && L.Catalog("en").Keys.All(k=>!Regex.IsMatch(k,@"[\u0400-\u04ff]")),"All message keys use the English base catalog");
          check(Path.GetFileName(Storage.DirectoryPath)!="EndfieldWidget" && InstanceActivation.Name=="Local\\EndfieldWidget.Show","Diagnostic profile is isolated and the application identifier is neutral");
          DemoReading();Paint();window.UpdateLayout();
          check(Find<TextBlock>("PlayLabel").Text==L.T("Play"),"The fresh widget displays its English game action");
          SaveImage(window,Path.Combine(folder,"release-widget.png"));
          Settings();settingsDialog.Left=settingsDialog.Top=-10000;((Expander)settingsDialog.FindName("TokenHelp")).IsExpanded=true;settingsDialog.UpdateLayout();
          check(settingsDialog.Title==L.T("Settings · Endfield"),"Settings use the English release title");
          check(((ComboBox)settingsDialog.FindName("LanguageSelector")).Items.Count==15,"All fifteen languages remain available without a preferred regional variant");
          check(((CheckBox)settingsDialog.FindName("StartupSwitch")).IsChecked==false,"A fresh installation leaves Windows startup disabled");
          SaveImage(settingsDialog,Path.Combine(folder,"release-settings.png"));
          ChangeInterfaceLanguage("uk",settingsDialog);await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          check(L.T("Paste your login token.")=="Вставте токен входу.","The login message follows the selected translation");
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
  }
}
