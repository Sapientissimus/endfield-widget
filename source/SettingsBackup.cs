using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace EndfieldWidget {
  static partial class Storage {
    static readonly object writeLock=new object();
    public static bool SettingsRecovered;
    public static string SettingsProblem="";
    static Config ParseSettings(string text) {
      try {
        var map=Json.Decode(text) as IDictionary;if(map==null || map.Count==0)return null;
        if(!map.Keys.Cast<object>().OfType<string>().Any(name=>typeof(Config).GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.IgnoreCase)!=null))return null;
        return new JavaScriptSerializer().Deserialize<Config>(text);
      } catch {return null;}
    }
    static Config ReadSettingsFile(string path){try{return ParseSettings(File.ReadAllText(path,Encoding.UTF8));}catch{return null;}}
    public static Config ReadBackup(){return ReadSettingsFile(Path.Combine(DirectoryPath,"settings.json.bak"));}
    static Config ReadSettings() {
      lock(writeLock) {
        SettingsRecovered=false;SettingsProblem="";
        string path=Path.Combine(DirectoryPath,"settings.json");var current=ReadSettingsFile(path);if(current!=null)return current;
        var backup=ReadBackup();if(backup!=null){SettingsRecovered=true;return backup;}
        if(File.Exists(path)){SettingsProblem=L.T("Settings are damaged. No backup is available.");}
        return null;
      }
    }
    internal static void AtomicWrite(string path,string text) {
      string temp=path+".tmp."+Guid.NewGuid().ToString("N");
      try {
        File.WriteAllText(temp,text,new UTF8Encoding(false));
        if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
      } finally {try{if(File.Exists(temp))File.Delete(temp);}catch{}}
    }
    static void WriteSettings(Config current) {
      string path=Path.Combine(DirectoryPath,"settings.json"),backupPath=path+".bak",body=Json.Encode(current);
      Config previous=ReadSettingsFile(path),existingBackup=ReadBackup();
      string oldBody=null;try{oldBody=File.ReadAllText(path,Encoding.UTF8);}catch{}
      if(previous!=null && oldBody!=body) {
        // The backup preserves appearance, while explicit account changes also update its protected credentials.
        previous.ProtectedToken=current.ProtectedToken;previous.RoleId=current.RoleId;
        AtomicWrite(backupPath,Json.Encode(previous));
      } else if(existingBackup!=null && (existingBackup.ProtectedToken!=current.ProtectedToken || existingBackup.RoleId!=current.RoleId)) {
        existingBackup.ProtectedToken=current.ProtectedToken;existingBackup.RoleId=current.RoleId;AtomicWrite(backupPath,Json.Encode(existingBackup));
      } else if(existingBackup==null && previous!=null)AtomicWrite(backupPath,Json.Encode(previous));
      if(oldBody!=body)AtomicWrite(path,body);
      if(ReadBackup()==null)AtomicWrite(backupPath,body);
    }
  }
}
