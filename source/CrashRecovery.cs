using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace EndfieldWidget {
  public sealed class RecoveryBudget {
    public long WindowStart {get;set;}
    public int Attempts {get;set;}
    public bool Begin(bool recovered,DateTimeOffset now) {
      long seconds=now.ToUnixTimeSeconds();
      if(!recovered || WindowStart<=0 || seconds<WindowStart || seconds-WindowStart>=900){WindowStart=seconds;Attempts=0;}
      if(recovered)Attempts=Math.Min(100,Math.Max(0,Attempts)+1);
      return Attempts<2;
    }
  }
  sealed class CrashRecovery:IDisposable {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int RegisterApplicationRestart(string command,uint flags);
    [DllImport("kernel32.dll")]static extern int UnregisterApplicationRestart();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetApplicationRestartSettings(IntPtr process,StringBuilder command,ref uint length,out uint flags);
    public const uint Flags=4|8;
    public bool Registered {get;private set;}
    public static bool RuntimeStarted;
    readonly Func<string,uint,int> register;readonly Action unregister;
    public CrashRecovery(Func<string,uint,int> add,Action remove){register=add;unregister=remove;}
    public static CrashRecovery Native(){return new CrashRecovery(RegisterApplicationRestart,()=>UnregisterApplicationRestart());}
    public bool Start(RecoveryBudget budget,bool recovered,DateTimeOffset now) {
      if(!budget.Begin(recovered,now))return false;
      try{Registered=register("--recovered",Flags)==0;}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
      return Registered;
    }
    public static bool ReadRegistration(out string command,out uint flags) {
      var buffer=new StringBuilder(1024);uint length=(uint)buffer.Capacity;
      using(var process=System.Diagnostics.Process.GetCurrentProcess()){int result=GetApplicationRestartSettings(process.Handle,buffer,ref length,out flags);command=buffer.ToString();return result==0;}
    }
    public void Dispose(){if(Registered){try{unregister();}catch{}Registered=false;}}
  }
  partial class Widget {
    CrashRecovery crashRecovery;
    public void EnableCrashRecovery(bool recovered) {
      if(preview || Storage.Isolated)return;
      var budget=Storage.Read<RecoveryBudget>("recovery.json")??new RecoveryBudget();crashRecovery=CrashRecovery.Native();
      crashRecovery.Start(budget,recovered,DateTimeOffset.UtcNow);try{Storage.Write("recovery.json",budget);}catch{}
    }
  }
}
