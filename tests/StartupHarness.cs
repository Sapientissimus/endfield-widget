using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Xml;

namespace EndfieldWidget {
  static class StartupHarness {
    static readonly List<string> lines=new List<string>();
    static int failed;
    static void Check(bool ok,string label){lines.Add((ok?"PASS: ":"FAIL: ")+label);if(!ok)failed++;}
    static bool Throws(Action action){try{action();return false;}catch{return true;}}
    [STAThread] static int Main(string[] args) {
      if(args.Length==2 && args[0]=="--child") {
        File.WriteAllLines(args[1],new[]{Environment.CurrentDirectory,WindowsIdentity.GetCurrent().User.Value,Process.GetCurrentProcess().Id.ToString()},new UTF8Encoding(false));
        for(int i=0;i<100 && !File.Exists(args[1]+".release");i++)Thread.Sleep(100);
        return 0;
      }
      string report=Path.GetFullPath(args[0]);
      try {Policies();Native(Path.GetDirectoryName(report));}
      catch(Exception error){Check(false,"Unexpected "+error.GetType().Name+": "+error.Message);}
      File.WriteAllLines(report,lines,new UTF8Encoding(false));return failed==0?0:1;
    }
    sealed class FakeTasks:IStartupTaskStore {
      public StartupTask Value;
      public int Writes,Deletes;
      public bool FailWrite,FailRead,FailDelete,CorruptNext;
      public StartupTask Read(){if(FailRead)throw new UnauthorizedAccessException();return Value;}
      public void Write(string xml){Writes++;if(FailWrite)throw new IOException();Value=new StartupTask{Xml=CorruptNext?"<wrong/>":xml,Enabled=true};CorruptNext=false;}
      public void Delete(){Deletes++;if(FailDelete)throw new IOException();Value=null;}
    }
    sealed class FakeLegacy:ILegacyStartupStore {
      public string Value;
      public bool Allowed {get;set;}
      public int Deletes;
      public bool FailDelete;
      public string Command {get{return Value;}}
      public void Delete(){Deletes++;if(FailDelete)throw new IOException();Value=null;}
    }
    static void Policies() {
      string executable=Assembly.GetExecutingAssembly().Location,user=WindowsIdentity.GetCurrent().User.Value;
      var tasks=new FakeTasks();var legacy=new FakeLegacy{Allowed=true};
      var startup=new UserStartupRegistration(executable,user,tasks,legacy);
      Check(!startup.Enabled && tasks.Writes==0 && tasks.Deletes==0 && legacy.Deletes==0,"S06 Reading/opening settings never registers, repairs or migrates startup");
      legacy.Value=UserStartupRegistration.Command(executable);
      Check(startup.Enabled,"S07 Existing valid registration is preserved until an explicit switch change");
      legacy.Allowed=false;Check(!startup.Enabled,"S08 Windows-disabled legacy startup is respected");legacy.Allowed=true;
      startup.Set(true);Check(startup.Enabled && tasks.Writes==1 && legacy.Value==null,"S09 Enabling verifies a logon task before removing the legacy entry");
      string valid=tasks.Value.Xml;
      Check(valid.Contains("<Delay>PT15S</Delay>") && valid.Contains("<LogonType>InteractiveToken</LogonType>") && valid.Contains("<RunLevel>LeastPrivilege</RunLevel>"),"S10 Task delays 15 seconds and uses the current interactive user without elevation/password");
      Check(valid.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>") && valid.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>") && valid.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"),"S11 Battery use and the default 72-hour task limit cannot prevent or stop the widget");
      Check(valid.Contains("<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>") && valid.Contains("<RunOnlyIfIdle>false</RunOnlyIfIdle>"),"S12 Logon startup does not wait for network connectivity or idle state");
      Check(StartupDefinition.Matches(valid,executable,user),"S13 The generated task round-trips through validation");
      tasks.Value.Enabled=false;Check(!startup.Enabled,"S14 A task disabled in Windows is reported disabled");tasks.Value.Enabled=true;
      foreach(var replacement in new[]{
        new[]{"<Arguments>--autostart</Arguments>","<Arguments>--wrong</Arguments>"},
        new[]{"<DisallowStartIfOnBatteries>false", "<DisallowStartIfOnBatteries>true"},
        new[]{"<StopIfGoingOnBatteries>false", "<StopIfGoingOnBatteries>true"},
        new[]{"<ExecutionTimeLimit>PT0S", "<ExecutionTimeLimit>PT72H"},
        new[]{"<RunOnlyIfNetworkAvailable>false", "<RunOnlyIfNetworkAvailable>true"},
        new[]{"<RunOnlyIfIdle>false", "<RunOnlyIfIdle>true"},
        new[]{"<RunLevel>LeastPrivilege", "<RunLevel>HighestAvailable"},
        new[]{"<Delay>PT15S", "<Delay>PT5H"},
        new[]{"<LogonType>InteractiveToken", "<LogonType>Password"}
      })Check(!StartupDefinition.Matches(valid.Replace(replacement[0],replacement[1]),executable,user),"S15 Invalid launch condition rejected: "+replacement[0]);
      Check(!StartupDefinition.Matches(valid,executable,"S-1-5-21-1-2-3-999") && !StartupDefinition.Matches(valid,executable+"-missing",user),"S16 Another user or executable cannot masquerade as enabled startup");
      Check(!StartupDefinition.Matches("<!DOCTYPE Task [<!ENTITY x SYSTEM 'file:///missing'>]><Task>&x;</Task>",executable,user) && !StartupDefinition.Matches("<Task",executable,user),"S17 Malformed/external-entity XML is rejected without parsing external files");
      startup.Set(false);Check(!startup.Enabled && tasks.Value==null && legacy.Value==null,"S18 Disabling removes both registration mechanisms");
      legacy.Value=UserStartupRegistration.Command(executable);tasks.FailWrite=true;
      Check(Throws(()=>startup.Set(true)) && tasks.Value==null && legacy.Value!=null,"S19 Registration failure leaves the legacy entry intact");tasks.FailWrite=false;
      tasks.CorruptNext=true;Check(Throws(()=>startup.Set(true)) && tasks.Value==null && legacy.Value!=null,"S20 Failed read-back rolls back the new task before touching the old entry");
      legacy.FailDelete=true;Check(Throws(()=>startup.Set(true)) && tasks.Value==null && legacy.Value!=null,"S21 Failed migration cleans up the new task and preserves the prior startup setting");legacy.FailDelete=false;
      startup.Set(true);string previous=tasks.Value.Xml;legacy.FailDelete=true;
      Check(Throws(()=>startup.Set(true)) && tasks.Value.Xml==previous && startup.Enabled,"S22 Failed update restores an existing task definition");legacy.FailDelete=false;
      tasks.FailDelete=true;Check(Throws(()=>startup.Set(false)) && startup.Enabled,"S23 A failed disable stays visibly enabled rather than claiming success");tasks.FailDelete=false;startup.Set(false);
      string temp=Path.Combine(Path.GetDirectoryName(executable),"widget & 日本語 paths.exe");File.Copy(executable,temp);
      try {string xml=StartupDefinition.Create(temp,user);Check(StartupDefinition.Matches(xml,temp,user) && xml.Contains("&amp;"),"S24 Paths with spaces, Unicode text and ampersands survive XML escaping");}finally{File.Delete(temp);}
      string profile=Path.Combine(Path.GetDirectoryName(executable),"isolated-log");Storage.DirectoryPath=profile;Storage.Isolated=true;
      StartupLaunchRecord.Save("failed",false,false,new IOException("PRIVATE-TEST-MESSAGE"));string log=File.ReadAllText(Path.Combine(profile,"automatic-start.json"));
      Check(log.Contains("System.IO.IOException") && !log.Contains("PRIVATE-TEST-MESSAGE") && !log.Contains(executable),"S25 Last-start diagnostics store only stage/version/time and safe status, never raw errors or paths");
      Check(UserStartupRegistration.TaskName(user)!=UserStartupRegistration.TaskName("another-user"),"S26 Startup task names are scoped to the current user");
      tasks.FailRead=true;Check(Throws(()=>{bool unused=startup.Enabled;}),"S34 Service/access failures are surfaced instead of being mistaken for disabled startup");tasks.FailRead=false;
      Check(!StartupDefinition.Matches(valid.Replace("</LogonTrigger>","<EndBoundary>2020-01-01T00:00:00</EndBoundary></LogonTrigger>"),executable,user),"S35 An expired logon trigger is rejected");
    }
    static void Native(string folder) {
      string user=WindowsIdentity.GetCurrent().User.Value,name="EndfieldWidget-Test-"+Guid.NewGuid().ToString("N");
      var store=new WindowsStartupTaskStore(name,user);string child=Path.Combine(folder,"native-child.txt");
      string executable=Assembly.GetExecutingAssembly().Location,xml=StartupDefinition.Create(executable,user);
      var doc=new XmlDocument();doc.LoadXml(xml);var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("t","http://schemas.microsoft.com/windows/2004/02/mit/task");
      // Register a DISABLED logon trigger: this temporary test can only run on demand.
      doc.SelectSingleNode("//t:LogonTrigger/t:Enabled",ns).InnerText="false";
      doc.SelectSingleNode("//t:Exec/t:Arguments",ns).InnerText="--child \""+child+"\"";
      try {
        Check(store.Read()==null,"S27 Native COM distinguishes a missing task from a service/permission failure");
        store.Write(doc.OuterXml);var saved=store.Read();Check(saved!=null && saved.Enabled,"S28 Native Task Scheduler accepts the standard-user definition and reads it back");
        File.WriteAllText(Path.Combine(folder,"native-task.xml"),saved.Xml,new UTF8Encoding(false));
        var savedDoc=new XmlDocument();savedDoc.LoadXml(saved.Xml);var savedNs=new XmlNamespaceManager(savedDoc.NameTable);savedNs.AddNamespace("t","http://schemas.microsoft.com/windows/2004/02/mit/task");
        Check(savedDoc.SelectSingleNode("//t:LogonTrigger/t:Enabled",savedNs).InnerText=="false","S29 Native test logon trigger stays disabled; no persistent real startup is enabled");
        savedDoc.SelectSingleNode("//t:LogonTrigger/t:Enabled",savedNs).InnerText="true";savedDoc.SelectSingleNode("//t:Exec/t:Arguments",savedNs).InnerText="--autostart";
        Check(StartupDefinition.Matches(savedDoc.OuterXml,executable,user),"S30 Production validation accepts Task Scheduler's normalized XML");
        object service=null,root=null,task=null,running=null;
        try {
          service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));WindowsStartupTaskStore.Call(service,"Connect");root=WindowsStartupTaskStore.Call(service,"GetFolder",@"\");task=WindowsStartupTaskStore.Call(root,"GetTask",name);running=WindowsStartupTaskStore.Call(task,"Run",new object[]{null});
          for(int i=0;i<150 && !File.Exists(child);i++)Thread.Sleep(100);
          Check(File.Exists(child),"S31 Windows actually launches the isolated child through the task action");
          if(File.Exists(child)) {
            var output=File.ReadAllLines(child);
            Check(output.Length==3 && string.Equals(output[0],Path.GetDirectoryName(executable),StringComparison.OrdinalIgnoreCase) && output[1]==user,"S32 The launched child receives the configured working directory and current user's session");
            using(var childProcess=Process.GetProcessById(int.Parse(output[2]))) {
              store.Delete();Thread.Sleep(100);Check(!childProcess.HasExited,"S36 Disabling/deleting the startup task does not terminate an already running widget");
              File.WriteAllText(child+".release","");Check(childProcess.WaitForExit(5000),"S37 The isolated child exits normally after releasing the test");
            }
          }
        }finally{foreach(var item in new[]{running,task,root,service})if(item!=null && Marshal.IsComObject(item))Marshal.ReleaseComObject(item);}
      }finally{File.WriteAllText(child+".release","");store.Delete();Check(store.Read()==null,"S33 The isolated native task is removed after the test");}
      lines.Add("ISOLATION: Fake policy stores; one uniquely named native task with a disabled trigger and an offline child, removed in finally. No personal profile, token, real startup entry, clipboard, game or notifications.");
    }
  }
}
