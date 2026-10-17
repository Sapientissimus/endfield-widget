using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml;
using Microsoft.Win32;

namespace EndfieldWidget {
  sealed class StartupTask {public string Xml;public bool Enabled;}
  interface IStartupTaskStore {StartupTask Read();void Write(string xml);void Delete();}
  interface ILegacyStartupStore {string Command {get;} bool Allowed {get;} void Delete();}

  // Only explicit changes to the settings switch write to either store.
  sealed class UserStartupRegistration:IStartupRegistration {
    readonly IStartupTaskStore tasks;
    readonly ILegacyStartupStore legacy;
    readonly string executable,user;
    public UserStartupRegistration():this(Assembly.GetExecutingAssembly().Location,WindowsIdentity.GetCurrent().User.Value,null,null){}
    internal UserStartupRegistration(string path,string userId,IStartupTaskStore taskStore,ILegacyStartupStore legacyStore) {
      executable=Path.GetFullPath(path);user=userId;
      tasks=taskStore??new WindowsStartupTaskStore(TaskName(user),user);
      legacy=legacyStore??new LegacyStartupStore();
    }
    internal static string TaskName(string userId){return "EndfieldWidget-"+userId;}
    public bool Enabled {
      get {
        var task=tasks.Read();
        if(task!=null)return task.Enabled && StartupDefinition.Matches(task.Xml,executable,user);
        return string.Equals(legacy.Command,Command(executable),StringComparison.OrdinalIgnoreCase) && legacy.Allowed && File.Exists(executable);
      }
    }
    // Retained for reading existing installations and checking legacy path quoting.
    public static string Command(string executable){if(string.IsNullOrEmpty(executable) || executable.Contains("\"") || !Path.IsPathRooted(executable))throw new ArgumentException("Executable path");string command="\""+executable+"\"";if(command.Length>260)throw new InvalidOperationException(L.T("Run the widget from a folder with a shorter path."));return command;}
    public void Set(bool enabled) {
      if(!enabled){legacy.Delete();tasks.Delete();return;}
      if(!File.Exists(executable))throw new FileNotFoundException();
      string xml=StartupDefinition.Create(executable,user);var previous=tasks.Read();
      try {
        tasks.Write(xml);
        var registered=tasks.Read();
        if(registered==null || !registered.Enabled || !StartupDefinition.Matches(registered.Xml,executable,user))throw new InvalidOperationException(L.T("Could not change autostart."));
        // Keep the previous launch entry until the new task has been read back.
        legacy.Delete();
      } catch {
        try{if(previous==null)tasks.Delete();else tasks.Write(previous.Xml);}catch{}
        throw;
      }
    }
  }

