using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  static class SteamGameLauncher {
    public const string AppId="4732690";
    static void Registration(List<string> candidates,RegistryKey root,string name) {
      try {using(var key=root.OpenSubKey(name)) {
        if(key==null)return;
        candidates.Add(key.GetValue("SteamExe") as string);
        foreach(string field in new[]{"SteamPath","InstallPath"}) {
          string folder=key.GetValue(field) as string;
          if(!string.IsNullOrWhiteSpace(folder))candidates.Add(Path.Combine(folder.Trim().Trim('"'),"steam.exe"));
        }
      }}catch { }
    }
    public static string SelectExecutable(IEnumerable<string> candidates) {
      foreach(string candidate in candidates) {
        if(string.IsNullOrWhiteSpace(candidate))continue;
        try {
          string path=Path.GetFullPath(candidate.Trim().Trim('"'));
          if(string.Equals(Path.GetFileName(path),"steam.exe",StringComparison.OrdinalIgnoreCase) && File.Exists(path))return path;
        }catch { }
      }
      return null;
    }
    public static string FindExecutable() {
      var candidates=new List<string>();
      Registration(candidates,Registry.CurrentUser,@"Software\Valve\Steam");
      Registration(candidates,Registry.LocalMachine,@"Software\Valve\Steam");
      Registration(candidates,Registry.LocalMachine,@"Software\WOW6432Node\Valve\Steam");
      foreach(var folder in new[]{Environment.SpecialFolder.ProgramFilesX86,Environment.SpecialFolder.ProgramFiles}) {
        string path=Environment.GetFolderPath(folder);
        if(!string.IsNullOrEmpty(path))candidates.Add(Path.Combine(path,"Steam","steam.exe"));
      }
      return SelectExecutable(candidates);
    }
    public static ProcessStartInfo Command(string executable) {
      if(string.IsNullOrWhiteSpace(executable))throw new FileNotFoundException("Steam is not installed.");
      string path=Path.GetFullPath(executable);
      return new ProcessStartInfo {FileName=path,Arguments="-applaunch "+AppId,WorkingDirectory=Path.GetDirectoryName(path),UseShellExecute=false};
    }
    public static void Launch() {
      using(var process=Process.Start(Command(FindExecutable()))) {
        if(process==null)throw new InvalidOperationException("Steam could not be started.");
      }
    }
  }

  partial class Widget {
    Action gameLauncher;
    Func<bool?> gameProbe;
    DateTimeOffset? observedGameStart;
    GameActivity gameActivity=new GameActivity();
    DispatcherTimer gameWatchTimer;
    Forms.ToolStripMenuItem trayGamePlay;
    bool gameLaunching;
    string gameLaunchError="";
    Task gameLaunchTask=Task.FromResult(0);
    static string GameLaunchHint {get{return L.T("Launch Endfield through Steam");}}
    void InitializeGameLauncher() {
      gameLauncher=()=>{
        if(preview || Storage.Isolated)throw new InvalidOperationException("Game launch is disabled in diagnostics.");
        SteamGameLauncher.Launch();
      };
      gameProbe=()=>{if(preview || Storage.Isolated)return (bool?)false;var observation=GameActivity.Sample();observedGameStart=observation.Started;return observation.Running;};
      gameStartProbe=()=>observedGameStart;
      gameFocuser=()=>!preview && !Storage.Isolated && GameWindow.Focus();
      Find<Button>("PlayButton").Click+=(s,e)=>QueueGameLaunch();
      Find<Button>("CompactPlayButton").Click+=(s,e)=>QueueGameLaunch();
      Find<Button>("MicroPlayButton").Click+=(s,e)=>QueueGameLaunch();
      PollGameActivity(DateTimeOffset.UtcNow);UpdateGameLaunchControls();
      if(!preview && !Storage.Isolated) {
        gameWatchTimer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromSeconds(5)};
        gameWatchTimer.Tick+=(s,e)=>PollGameActivity(DateTimeOffset.UtcNow);gameWatchTimer.Start();
      }
    }
    void QueueGameLaunch(){gameLaunchTask=LaunchGame();}
    void BuildGameTrayControl(Forms.ContextMenuStrip menu) {
      trayGamePlay=new Forms.ToolStripMenuItem(L.T("Play"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>QueueGameLaunch())));
      int index=Math.Min(4,menu.Items.Count);menu.Items.Insert(index,trayGamePlay);menu.Items.Insert(index+1,new Forms.ToolStripSeparator());
      menu.Items.Insert(index+1,new Forms.ToolStripMenuItem(L.T("Calendar"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>ShowCalendar()))));
      menu.Opening+=(s,e)=>PollGameActivity(DateTimeOffset.UtcNow);UpdateGameLaunchControls();
    }
    void UpdateGameLaunchControls() {
      string label=gameActivity.Running?L.T("Game is running"):gameLaunching?L.T("Launching…"):gameLaunchError.Length>0?L.T("Try again"):L.T("Play");
      string hint=gameLaunchError.Length>0?gameLaunchError:gameActivity.Running?L.T("Return to game"):GameLaunchHint;
      bool enabled=!gameLaunching;
      Text("PlayLabel",label);
      foreach(string name in new[]{"PlayButton","CompactPlayButton","MicroPlayButton"}) {
        var button=Find<Button>(name);button.IsEnabled=enabled;button.ToolTip=hint;AutomationProperties.SetName(button,label);
      }
      if(trayGamePlay!=null){trayGamePlay.Text=label;trayGamePlay.Enabled=enabled;trayGamePlay.ToolTipText=hint;}
    }
    void PollGameActivity(DateTimeOffset now) {
      if(closing || extrasDisposed || recovery.Suspended)return;
      bool? observed=null;try{observed=gameProbe();}catch{}
      bool wasRunning=gameActivity.Running,exited=gameActivity.Observe(observed);
      ObserveSession(wasRunning,now);
      if(wasRunning!=gameActivity.Running){if(gameActivity.Running)gameLaunchError="";UpdateGameLaunchControls();PaintSession(now);AdjustBackgroundTimer();}
      if(exited && !string.IsNullOrEmpty(accountToken))RequestRecovery(now);
    }
    void DisposeGameLauncher(){if(gameWatchTimer!=null){gameWatchTimer.Stop();gameWatchTimer=null;}}
    async Task LaunchGame() {
      if(gameLaunching)return;
      PollGameActivity(DateTimeOffset.UtcNow);
      if(gameActivity.Running) {
        bool focused=false;try{focused=gameFocuser();}catch{}
        gameLaunchError=focused?"":L.T("The game window is not available yet. Try again.");UpdateGameLaunchControls();return;
      }
      gameLaunching=true;gameLaunchError="";UpdateGameLaunchControls();
      try {
        gameLauncher();
        await Task.Delay(2000);
      }catch(FileNotFoundException) {
        gameLaunchError=L.T("Steam was not found. Install or open Steam and try again.");
      }catch {
        gameLaunchError=L.T("Could not launch the game. Check that Steam opens and try again.");
      }finally {gameLaunching=false;if(!closing)UpdateGameLaunchControls();}
    }
  }
}
