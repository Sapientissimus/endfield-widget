using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckGameLaunch(Action<bool,string> check,string folder) {
      string fakeSteam=Path.Combine(folder,"fake Steam","steam.exe");
      Directory.CreateDirectory(Path.GetDirectoryName(fakeSteam));File.WriteAllText(fakeSteam,"isolated launch target; never executed");
      check(SteamGameLauncher.SelectExecutable(new[]{null,"",Path.Combine(folder,"missing","steam.exe"),"\""+fakeSteam+"\""})==Path.GetFullPath(fakeSteam),"Steam discovery skips missing paths and accepts an installed executable in a path with spaces");
      check(SteamGameLauncher.SelectExecutable(new[]{Path.Combine(folder,"missing","steam.exe"),Path.Combine(folder,"not-steam.exe")})==null,"Missing Steam is detected without writing any registry key");
      var command=SteamGameLauncher.Command(fakeSteam);
      check(command.FileName==Path.GetFullPath(fakeSteam) && command.Arguments=="-applaunch 4732690" && command.WorkingDirectory==Path.GetDirectoryName(fakeSteam) && !command.UseShellExecute,"Steam launch uses the Endfield app ID and separate executable/arguments without a shell or game-directory dependency");
      var original=gameLauncher;var originalProbe=gameProbe;var originalActivity=gameActivity;var originalTray=trayGamePlay;var originalFocuser=gameFocuser;
      var button=Find<Button>("PlayButton");int calls=0,focuses=0;var trayMenu=new Forms.ContextMenuStrip();
      try {
        gameProbe=()=>false;gameActivity=new GameActivity();BuildGameTrayControl(trayMenu);
        gameFocuser=()=>{focuses++;return true;};
        gameLauncher=()=>{calls++;};
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));var pending=gameLaunchTask;
        check(calls==1 && gameLaunching && !button.IsEnabled && Find<TextBlock>("PlayLabel").Text=="Launching…","The real play-button event dispatches once and shows launch feedback");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(calls==1,"Rapid repeated play-button events do not start duplicate launch requests");
        await pending;
        check(button.IsEnabled && !gameLaunching && Find<TextBlock>("PlayLabel").Text=="Play","The play button becomes available again after sending the launch request");
        Find<Button>("CompactPlayButton").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await gameLaunchTask;
        check(calls==2,"The compact play button uses the same Steam launch action as the full view");
        trayGamePlay.PerformClick();await gameLaunchTask;
        check(calls==3,"The tray play action dispatches the same launch without requiring the full widget");
        gameProbe=()=>true;PollGameActivity(DateTimeOffset.UtcNow);await LaunchGame();
        check(calls==3 && focuses==1 && button.IsEnabled && Find<Button>("CompactPlayButton").IsEnabled && trayGamePlay.Enabled && trayGamePlay.Text=="Game is running","A running game returns to its existing window without dispatching a duplicate Steam launch");
        gameProbe=()=>false;gameActivity=new GameActivity();UpdateGameLaunchControls();
        gameLauncher=()=>{throw new FileNotFoundException("isolated missing-Steam failure");};await LaunchGame();
        check(button.IsEnabled && Find<TextBlock>("PlayLabel").Text=="Try again" && button.ToolTip.ToString().Contains("Steam was not found"),"Missing Steam leaves an actionable retry button and clear explanation");
        gameLauncher=()=>{throw new InvalidOperationException("isolated process-start failure");};await LaunchGame();
        check(button.IsEnabled && !gameLaunching && button.ToolTip.ToString().Contains("Could not launch the game"),"A failed launch is handled without crashing the widget or leaving its button disabled");
        gameLauncher=()=>{calls++;};await LaunchGame();
        check(calls==4 && Find<TextBlock>("PlayLabel").Text=="Play" && button.ToolTip.ToString()==GameLaunchHint,"Retry clears prior errors and dispatches the launch normally");
      }finally {gameLauncher=original;gameProbe=originalProbe;gameFocuser=originalFocuser;gameActivity=originalActivity;trayGamePlay=originalTray;trayMenu.Dispose();UpdateGameLaunchControls();}
    }
  }
}
