using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace EndfieldWidget {
  public partial class Config {
    // Empty means automatic detection. Never persist a machine-specific detected path.
    public string GameExecutablePath {get;set;}
  }

  enum GameLaunchKind {Steam,Launcher,Manual}

  sealed class GameLaunchTarget {
    public readonly GameLaunchKind Kind;
    public readonly string Executable;
    public readonly string GameDirectory;
    public GameLaunchTarget(GameLaunchKind kind,string executable,string directory=null){Kind=kind;Executable=executable;GameDirectory=directory;}
    public ProcessStartInfo Command() {
      if(!File.Exists(Executable))throw new FileNotFoundException("The selected launch target is unavailable.");
      if(Kind==GameLaunchKind.Steam)return SteamGameLauncher.Command(Executable);
      return new ProcessStartInfo{FileName=Executable,Arguments="",WorkingDirectory=Path.GetDirectoryName(Executable),UseShellExecute=true};
    }
  }

  sealed class GamePathException:Exception { }

  // Valve KeyValues are nested, not JSON. Parse bounded input so comments, escaped
  // backslashes and old/new library layouts cannot turn into false install paths.
  static class SteamKeyValues {
    sealed class Reader {
      readonly string text;int offset,nodes;
      public Reader(string value){text=value;}
      void Space(){while(offset<text.Length){if(char.IsWhiteSpace(text[offset]) || text[offset]=='\ufeff'){offset++;continue;}if(text[offset]=='/' && offset+1<text.Length && text[offset+1]=='/'){while(offset<text.Length && text[offset]!='\n')offset++;continue;}break;}}
      string Quoted() {
        Space();if(offset>=text.Length || text[offset++]!='"')throw new FormatException();
        var value=new StringBuilder();
        while(offset<text.Length){char ch=text[offset++];if(ch=='"')return value.ToString();if(ch=='\\' && offset<text.Length && (text[offset]=='\\' || text[offset]=='"'))ch=text[offset++];value.Append(ch);}
        throw new FormatException();
      }
      public Dictionary<string,object> Object(bool nested,int depth) {
        if(depth>16)throw new FormatException();var result=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
        while(true) {
          Space();if(offset==text.Length){if(nested)throw new FormatException();return result;}
          if(text[offset]=='}'){if(!nested)throw new FormatException();offset++;return result;}
          if(++nodes>10000)throw new FormatException();string key=Quoted();Space();
          if(offset<text.Length && text[offset]=='{'){offset++;result[key]=Object(true,depth+1);}else result[key]=Quoted();
        }
      }
    }
    public static Dictionary<string,object> Read(string path) {
      try{var file=new FileInfo(path);if(!file.Exists || file.Length>262144)return null;return new Reader(File.ReadAllText(path,Encoding.UTF8)).Object(false,0);}catch{return null;}
    }
    public static object Get(Dictionary<string,object> map,string key){object value;return map!=null && map.TryGetValue(key,out value)?value:null;}
  }

  static class GameInstallations {
    static readonly string[] LauncherNames={"Launcher.exe","GRYPHLINK.exe","Games.exe"};
    static string AbsolutePath(string value) {
      if(string.IsNullOrWhiteSpace(value))return null;
      try {
        string path=Environment.ExpandEnvironmentVariables(value.Trim());
        if(path.Length>=2 && path[0]=='"' && path[path.Length-1]=='"')path=path.Substring(1,path.Length-2);
        if(!Regex.IsMatch(path,@"^(?:[a-zA-Z]:[\\/]|\\\\(?![?.]))") || path.IndexOf('"')>=0)return null;
        return Path.GetFullPath(path);
      }catch{return null;}
    }
    public static GameLaunchTarget Manual(string value) {
      string path=AbsolutePath(value);
      if(path==null || !string.Equals(Path.GetExtension(path),".exe",StringComparison.OrdinalIgnoreCase) || !File.Exists(path))throw new GamePathException();
      return new GameLaunchTarget(GameLaunchKind.Manual,path);
    }
    public static IEnumerable<string> Libraries(string steamDirectory) {
      var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string root=AbsolutePath(steamDirectory);
      if(root==null)return paths;paths.Add(root);
      foreach(string relative in new[]{@"steamapps\libraryfolders.vdf",@"config\libraryfolders.vdf"}) {
        var document=SteamKeyValues.Read(Path.Combine(root,relative));
        var libraries=SteamKeyValues.Get(document,"libraryfolders") as Dictionary<string,object>;
        if(libraries==null)continue;
        foreach(var entry in libraries) {
          uint index;if(!uint.TryParse(entry.Key,out index))continue;
          var item=entry.Value as Dictionary<string,object>;
          string path=AbsolutePath(item==null?entry.Value as string:SteamKeyValues.Get(item,"path") as string);
          if(path!=null)paths.Add(path);
        }
      }
      return paths;
    }
    public static GameLaunchTarget Steam(string client) {
      string executable=SteamGameLauncher.SelectExecutable(new[]{client});if(executable==null)return null;
      foreach(string library in Libraries(Path.GetDirectoryName(executable))) {
        string apps=Path.Combine(library,"steamapps");
        var manifest=SteamKeyValues.Get(SteamKeyValues.Read(Path.Combine(apps,"appmanifest_"+SteamGameLauncher.AppId+".acf")),"AppState") as Dictionary<string,object>;
        if(!string.Equals(SteamKeyValues.Get(manifest,"appid") as string,SteamGameLauncher.AppId,StringComparison.Ordinal))continue;
        string name=SteamKeyValues.Get(manifest,"installdir") as string;
        // installdir is one directory name, never a rooted path or a traversal.
        if(string.IsNullOrWhiteSpace(name) || name=="." || name==".." || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)continue;
        try{string game=Path.Combine(apps,"common",name);if(File.Exists(Path.Combine(game,"Endfield.exe")))return new GameLaunchTarget(GameLaunchKind.Steam,executable,game);}catch{}
      }
      return null;
    }
    public static string RegisteredExecutable(string value) {
      if(string.IsNullOrWhiteSpace(value))return null;
      var match=Regex.Match(value,@"^\s*(?:""([^""]+\.exe)""|(.+?\.exe))(?=\s|,|$)",RegexOptions.IgnoreCase);
      return match.Success?AbsolutePath(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value):null;
    }
    static bool SteamPayload(string path){return path.Replace('/','\\').IndexOf(@"\steamapps\common\",StringComparison.OrdinalIgnoreCase)>=0;}
    public static GameLaunchTarget Standalone(IEnumerable<string> locations) {
      string directGame=null;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(string value in locations) {
        string path=AbsolutePath(value);if(path==null || SteamPayload(path) || !seen.Add(path))continue;
        try {
          bool file=File.Exists(path);string directory=file?Path.GetDirectoryName(path):path;
          if(file && LauncherNames.Contains(Path.GetFileName(path),StringComparer.OrdinalIgnoreCase))return new GameLaunchTarget(GameLaunchKind.Launcher,path);
          var roots=new List<string>{directory};
          if(File.Exists(Path.Combine(directory,"Endfield.exe"))) {
            directGame=directGame??Path.Combine(directory,"Endfield.exe");
            // An official game folder may sit two levels below its launcher.
            if(string.Equals(new DirectoryInfo(directory).Name,"Endfield Game",StringComparison.OrdinalIgnoreCase)) {
              var parent=Directory.GetParent(directory);if(parent!=null){roots.Add(parent.FullName);if(parent.Parent!=null)roots.Add(parent.Parent.FullName);}
            }
          }
          foreach(string root in roots)foreach(string name in LauncherNames)foreach(string relative in new[]{name,Path.Combine("launcher",name)}) {
            string candidate=Path.Combine(root,relative);if(File.Exists(candidate))return new GameLaunchTarget(GameLaunchKind.Launcher,candidate);
          }
        }catch{}
      }
      return directGame==null?null:new GameLaunchTarget(GameLaunchKind.Launcher,directGame);
    }
    static IEnumerable<string> RegisteredLocations() {
      var paths=new List<string>();
      foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
        try{using(var root=RegistryKey.OpenBaseKey(hive,view)) {
          using(var uninstall=root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))if(uninstall!=null)foreach(string name in uninstall.GetSubKeyNames()) {
            try{using(var app=uninstall.OpenSubKey(name)) {
              if(app==null)continue;string title=app.GetValue("DisplayName") as string??"";
              if(title.IndexOf("Endfield",StringComparison.OrdinalIgnoreCase)<0 && title.IndexOf("GRYPHLINK",StringComparison.OrdinalIgnoreCase)<0 && title.IndexOf("GRYPHLINE",StringComparison.OrdinalIgnoreCase)<0 && title.IndexOf("Hypergryph Launcher",StringComparison.OrdinalIgnoreCase)<0)continue;
              paths.Add(app.GetValue("InstallLocation") as string);paths.Add(RegisteredExecutable(app.GetValue("DisplayIcon") as string));
            }}catch{}
          }
          using(var classes=root.OpenSubKey(@"Software\Classes"))if(classes!=null)foreach(string name in classes.GetSubKeyNames()) {
            if(!name.StartsWith("gryph",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("hypergryph",StringComparison.OrdinalIgnoreCase) && !name.StartsWith("endfield",StringComparison.OrdinalIgnoreCase))continue;
            try{using(var command=classes.OpenSubKey(name+@"\shell\open\command"))if(command!=null)paths.Add(RegisteredExecutable(command.GetValue("") as string));}catch{}
          }
        }}catch{}
      }
      return paths;
    }
    static IEnumerable<string> CommonLocations() {
      var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var folder in new[]{Environment.SpecialFolder.ProgramFiles,Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.LocalApplicationData}) {
        string path=Environment.GetFolderPath(folder);if(!string.IsNullOrEmpty(path)){roots.Add(path);roots.Add(Path.Combine(path,"Programs"));}
      }
      try{foreach(var drive in DriveInfo.GetDrives())if(drive.DriveType==DriveType.Fixed && drive.IsReady){roots.Add(drive.RootDirectory.FullName);roots.Add(Path.Combine(drive.RootDirectory.FullName,"Games"));}}catch{}
      foreach(string root in roots)foreach(string name in new[]{"GRYPHLINK","GRYPHLINE","Hypergryph Launcher"})yield return Path.Combine(root,name);
    }
    public static GameLaunchTarget Select(string manual,IEnumerable<string> steamClients,IEnumerable<string> standaloneLocations) {
      if(!string.IsNullOrWhiteSpace(manual))return Manual(manual);
      foreach(string client in steamClients){var target=Steam(client);if(target!=null)return target;}
      return Standalone(standaloneLocations);
    }
    public static GameLaunchTarget Resolve(string manual) {
      if(!string.IsNullOrWhiteSpace(manual))return Manual(manual);
      var steam=Steam(SteamGameLauncher.FindExecutable());
      return steam??Standalone(RegisteredLocations().Concat(CommonLocations()));
    }
    public static void Launch(GameLaunchTarget target) {
      if(target==null)throw new FileNotFoundException("Endfield was not found.");
      using(var process=Process.Start(target.Command()))if(process==null)throw new InvalidOperationException("The game could not be started.");
    }
  }
}
