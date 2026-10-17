using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EndfieldWidget {
  // Compiled with the production sources and a separate entry point. It never
  // enters Program.Main, takes the production mutex or opens a personal profile.
  static class AuditHarness {
    internal static readonly List<string> Lines=new List<string>();
    internal static int Failures;
    internal static void Check(bool passed,string id,string label){Lines.Add((passed?"PASS: ":"FAIL: ")+id+" "+label);if(!passed)Failures++;}
    internal static void Note(string text){Lines.Add("METRIC: "+text);}
    [STAThread] static int Main(string[] args) {
      if(args.Length!=3)return 2;
      string report=Path.GetFullPath(args[0]),kind=args[1],fixtures=Path.GetFullPath(args[2]);
      Directory.CreateDirectory(Path.GetDirectoryName(report));Storage.DirectoryPath=Path.Combine(Path.GetDirectoryName(report),"isolated-"+kind+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Storage.DirectoryPath);Storage.Isolated=true;
      var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
      if(kind.StartsWith("startup-",StringComparison.Ordinal) || kind=="settings-backup") {
        try{Startup(kind);}catch(Exception error){Check(false,kind,"Application rejected the isolated scenario with "+error.GetType().Name);}
        finally{File.WriteAllLines(report,Lines,new UTF8Encoding(false));app.Shutdown();}
        return Failures==0?0:1;
      }
      Widget widget=null;
      try{widget=new Widget(true);widget.AuditStart(report,kind,fixtures);app.Run();}
      catch(Exception error){Check(false,kind,error.GetType().Name);File.WriteAllLines(report,Lines,new UTF8Encoding(false));}
      return Failures==0?0:1;
    }
    static void Startup(string kind) {
      var now=DateTimeOffset.UtcNow;var config=new Config{ProtectedToken=Storage.Protect("OFFLINE-AUDIT-ACCOUNT"),RoleId="audit-role",Scale=1,CompactScale=1};Storage.Write("settings.json",config);
      if(kind=="settings-backup") {
        File.WriteAllText(Path.Combine(Storage.DirectoryPath,"settings.json"),"{\"unrelated_document\":true}");
        var restored=Storage.Read<Config>("settings.json");Check(restored!=null && restored.ProtectedToken==config.ProtectedToken && Storage.SettingsRecovered,"A01","Structurally unrelated settings recover the valid backup instead of losing the account");return;
      }
      var sanity=new Sanity{RoleId="audit-role",Current=97,Maximum=360,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddHours(3).ToUnixTimeSeconds(),Nickname="Offline audit"};
      if(kind=="startup-role")sanity.RoleId="different-role";
      if(kind=="startup-zero"){sanity.Maximum=0;sanity.Current=0;}
      if(kind=="startup-time")sanity.FullAt=long.MaxValue;
      Storage.Write("sanity.json",sanity);
      if(kind=="startup-banner")Storage.Write(Publications.Cache("banners"),new BannerState{Debuts=new List<Debut>{null},Notices=new List<BannerNotice>{null}});
      if(kind=="startup-schedule")Storage.Write(Publications.Cache("schedule"),new Schedule{Version=null,Start=now.AddDays(1),End=now.AddDays(1).AddHours(4)});
      var widget=new Widget(false);
      try{widget.AuditStartupResult(kind);}finally{widget.AuditDispose();}
    }
    internal static byte[] Png(int width,int height) {
      var pixels=new byte[width*height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=80;pixels[i+1]=180;pixels[i+2]=90;pixels[i+3]=255;}
      var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}
    }
    internal static async Task Ensure(BannerClient client,Debut debut){var task=(Task)typeof(BannerClient).GetMethod("EnsurePortrait",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(client,new object[]{debut});await task;}
  }
  partial class Widget {
    internal void AuditDispose(){closing=true;DisposeExtras();if(timer!=null)timer.Stop();if(desktopSurface!=null)desktopSurface.Dispose();if(tray!=null)tray.Dispose();DisposeTrayStatus();http.Dispose();window.Close();}
    internal void AuditStartupResult(string kind) {
      if(kind=="startup-role")AuditHarness.Check(sanity==null && Find<TextBlock>("SanityNumber").Text=="—","A02","A cached value from another role is not shown as the selected character's reading");
      if(kind=="startup-zero")AuditHarness.Check(sanity==null && Find<TextBlock>("SanityNumber").Text=="—","A03","An invalid cached resource maximum cannot break initial rendering");
      if(kind=="startup-time")AuditHarness.Check(sanity!=null && !sanity.LocalFullAt().HasValue,"A04","An out-of-range recovery timestamp leaves the numeric reading usable without crashing");
      if(kind=="startup-banner")AuditHarness.Check(bannerState.Debuts!=null && bannerState.Debuts.All(d=>d!=null),"A05","Null entries in a public debut cache do not prevent startup");
      if(kind=="startup-schedule")AuditHarness.Check(schedule.Bundled,"A06","An incomplete maintenance cache falls back to a usable seed");
      AuditHarness.Check(startup is MemoryStartupRegistration,"ISOLATION","Startup scenarios use a memory-only startup backend");
    }
    internal void AuditStart(string report,string kind,string fixtures) {
      window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=window.Top=-10000;window.Show();
      window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        try {
          AuditHarness.Check(Storage.Isolated && startup is MemoryStartupRegistration,"ISOLATION","No personal profile, startup registration, clipboard, game launch or actual notifications");
          if(kind.StartsWith("next-",StringComparison.Ordinal))await AuditContinuation(kind,fixtures);else if(kind=="ui")await AuditUi();else if(kind=="network")await AuditNetwork(fixtures);else if(kind=="images")AuditImages();else if(kind=="invariants")AuditInvariants();else throw new ArgumentException("Unknown audit group");
        }catch(Exception error){AuditHarness.Check(false,kind,"Unexpected "+error.GetType().Name+": "+error.Message);}
        finally{if(settingsDialog!=null)settingsDialog.Close();File.WriteAllLines(report,AuditHarness.Lines,new UTF8Encoding(false));Exit();}
      }));
    }
    async Task AuditUi() {
      DemoReading();Settings();settingsDialog.Left=settingsDialog.Top=-10000;
      ((TextBox)settingsDialog.FindName("ShortcutInput0")).Text="Ctrl+Shift+F8";
      ((TextBox)settingsDialog.FindName("ShortcutInput1")).Text=L.T("Disabled");
      ((ComboBox)settingsDialog.FindName("BlockModeSelector")).SelectedIndex=2;
      ((ComboBox)settingsDialog.FindName("CropModeSelector")).SelectedIndex=1;
      ((ComboBox)settingsDialog.FindName("AnchorSelector")).SelectedIndex=3;
      ((TextBox)settingsDialog.FindName("MarginXInput")).Text="37";
      string saved=config.HotkeyToggle;ChangeInterfaceLanguage("en",settingsDialog);await window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
      AuditHarness.Check(((TextBox)settingsDialog.FindName("ShortcutInput0")).Text=="Ctrl+Shift+F8","A07","Language switching keeps an unsaved shortcut");
      AuditHarness.Check(((TextBox)settingsDialog.FindName("ShortcutInput1")).Text==L.T("Disabled"),"A07","A pending disabled shortcut is translated without re-enabling it");
      AuditHarness.Check(((ComboBox)settingsDialog.FindName("BlockModeSelector")).SelectedIndex==2 && ((ComboBox)settingsDialog.FindName("CropModeSelector")).SelectedIndex==1,"A07","The edited block and crop target modes survive a language change");
      AuditHarness.Check(((ComboBox)settingsDialog.FindName("AnchorSelector")).SelectedIndex==3 && ((TextBox)settingsDialog.FindName("MarginXInput")).Text=="37","A07","Pending placement choices survive together with their numeric draft");
      AuditHarness.Check(config.HotkeyToggle==saved,"A07","Changing language does not apply an unconfirmed shortcut draft");
      ((Button)settingsDialog.FindName("ApplyShortcutsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      AuditHarness.Check(config.HotkeyToggle=="Ctrl+Shift+F8" && config.HotkeyMenu=="","A07","Saving after switching language applies the retained shortcut draft");
      settingsDialog.Close();L.Set("uk");
    }
    async Task AuditNetwork(string fixtures) {
      var png=AuditHarness.Png(3,4);var debut=new Debut{Key="audit-repair",Name="Audit",Portrait="https://web-static.hg-cdn.com/audit/repair.png"};var path=BannerClient.ImagePath(debut);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,new byte[]{137,80,78});
      using(var handler=new AuditHttp(_=>new ByteArrayContent(png)))using(var client=new HttpClient(handler)) {
        await AuditHarness.Ensure(new BannerClient(client),debut);
        AuditHarness.Check(handler.Requests==1 && File.ReadAllBytes(path).SequenceEqual(png),"A08","An incomplete cached PNG is downloaded again and repaired");
        await AuditHarness.Ensure(new BannerClient(client),debut);AuditHarness.Check(handler.Requests==1,"A08","A valid cached PNG is reused without a second download");
      }
      var oversized=new AuditGeneratedContent(8*1024*1024,false);debut=new Debut{Key="audit-large-body",Portrait="https://web-static.hg-cdn.com/audit/large.png"};bool rejected=false;
      using(var handler=new AuditHttp(_=>oversized))using(var client=new HttpClient(handler))try{await AuditHarness.Ensure(new BannerClient(client),debut);}catch(FormatException){rejected=true;}
      AuditHarness.Check(rejected && !File.Exists(BannerClient.ImagePath(debut)),"A09","An oversized response cannot be committed to the image cache");
      AuditHarness.Check(oversized.Stream.BytesRead<=6000001,"A09","A chunked response without Content-Length is stopped at the limit");AuditHarness.Note("chunked-image bytes read="+oversized.Stream.BytesRead);
      var stalled=new AuditGeneratedContent(128,true);debut=new Debut{Key="audit-timeout",Portrait="https://web-static.hg-cdn.com/audit/stall.png"};
      using(var handler=new AuditHttp(_=>stalled))using(var client=new HttpClient(handler){Timeout=TimeSpan.FromMilliseconds(70)}) {
        var task=AuditHarness.Ensure(new BannerClient(client),debut);await Task.Delay(350);bool completed=task.IsCompleted;stalled.Stream.Release();try{await task;}catch{}AuditHarness.Check(completed,"A10","The portrait body has a deadline after HTTP headers arrive");
      }
      stalled=new AuditGeneratedContent(128,true);debut=new Debut{Key="audit-cancel",Portrait="https://web-static.hg-cdn.com/audit/cancel.png"};
      using(var handler=new AuditHttp(_=>stalled))using(var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)}) {
        var downloader=new BannerClient(client);var task=AuditHarness.Ensure(downloader,debut);await Task.Delay(50);
        var cancel=typeof(BannerClient).GetMethod("CancelDownloads",BindingFlags.Instance|BindingFlags.NonPublic);if(cancel!=null)cancel.Invoke(downloader,null);
        await Task.Delay(100);bool completed=task.IsCompleted;stalled.Stream.Release();try{await task;}catch{}
        AuditHarness.Check(completed && !File.Exists(BannerClient.ImagePath(debut)),"A10","Recovery and disposal can cancel an active portrait body without committing it");
      }
      var version=File.ReadAllText(Path.Combine(fixtures,"localization","en-us-9560.json"));
      using(var handler=new AuditHttp(request=>request.RequestUri.AbsolutePath=="/api/bulletin"?new StringContent("{\"code\":0,\"data\":{\"total\":1,\"list\":[{\"cid\":\"9560\",\"title\":\"[Sanctuary of Ink] Version Update Notes\",\"displayTime\":1792026000}]}}",Encoding.UTF8,"application/json"):new StringContent(version,Encoding.UTF8,"application/json")))
      using(var client=new HttpClient(handler)) {
        var changes=new List<Action>();var visited=new HashSet<object>();Action<object> replace=null;
        replace=target=>{if(target==null || !visited.Add(target))return;foreach(var field in target.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)){if(field.FieldType==typeof(HttpClient)){var previous=field.GetValue(target);field.SetValue(target,client);changes.Add(()=>field.SetValue(target,previous));}else if(field.FieldType==typeof(BannerClient))replace(field.GetValue(target));}};
        replace(news);replace(banners);bool oldCache=banners.CacheEnabled;banners.CacheEnabled=true;
        try {
          await news.Fetch();await banners.FetchSchedule();AuditHarness.Check(handler.Archives==1 && handler.Articles==1,"A11","News and banner clients share the same archive and bulletin requests");AuditHarness.Note("shared update archive requests="+handler.Archives+", article requests="+handler.Articles);
          Storage.Write(Publications.Cache("portraits"),new List<BannerArt>{null,new BannerArt{Key="broken"}});bool usable=false;
          try{var state=await banners.Fetch(BannerState.Seed(),Schedule.Seed(),false);usable=BannerClient.Select(state,DateTimeOffset.UtcNow,false)!=null;}catch{}
          AuditHarness.Check(usable,"A13","Invalid entries in the portrait catalog do not permanently block official debut updates");
        }
        finally{banners.CacheEnabled=oldCache;foreach(var restore in changes)restore();}
      }
    }
    void AuditImages() {
      var debut=new Debut{Key="audit-hd",Name="Audit",Banner="Offline fixture",Portrait="https://web-static.hg-cdn.com/audit/hd.png"};var path=BannerClient.ImagePath(debut);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,AuditHarness.Png(3000,4000));
      GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long before=Process.GetCurrentProcess().PrivateMemorySize64;
      ApplyDebut(debut);var image=Find<Image>("OperatorImage").Source as BitmapSource;
      AuditHarness.Check(image!=null && image.PixelWidth<=1280 && image.PixelHeight<=1800,"A12","A future high-resolution portrait is decoded near display size");
      if(image!=null)AuditHarness.Note("decoded portrait="+image.PixelWidth+"x"+image.PixelHeight+", pixel buffer bytes="+(image.PixelWidth*(long)image.PixelHeight*4));
      GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();AuditHarness.Note("portrait private memory delta="+(Process.GetCurrentProcess().PrivateMemorySize64-before));
      bool unlocked=false;try{using(var file=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))unlocked=true;}catch(IOException){}
      AuditHarness.Check(unlocked,"A12","The decoded portrait does not keep its cache file locked");
      bool bounded=true;
      foreach(var size in new[]{new[]{1,16384},new[]{16384,1}}) {
        var narrow=new Debut{Key="audit-extreme-"+size[0],Name="Audit",Banner="Offline fixture",Portrait="https://web-static.hg-cdn.com/audit/extreme-"+size[0]+".png"};File.WriteAllBytes(BannerClient.ImagePath(narrow),AuditHarness.Png(size[0],size[1]));ApplyDebut(narrow);
        var decoded=Find<Image>("OperatorImage").Source as BitmapSource;bounded&=decoded!=null && decoded.PixelWidth<=1280 && decoded.PixelHeight<=2048;
      }
      AuditHarness.Check(bounded,"A12","Extreme aspect ratios respect both decoded dimension limits");
    }
    void AuditInvariants() {
      string previous=Environment.CurrentDirectory;try{Environment.CurrentDirectory=Environment.SystemDirectory;using(var image=BannerClient.OpenPortrait(BannerState.Seed().Debuts.First()))AuditHarness.Check(image.Length>0 && Path.IsPathRooted(Storage.DirectoryPath),"V01","Startup from System32 resolves resources and the profile using absolute locations");}finally{Environment.CurrentDirectory=previous;}
      DateTimeOffset a=new DateTimeOffset(2026,10,25,0,59,0,TimeSpan.Zero),b=a.AddMinutes(2);var old=Clock.ZoneOverride;
      try{foreach(string zone in new[]{"FLE Standard Time","Eastern Standard Time","Tokyo Standard Time"}){Clock.ZoneOverride=()=>TimeZoneInfo.FindSystemTimeZoneById(zone);var start=Clock.Local(a);var end=Clock.Local(b);AuditHarness.Check(start.UtcDateTime==a.UtcDateTime && end.UtcDateTime==b.UtcDateTime && (end-start).TotalMinutes==2 && Clock.After(end-start)=="2 minutes","V02","A DST/local-zone conversion does not shift a relative countdown: "+zone);}}finally{Clock.ZoneOverride=old;}
      var spec=SkportClient.NormalizeToken("ACCOUNT_TOKEN=a%2Fb%2Bc; other=value");AuditHarness.Check(spec=="a/b+c","V03","Percent-encoded SKPORT credentials are decoded exactly once without treating '+' as a space");
      AuditHarness.Check(widgetMenu.Items.OfType<MenuItem>().All(i=>string.IsNullOrEmpty(i.InputGestureText)),"V04","The context menu has no nonfunctional keyboard hints");
      AuditHarness.Check(window.Resources.MergedDictionaries.Count(d=>ReferenceEquals(d,L.Resources))==1,"V05","The shared locale dictionary is attached once");
      bool survived=false;try{var config=new Config{RoleId="fixture"};var reading=new Sanity{RoleId="fixture",Current=97,Maximum=360,Checked=DateTimeOffset.UtcNow};var policy=new NotificationPolicy(new NotificationHistory{Sent=new List<NotificationRecord>{null,new NotificationRecord{Key=null}}});survived=policy.Next(config,reading,null,DateTimeOffset.UtcNow,true,false,true)==null;}catch{}
      AuditHarness.Check(survived,"A14","Malformed history entries cannot crash a background tick when notifications are disabled");
    }
  }
  sealed class AuditHttp:HttpMessageHandler {
    readonly Func<HttpRequestMessage,HttpContent> content;
    public int Requests,Archives,Articles;
    public AuditHttp(Func<HttpRequestMessage,HttpContent> value){content=value;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Requests++;if(request.RequestUri.AbsolutePath=="/api/bulletin")Archives++;else if(request.RequestUri.AbsolutePath.StartsWith("/api/bulletin/",StringComparison.Ordinal))Articles++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content(request)});}
  }
  sealed class AuditGeneratedContent:HttpContent {
    public readonly AuditGeneratedStream Stream;
    public AuditGeneratedContent(long length,bool stall){Stream=new AuditGeneratedStream(length,stall);}
    protected override bool TryComputeLength(out long length){length=0;return false;}
    protected override Task<Stream> CreateContentReadStreamAsync(){return Task.FromResult<Stream>(Stream);}
    protected override async Task SerializeToStreamAsync(Stream target,TransportContext context){await Stream.CopyToAsync(target);}
    protected override void Dispose(bool disposing){if(disposing)Stream.Dispose();base.Dispose(disposing);}
  }
  sealed class AuditGeneratedStream:Stream {
    readonly long length;readonly bool stall;readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>();
    public long BytesRead;
    public AuditGeneratedStream(long count,bool waiting){length=count;stall=waiting;}
    public void Release(){release.TrySetCanceled();}
    public override int Read(byte[] buffer,int offset,int count){int n=(int)Math.Min(count,length-BytesRead);Array.Clear(buffer,offset,n);if(BytesRead==0 && n>=4){buffer[offset]=137;buffer[offset+1]=80;buffer[offset+2]=78;buffer[offset+3]=71;}BytesRead+=n;return n;}
    public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){if(stall){using(token.Register(()=>release.TrySetCanceled()))await release.Task;}token.ThrowIfCancellationRequested();return Read(buffer,offset,count);}
    protected override void Dispose(bool disposing){if(disposing)release.TrySetCanceled();base.Dispose(disposing);}
    public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}public override long Length{get{return length;}}public override long Position{get{return BytesRead;}set{throw new NotSupportedException();}}
    public override void Flush(){}public override long Seek(long offset,SeekOrigin origin){throw new NotSupportedException();}public override void SetLength(long value){throw new NotSupportedException();}public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
  }
}
