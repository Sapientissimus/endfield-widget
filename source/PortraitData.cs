using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace EndfieldWidget {
  static class PortraitData {
    internal const int MaximumBytes=6000000;
    static readonly object fileGate=new object();
    static readonly byte[] Signature={137,80,78,71,13,10,26,10};
    static uint Number(byte[] bytes,int offset){return (uint)bytes[offset]<<24|(uint)bytes[offset+1]<<16|(uint)bytes[offset+2]<<8|bytes[offset+3];}
    public static BitmapImage Load(Stream stream){return Decode(stream,1280,2048);}
    static BitmapImage Decode(Stream stream,int maximumWidth,int maximumHeight) {
      if(!stream.CanSeek || stream.Length<33 || stream.Length>MaximumBytes)throw new FormatException("Invalid portrait length.");
      stream.Position=0;var header=new byte[24];int have=0;
      while(have<header.Length){int count=stream.Read(header,have,header.Length-have);if(count==0)throw new FormatException("Incomplete PNG header.");have+=count;}
      if(!Signature.SequenceEqual(header.Take(8)) || Number(header,8)!=13 || header[12]!=73 || header[13]!=72 || header[14]!=68 || header[15]!=82)throw new FormatException("Expected a PNG portrait.");
      uint width=Number(header,16),height=Number(header,20);
      if(width==0 || height==0 || width>16384 || height>16384 || width*(ulong)height>64000000)throw new FormatException("Invalid portrait dimensions.");
      double scale=Math.Min(1,Math.Min(maximumWidth/(double)width,maximumHeight/(double)height));stream.Position=0;
      var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
      if(scale<1) {
        if(width/(double)maximumWidth>=height/(double)maximumHeight)image.DecodePixelWidth=Math.Max(1,(int)Math.Floor(width*scale));
        else image.DecodePixelHeight=Math.Max(1,(int)Math.Floor(height*scale));
      }
      image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    internal static bool ValidFile(string path) {
      try{using(var stream=File.OpenRead(path)){Decode(stream,24,32);return true;}}
      catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}catch(FormatException){return false;}catch(NotSupportedException){return false;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
    }
    internal static void Save(string path,byte[] bytes) {
      // Decode before replacing a previous cache, then commit the complete file.
      using(var stream=new MemoryStream(bytes,false))Decode(stream,24,32);
      lock(fileGate) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+".tmp."+Guid.NewGuid().ToString("N");
        try{File.WriteAllBytes(temporary,bytes);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}catch(IOException){}}
      }
    }
    internal static async Task<byte[]> Read(HttpContent content,CancellationToken token) {
      if(content.Headers.ContentLength>MaximumBytes)throw new FormatException("Portrait exceeds the encoded size limit.");
      using(var input=await content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()) {
        var buffer=new byte[32768];
        for(;;) {
          token.ThrowIfCancellationRequested();int count=await input.ReadAsync(buffer,0,Math.Min(buffer.Length,MaximumBytes-(int)output.Length+1),token).ConfigureAwait(false);
          if(count==0)break;
          if(output.Length+count>MaximumBytes)throw new FormatException("Portrait exceeds the encoded size limit.");
          output.Write(buffer,0,count);
        }
        token.ThrowIfCancellationRequested();return output.ToArray();
      }
    }
  }
  partial class BannerClient {
    readonly object downloadGate=new object();
    readonly HashSet<CancellationTokenSource> downloads=new HashSet<CancellationTokenSource>();
    internal void CancelDownloads() {
      CancellationTokenSource[] pending;lock(downloadGate)pending=downloads.ToArray();
      foreach(var source in pending)try{source.Cancel();}catch(ObjectDisposedException){}
    }
    async Task DownloadPortrait(Debut debut,string path) {
      using(var timeout=new CancellationTokenSource()) {
        lock(downloadGate)downloads.Add(timeout);
        try {
          var budget=http.Timeout;if(budget==System.Threading.Timeout.InfiniteTimeSpan || budget>TimeSpan.FromSeconds(20))budget=TimeSpan.FromSeconds(20);timeout.CancelAfter(budget);
          using(var response=await http.GetAsync(debut.Portrait,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false))
          using(timeout.Token.Register(()=>{try{response.Dispose();}catch{}})) {
            response.EnsureSuccessStatusCode();byte[] bytes=await PortraitData.Read(response.Content,timeout.Token).ConfigureAwait(false);timeout.Token.ThrowIfCancellationRequested();PortraitData.Save(path,bytes);
          }
        }catch{if(timeout.IsCancellationRequested)throw new OperationCanceledException(timeout.Token);throw;}
        finally{lock(downloadGate)downloads.Remove(timeout);}
      }
    }
  }
}
