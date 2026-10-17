using System;
using System.Threading;

namespace EndfieldWidget {
  sealed class InstanceActivation:IDisposable {
    public const string Name="Local\\EndfieldWidget.Show";
    readonly EventWaitHandle signal;RegisteredWaitHandle waiting;volatile bool disposed;
    public InstanceActivation(string name){signal=new EventWaitHandle(false,EventResetMode.AutoReset,name);}
    public void Bind(Action show) {
      waiting=ThreadPool.RegisterWaitForSingleObject(signal,(s,t)=>{if(!disposed)try{show();}catch(InvalidOperationException){}},null,Timeout.Infinite,false);
    }
    public static bool Request(string name,int attempts=20) {
      for(int i=0;i<attempts;i++) {
        try{using(var target=EventWaitHandle.OpenExisting(name))return target.Set();}
        catch(WaitHandleCannotBeOpenedException){if(i+1<attempts)Thread.Sleep(50);}catch(UnauthorizedAccessException){return false;}
      }
      return false;
    }
    public void Dispose(){disposed=true;if(waiting!=null)waiting.Unregister(null);signal.Dispose();}
  }
  partial class Widget {
    public void RequestShowFromLauncher(){PostBackground(()=>{if(widgetMenu!=null)widgetMenu.IsOpen=false;ShowWidget();});}
  }
}
