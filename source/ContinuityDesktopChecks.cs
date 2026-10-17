using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckNativeContinuity(Action<bool,string> check) {
      using(var native=CrashRecovery.Native()) {
        bool registered=native.Start(new RecoveryBudget(),false,DateTimeOffset.UtcNow);string command;uint flags;
        check(registered && CrashRecovery.ReadRegistration(out command,out flags) && command=="--recovered" && flags==12,"The actual isolated process registers Windows restart recovery with the intended command and flags");
      }
      string remaining;uint remainingFlags;check(!CrashRecovery.ReadRegistration(out remaining,out remainingFlags),"Disposing recovery removes its native registration from the isolated test process");
      string name="Local\\EndfieldWidget.Test.Activation."+Guid.NewGuid().ToString("N");
      bool originallyVisible=window.IsVisible;int requests=0;
      using(var activation=new InstanceActivation(name)) {
        activation.Bind(()=>{requests++;RequestShowFromLauncher();});window.Hide();
        using(var child=Process.Start(new ProcessStartInfo{FileName=Assembly.GetExecutingAssembly().Location,Arguments="--signal-test "+name,UseShellExecute=false,CreateNoWindow=true})) {
          child.WaitForExit();check(child.ExitCode==0,"A separate launcher process signals the existing instance without creating another widget");
        }
        // Wait for the observable UI result instead of assuming a cold CLR's
        // registered-wait callback always starts within 250 ms under load.
        for(int attempt=0;attempt<100 && !(requests==1 && window.IsVisible && desktopSurface.IsAttached);attempt++)await Task.Delay(20);
        check(requests==1 && window.IsVisible && desktopSurface.IsAttached,"The IPC request restores a hidden widget on its existing desktop layer");
        check(InstanceActivation.Request(name,1),"Repeated launches can reach an already visible instance");await Task.Delay(150);
        check(requests==2 && window.IsVisible,"A repeated show request keeps the visible widget open instead of toggling it off");
      }
      check(!InstanceActivation.Request("Local\\EndfieldWidget.Test.Absent."+Guid.NewGuid().ToString("N"),1),"A missing primary instance fails safely without opening an unrelated app or changing settings");
      if(!originallyVisible)window.Hide();
    }
  }
}
