using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

[assembly:AssemblyTitle("Endfield Widget")]
[assembly:AssemblyDescription("Desktop companion for Arknights: Endfield")]
[assembly:AssemblyProduct("Endfield Widget")]
[assembly:AssemblyVersion("1.0.1.0")]
[assembly:AssemblyFileVersion("1.0.1.0")]
[assembly:AssemblyInformationalVersion("1.0.1")]

namespace EndfieldWidget {
  static class BuildInfo {
    public const string Version="1.0.1";
    public const string Build="2";
    static string hash;
    public static string Hash(){if(hash==null)try{using(var stream=File.OpenRead(Assembly.GetExecutingAssembly().Location))using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}catch{hash="unavailable";}return hash;}
  }
  public sealed class FailureRecord {
    public string Type {get;set;}
    public string[] Sites {get;set;}
    public string AtUtc {get;set;}
    public static FailureRecord From(Exception error) {
      var frames=new StackTrace(error,false).GetFrames()??new StackFrame[0];
      return new FailureRecord{Type=error.GetType().FullName,AtUtc=DateTimeOffset.UtcNow.ToString("o"),Sites=frames.Select(f=>f.GetMethod()).Where(m=>m!=null && m.DeclaringType!=null && m.DeclaringType.Namespace=="EndfieldWidget").Take(8).Select(m=>m.DeclaringType.FullName+"."+m.Name).ToArray()};
    }
    public static void Record(Exception error){try{Storage.Write("failure.json",From(error));}catch{}}
  }
  static class DiagnosticsReport {
    public static string Render(Config c,bool desktopOwned,bool recoveryRegistered,bool recoveredSettings,FailureRecord failure) {
      // Whitelist each value: never serialize Config, account caches, exception messages, process paths or raw logs.
      var report=new Dictionary<string,object>{
        {"Application","Endfield"},{"Version",BuildInfo.Version},{"Build",BuildInfo.Build},{"ExecutableSHA256",BuildInfo.Hash()},
        {"CreatedUtc",DateTimeOffset.UtcNow.ToString("o")},{"Runtime",Environment.Version.ToString()},{"Windows",Environment.OSVersion.Version.ToString()},{"ProcessBits",IntPtr.Size*8},
        {"Mode",c.CurrentMode()},{"Scale",c.Scale},{"CompactScale",c.CompactScale},{"MicroScale",c.MicroScale},{"TextScale",c.TextScale},
        {"Design",c.DesignPreset},{"SanityStyle",c.SanityStyle},{"BackgroundOpacity",c.BackgroundOpacity},{"PortraitOpacity",c.PortraitOpacity},
        {"PositionLocked",c.PositionLocked},{"ClickThrough",c.ClickThrough},{"DesktopOwned",desktopOwned},{"RecoveryRegistered",recoveryRegistered},{"SettingsRecoveredFromBackup",recoveredSettings}
      };
      if(failure!=null) {
        if(failure.Type!=null && failure.Type.Length<160 && Regex.IsMatch(failure.Type,@"^(System|EndfieldWidget)\.[A-Za-z0-9_.+`]+$"))report["LastErrorType"]=failure.Type;
        report["LastErrorSites"]=(failure.Sites??new string[0]).Where(s=>s!=null && s.Length<200 && Regex.IsMatch(s,@"^EndfieldWidget\.[A-Za-z0-9_.+<>`]+$")).Take(8).ToArray();
      }
      return L.T("Endfield — diagnostic report\r\n\r\n")+Json.Encode(report)+"\r\n";
    }
  }
  partial class Widget {
    Func<string,bool> reportExporter;
    string MakeDiagnosticReport(){return DiagnosticsReport.Render(config,desktopSurface!=null && desktopSurface.IsAttached,crashRecovery!=null && crashRecovery.Registered,Storage.SettingsRecovered,Storage.Read<FailureRecord>("failure.json"));}
    bool ExportDiagnosticReport(System.Windows.Window owner) {
      string body=MakeDiagnosticReport();if(reportExporter!=null)return reportExporter(body);
      var save=new Microsoft.Win32.SaveFileDialog{Title=L.T("Save report"),Filter=L.T("Text report (*.txt)|*.txt"),FileName="Endfield-Report-"+Clock.Local(DateTimeOffset.UtcNow).ToString("yyyyMMdd-HHmm")+".txt",AddExtension=true,DefaultExt=".txt",OverwritePrompt=true};
      if(save.ShowDialog(owner)!=true)return false;
      File.WriteAllText(save.FileName,body,new UTF8Encoding(false));return true;
    }
  }
}
