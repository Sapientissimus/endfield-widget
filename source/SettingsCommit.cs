using System;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    DispatcherTimer settingsCommitTimer;
    bool settingsCommitPending,settingsBackupAvailable;
    int settingsCommitCount;
    void QueueSettingsCommit() {
      settingsCommitPending=true;
      if(settingsCommitTimer==null) {
        settingsCommitTimer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromMilliseconds(350)};
        settingsCommitTimer.Tick+=(s,e)=>FlushSettingsCommit();
      }
      settingsCommitTimer.Stop();settingsCommitTimer.Start();
    }
    void FlushSettingsCommit() {
      if(settingsCommitTimer!=null)settingsCommitTimer.Stop();
      if(!settingsCommitPending)return;
      settingsCommitPending=false;CommitSettings();
    }
    void CommitSettings() {
      if(settingsCommitTimer!=null)settingsCommitTimer.Stop();settingsCommitPending=false;
      try{Storage.Write("settings.json",config);settingsCommitCount++;}catch{}
      if(syncUndoButton!=null){settingsBackupAvailable=Storage.ReadBackup()!=null;syncUndoButton();}
    }
  }
}
