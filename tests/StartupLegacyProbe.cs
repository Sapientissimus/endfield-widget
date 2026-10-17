using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace EndfieldWidget {
  static class StartupLegacyProbe {
    [DllImport("advapi32.dll")] static extern int RegOverridePredefKey(IntPtr key,IntPtr replacement);
    [STAThread] static int Main(string[] args) {
      var lines=new List<string>();int failed=0;
      Action<bool,string> check=(ok,label)=>{lines.Add((ok?"PASS: ":"FAIL: ")+label);if(!ok)failed++;};
      string temporary=@"Software\EndfieldWidget.Tests\Startup-"+Guid.NewGuid().ToString("N");
      var user=Registry.CurrentUser;RegistryKey fake=null;
      try {
        fake=user.CreateSubKey(temporary);
        if(RegOverridePredefKey(new IntPtr(unchecked((int)0x80000001)),fake.Handle.DangerousGetHandle())!=0)throw new InvalidOperationException("Registry isolation unavailable");
        string executable=Assembly.GetExecutingAssembly().Location;
        var startup=new UserStartupRegistration();
        var storeType=Assembly.GetExecutingAssembly().GetType("EndfieldWidget.WindowsStartupTaskStore");
        if(storeType!=null) {
          // New sources: read a unique absent test task, never the actual user's task.
          string sid=WindowsIdentity.GetCurrent().User.Value;
          var store=Activator.CreateInstance(storeType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{"EndfieldWidget-Test-Missing-"+Guid.NewGuid().ToString("N"),sid},null);
          startup=(UserStartupRegistration)Activator.CreateInstance(typeof(UserStartupRegistration),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{executable,sid,store,null},null);
        }
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
          check(!startup.Enabled,"S01 Missing registration stays disabled; reading creates no launch entry");
          key.SetValue("EndfieldWidget",UserStartupRegistration.Command(executable));
          check(startup.Enabled,"S02 An allowed legacy command for this exact executable is recognized");
          key.SetValue("EndfieldWidget",UserStartupRegistration.Command(Path.Combine(Path.GetDirectoryName(executable),"missing-widget.exe")));
          check(!startup.Enabled,"S03 A missing legacy executable is not reported enabled");
          key.SetValue("EndfieldWidget",UserStartupRegistration.Command(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe")));
          check(!startup.Enabled,"S04 A command for another executable is not reported enabled for this widget");
          key.SetValue("EndfieldWidget",UserStartupRegistration.Command(executable));
          using(var approved=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")) {
            approved.SetValue("EndfieldWidget",new byte[]{3,0,0,0,0,0,0,0,0,0,0,0},RegistryValueKind.Binary);
            check(!startup.Enabled,"S05 An existing Windows-disabled legacy entry is not reported enabled");
          }
        }
        lines.Add("ISOLATION: Only this process's HKCU alias was redirected to a temporary non-startup subtree; no real Run entry or startup-approval value was changed.");
      }catch(Exception error){check(false,"Unexpected "+error.GetType().Name+": "+error.Message);}
      finally {
        RegOverridePredefKey(new IntPtr(unchecked((int)0x80000001)),IntPtr.Zero);
        if(fake!=null)fake.Dispose();user.DeleteSubKeyTree(temporary,false);
        File.WriteAllLines(args[0],lines,new UTF8Encoding(false));
      }
      return failed==0?0:1;
    }
  }
}
