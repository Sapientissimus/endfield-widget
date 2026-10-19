using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    static string LaunchFixture(string path,string content="isolated test target; never executed") {
      Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,content);return path;
    }
    static string VdfString(string value){return "\""+value.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";}
    static string InstallManifest(string directory,string appId="4732690"){return "\"AppState\" { \"appid\" "+VdfString(appId)+" \"installdir\" "+VdfString(directory)+" \"StateFlags\" \"4\" }";}
    async Task CheckGameDiscovery(Action<bool,string> check,string folder) {
      string root=Path.Combine(folder,"launch discovery"),steam=LaunchFixture(Path.Combine(root,"Steam client","steam.exe"));
      string library=Path.Combine(root,"Ігри 日本語"),game=LaunchFixture(Path.Combine(library,"steamapps","common","Endfield game","Endfield.exe"));
      string manifest=LaunchFixture(Path.Combine(library,"steamapps","appmanifest_4732690.acf"),InstallManifest("Endfield game"));
      string libraries=LaunchFixture(Path.Combine(Path.GetDirectoryName(steam),"steamapps","libraryfolders.vdf"),"// a comment containing \"path\" \"C:\\\\missing\"\n\"libraryfolders\" { \"1\" { \"path\" "+VdfString(library)+" \"apps\" { \"4732690\" \"100\" } } }");
      string launcher=LaunchFixture(Path.Combine(root,"GRYPHLINK","Launcher.exe"));
      string otherGame=LaunchFixture(Path.Combine(Path.GetDirectoryName(launcher),"games","EndField Game","Endfield.exe"));
      var target=GameInstallations.Select(null,new[]{steam},new[]{launcher});
      check(target!=null && target.Kind==GameLaunchKind.Steam && target.GameDirectory==Path.GetDirectoryName(game),"Automatic detection prefers a verified Steam installation over an installed official launcher, including a separate Unicode library");
      check(target.Command().FileName==steam && target.Command().Arguments=="-applaunch 4732690" && !target.Command().UseShellExecute,"Detected Steam games use Steam app launch, keeping the Steam client and game executable separate");
      check(GameInstallations.Libraries(Path.GetDirectoryName(steam)).Count()==2,"The primary library and modern libraryfolders are included without comment or apps-map false positives");
      File.WriteAllText(libraries,"\"LibraryFolders\" { \"1\" "+VdfString(library)+" }");
      check(GameInstallations.Steam(steam)!=null,"Legacy Steam library files remain supported");
      File.WriteAllText(libraries,"\"libraryfolders\" { \"1\" { \"path\" "+VdfString(library)+" } \"2\" { \"path\" "+VdfString(library)+" } }");
      check(GameInstallations.Libraries(Path.GetDirectoryName(steam)).Count()==2,"Duplicate Steam libraries are checked only once");
      File.WriteAllText(manifest,InstallManifest("Endfield game","111"));
      check(GameInstallations.Select("",new[]{steam},new[]{launcher}).Kind==GameLaunchKind.Launcher,"An unrelated app manifest does not override the available launcher");
      File.WriteAllText(manifest,"\"AppState\" { \"appid\" \"4732690\" \"nested\" { \"installdir\" \"Endfield game\" } }");
      check(GameInstallations.Steam(steam)==null,"An unrelated nested manifest field cannot become the installation directory");
      foreach(string invalid in new[]{"..",@"..\..\outside",@"C:\outside",@"..\common\Endfield game"}) {
        File.WriteAllText(manifest,InstallManifest(invalid));check(GameInstallations.Steam(steam)==null,"Steam rejects rooted or traversing installdir: "+invalid);
      }
      File.WriteAllText(manifest,InstallManifest("missing payload"));
      check(GameInstallations.Steam(steam)==null,"A manifest without the installed game executable does not count as a Steam installation");
      File.WriteAllText(manifest,InstallManifest("Endfield game"));
      File.WriteAllText(libraries,"\"libraryfolders\" { \"1\" { \"path\" "+VdfString(library));
      check(GameInstallations.Select(null,new[]{steam},new[]{launcher}).Kind==GameLaunchKind.Launcher,"Truncated Steam metadata fails safely and permits launcher fallback");
      File.WriteAllText(libraries,new string('x',262145));
      check(GameInstallations.Steam(steam)==null,"Oversized Steam metadata is ignored before parsing");
      File.WriteAllText(libraries,"\"libraryfolders\" { \"1\" { \"path\" "+VdfString(library)+" } }");
      target=GameInstallations.Select("\""+launcher+"\"",new[]{steam},new[]{launcher});
      check(target.Kind==GameLaunchKind.Manual && target.Executable==launcher,"An explicitly saved launcher path takes precedence even when Steam is installed");
      check(target.Command().WorkingDirectory==Path.GetDirectoryName(launcher) && target.Command().Arguments=="" && target.Command().UseShellExecute,"Manual launch preserves a path with spaces, uses its working directory and passes no embedded command arguments");
      check(GameInstallations.Select(game,new[]{steam},new[]{launcher}).Kind==GameLaunchKind.Manual,"A manually selected game executable remains an explicit override");
      foreach(string invalid in new[]{Path.Combine(root,"missing.exe"),Path.GetDirectoryName(game),game+" --arbitrary",@"C:relative.exe","steam://run/4732690",LaunchFixture(Path.Combine(root,"script.cmd"))}) {
        bool rejected=false;try{GameInstallations.Select(invalid,new[]{steam},new[]{launcher});}catch(GamePathException){rejected=true;}check(rejected,"Invalid manual paths are rejected without falling back or interpreting commands: "+Path.GetFileName(invalid));
      }
      check(GameInstallations.Select(null,new[]{Path.Combine(root,"missing Steam","steam.exe")},new[]{Path.GetDirectoryName(otherGame)}).Executable==launcher,"A launcher is found above its default game directory when Steam is unavailable");
      check(GameInstallations.Select(null,new string[0],new string[0])==null,"No installed game or launcher leaves a recoverable not-found result");
      string stalePayload=LaunchFixture(Path.Combine(root,"stale Steam","steamapps","common","Endfield","Endfield.exe"));
      check(GameInstallations.Standalone(new[]{stalePayload})==null,"A stale Steam payload is never silently treated as a standalone installation");
      check(GameInstallations.RegisteredExecutable("\""+launcher+"\",0")==launcher && GameInstallations.RegisteredExecutable("\""+launcher+"\" \"%1\"")==launcher,"Registry icon and URL-protocol values yield only the executable, never a forwarded command or URL");
      check(GameInstallations.RegisteredExecutable(Path.Combine(root,"launcher.ico")+",0")==null,"A launcher icon is not mistaken for a launchable executable");

      var previousResolver=gameTargetResolver;string previousPath=config.GameExecutablePath;Window dialog=null;
      try {
        config.GameExecutablePath="";gameTargetResolver=path=>string.IsNullOrWhiteSpace(path)?new GameLaunchTarget(GameLaunchKind.Steam,steam):GameInstallations.Manual(path);
        dialog=SettingsWindow();dialog.Left=dialog.Top=-10000;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Show();await gameSettingsTask;dialog.UpdateLayout();
        var input=(TextBox)dialog.FindName("GamePathInput");var save=(Button)dialog.FindName("GamePathSave");var automatic=(Button)dialog.FindName("GamePathAutomatic");var status=(TextBlock)dialog.FindName("GameLaunchStatus");
        check(input.Text=="" && status.Text=="Automatic: Steam","A fresh settings dialog uses automatic detection without saving a discovered machine path");
        input.Focus();await TestTextInput(input,launcher);save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await gameSettingsTask;
        check(input.Text==launcher && status.Text=="Manual path" && Storage.Read<Config>("settings.json").GameExecutablePath==launcher,"The real path field accepts text and Save persists the validated manual override");
        input.Text=Path.Combine(root,"missing.exe");save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await gameSettingsTask;
        check(config.GameExecutablePath==launcher && Storage.Read<Config>("settings.json").GameExecutablePath==launcher && ((TextBlock)dialog.FindName("GamePathMessage")).Visibility==Visibility.Visible,"Invalid input stays visible for correction while the previous valid path remains saved");
        dialog.Close();dialog=SettingsWindow();dialog.Left=dialog.Top=-10000;dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Show();await gameSettingsTask;
        check(((TextBox)dialog.FindName("GamePathInput")).Text==launcher,"The saved manual game path survives reopening Settings");
        ((Button)dialog.FindName("GamePathAutomatic")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await gameSettingsTask;
        check(config.GameExecutablePath=="" && Storage.Read<Config>("settings.json").GameExecutablePath=="" && ((TextBlock)dialog.FindName("GameLaunchStatus")).Text=="Automatic: Steam","Use automatic detection clears the override, persists it and restores Steam preference");
        dialog.UpdateLayout();SaveImage(dialog,Path.Combine(folder,"settings-game-launch.png"));
        var delayed=new TaskCompletionSource<GameLaunchTarget>();gameTargetResolver=path=>delayed.Task.GetAwaiter().GetResult();
        dialog.Close();dialog=SettingsWindow();var pending=gameSettingsTask;dialog.Close();delayed.SetResult(new GameLaunchTarget(GameLaunchKind.Launcher,launcher));await pending;
        check(pending.IsCompleted,"A late discovery result is discarded safely after Settings closes");
      }finally{if(dialog!=null)dialog.Close();gameTargetResolver=previousResolver;config.GameExecutablePath=previousPath;Storage.Write("settings.json",config);}
    }
  }
}