  static class StartupDefinition {
    const string Ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
    public static string Create(string executable,string user) {
      UserStartupRegistration.Command(executable);
      var doc=new XmlDocument();var task=doc.CreateElement("Task",Ns);task.SetAttribute("version","1.2");doc.AppendChild(task);
      var info=Add(task,"RegistrationInfo",null);Add(info,"Description","Endfield Widget — start at sign-in");
      var triggers=Add(task,"Triggers",null);var logon=Add(triggers,"LogonTrigger",null);
      Add(logon,"Enabled","true");Add(logon,"UserId",user);Add(logon,"Delay","PT15S");
      var principals=Add(task,"Principals",null);var principal=Add(principals,"Principal",null);principal.SetAttribute("id","User");
      Add(principal,"UserId",user);Add(principal,"LogonType","InteractiveToken");Add(principal,"RunLevel","LeastPrivilege");
      var settings=Add(task,"Settings",null);
      Add(settings,"MultipleInstancesPolicy","IgnoreNew");Add(settings,"DisallowStartIfOnBatteries","false");Add(settings,"StopIfGoingOnBatteries","false");
      Add(settings,"AllowHardTerminate","false");Add(settings,"StartWhenAvailable","true");Add(settings,"RunOnlyIfNetworkAvailable","false");
      var idle=Add(settings,"IdleSettings",null);Add(idle,"StopOnIdleEnd","false");Add(idle,"RestartOnIdle","false");
      Add(settings,"AllowStartOnDemand","true");Add(settings,"Enabled","true");Add(settings,"Hidden","false");Add(settings,"RunOnlyIfIdle","false");Add(settings,"WakeToRun","false");Add(settings,"ExecutionTimeLimit","PT0S");
      var actions=Add(task,"Actions",null);actions.SetAttribute("Context","User");var exec=Add(actions,"Exec",null);
      Add(exec,"Command",executable);Add(exec,"Arguments","--autostart");Add(exec,"WorkingDirectory",Path.GetDirectoryName(executable));
      return doc.OuterXml;
    }
    static XmlElement Add(XmlElement parent,string name,string value){var node=parent.OwnerDocument.CreateElement(name,Ns);if(value!=null)node.InnerText=value;parent.AppendChild(node);return node;}
    static bool SameUser(string candidate,string user) {
      if(string.Equals(candidate,user,StringComparison.OrdinalIgnoreCase))return true;
      using(var current=WindowsIdentity.GetCurrent())return current.User.Value==user && string.Equals(candidate,current.Name,StringComparison.OrdinalIgnoreCase);
    }
    public static bool Matches(string xml,string executable,string user) {
      if(string.IsNullOrEmpty(xml) || xml.Length>100000 || !File.Exists(executable))return false;
      try {
        var doc=new XmlDocument{XmlResolver=null};
        using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=100000}))doc.Load(reader);
        var ns=new XmlNamespaceManager(doc.NameTable);ns.AddNamespace("t",Ns);
        Func<string,string> text=p=>{var node=doc.SelectSingleNode("/t:Task/t:"+p,ns);return node==null?null:node.InnerText;};
        Func<string,int> count=p=>doc.SelectNodes("/t:Task/t:"+p,ns).Count;
        // The service omits default values and can normalize a SID to DOMAIN\name.
        Func<string,bool,bool,bool> flag=(p,expected,defaultValue)=>{string value=text(p);return value==null?defaultValue==expected:expected?(value=="true" || value=="1"):(value=="false" || value=="0");};
        return count("Triggers/*")==1 && count("Actions/*")==1 && count("Principals/*")==1 &&
          SameUser(text("Triggers/t:LogonTrigger/t:UserId"),user) && flag("Triggers/t:LogonTrigger/t:Enabled",true,true) && text("Triggers/t:LogonTrigger/t:Delay")=="PT15S" && count("Triggers/t:LogonTrigger/t:EndBoundary")==0 && count("Triggers/t:LogonTrigger/t:Repetition")==0 &&
          SameUser(text("Principals/t:Principal/t:UserId"),user) && text("Principals/t:Principal/t:LogonType")=="InteractiveToken" && (text("Principals/t:Principal/t:RunLevel")??"LeastPrivilege")=="LeastPrivilege" &&
          string.Equals(text("Actions/t:Exec/t:Command"),executable,StringComparison.OrdinalIgnoreCase) && text("Actions/t:Exec/t:Arguments")=="--autostart" &&
          string.Equals(text("Actions/t:Exec/t:WorkingDirectory"),Path.GetDirectoryName(executable),StringComparison.OrdinalIgnoreCase) &&
          flag("Settings/t:Enabled",true,true) && flag("Settings/t:DisallowStartIfOnBatteries",false,true) && flag("Settings/t:StopIfGoingOnBatteries",false,true) &&
          flag("Settings/t:RunOnlyIfNetworkAvailable",false,false) && flag("Settings/t:RunOnlyIfIdle",false,false) && text("Settings/t:ExecutionTimeLimit")=="PT0S" &&
          (text("Settings/t:MultipleInstancesPolicy")??"IgnoreNew")=="IgnoreNew" && flag("Settings/t:StartWhenAvailable",true,false);
      }catch(XmlException){return false;}
    }
  }

  sealed class LegacyStartupStore:ILegacyStartupStore {
    const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run",Name="EndfieldWidget";
    public string Command {get {using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,false))return key==null?null:key.GetValue(Name) as string;}}
    public bool Allowed {
      get {
        // Read Windows' existing disable flag; never clear the user's Windows choice.
        foreach(var branch in new[]{"Run","Run32"})using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\"+branch,false)) {
          if(key==null)continue;var flag=key.GetValue(Name) as byte[];
          if(flag!=null && (flag.Length<4 || (flag[0]!=2 && flag[0]!=6)))return false;
        }
        return true;
      }
    }
    public void Delete(){using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,true))if(key!=null)key.DeleteValue(Name,false);}
  }

  sealed class WindowsStartupTaskStore:IStartupTaskStore {
    readonly string name,user;
    internal WindowsStartupTaskStore(string taskName,string userId){name=taskName;user=userId;}
    internal static object Invoke(object target,string name,BindingFlags flags,params object[] args) {
      try{return target.GetType().InvokeMember(name,flags|BindingFlags.OptionalParamBinding,null,target,args);}
      catch(TargetInvocationException error){if(error.InnerException!=null)throw error.InnerException;throw;}
    }
    internal static object Call(object target,string method,params object[] args){return Invoke(target,method,BindingFlags.InvokeMethod,args);}
    internal static object Property(object target,string name){return Invoke(target,name,BindingFlags.GetProperty);}
    static void Release(object value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    T WithRoot<T>(Func<object,T> action) {
      object service=null,folder=null;
      try {service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));Call(service,"Connect");folder=Call(service,"GetFolder",@"\");return action(folder);}
      finally{Release(folder);Release(service);}
    }
    static bool Missing(Exception error){return error.HResult==unchecked((int)0x80070002) || error.HResult==unchecked((int)0x8004130F);}
    public StartupTask Read(){return WithRoot(folder=>{
      object task=null;
      try{task=Call(folder,"GetTask",name);return new StartupTask{Xml=(string)Property(task,"Xml"),Enabled=(bool)Property(task,"Enabled")};}
      catch(Exception error){if(Missing(error))return null;throw;}
      finally{Release(task);}
    });}
    public void Write(string xml){WithRoot(folder=>{object task=null;try{task=Call(folder,"RegisterTask",name,xml,6,user,null,3,null);return true;}finally{Release(task);}});}
    public void Delete(){WithRoot(folder=>{try{Call(folder,"DeleteTask",name,0);}catch(Exception error){if(!Missing(error))throw;}return true;});}
  }

  sealed class StartupLaunchRecord {
    public string Version {get;set;}
    public DateTimeOffset AtUtc {get;set;}
    public string Stage {get;set;}
    public bool Visible {get;set;}
    public bool DesktopOwned {get;set;}
    public string ErrorType {get;set;}
    internal static void Save(string stage,bool visible=false,bool owned=false,Exception error=null) {
      // A bounded last-run record; no command, account, credential or exception message.
      Storage.Write("automatic-start.json",new StartupLaunchRecord{Version=BuildInfo.Version,AtUtc=DateTimeOffset.UtcNow,Stage=stage,Visible=visible,DesktopOwned=owned,ErrorType=error==null?null:error.GetType().FullName});
    }
  }
  partial class Widget {
    internal void RecordAutomaticStartup(){window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>StartupLaunchRecord.Save("ready",window.IsVisible,desktopSurface!=null && desktopSurface.IsAttached)));}
  }
}
