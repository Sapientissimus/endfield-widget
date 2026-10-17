using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EndfieldWidget {
  sealed class BulletinException:Exception {
    public readonly string Problem;
    public readonly int Status;
    public BulletinException(string problem,int status):base("Офіційне джерело тимчасово недоступне."){Problem=problem;Status=status;}
  }
  public sealed class BulletinIndex {
    public int Schema {get;set;}
    public long Total {get;set;}
    public DateTimeOffset FullChecked {get;set;}
    public List<Dictionary<string,object>> Items {get;set;}
  }
  partial class BannerClient {
    sealed class CachedRead {public DateTimeOffset At;public Task<object> Value;}
    readonly object cacheGate=new object();
    readonly Dictionary<string,CachedRead> sharedReads=new Dictionary<string,CachedRead>(StringComparer.Ordinal);
    Task<List<Dictionary<string,object>>> archiveRead;
    DateTimeOffset archiveReadAt;
    public bool CacheEnabled=!Storage.Isolated;
    public Func<DateTimeOffset> CacheNow=()=>DateTimeOffset.UtcNow;
    internal void InvalidateReads(){lock(cacheGate){sharedReads.Clear();archiveRead=null;archiveReadAt=DateTimeOffset.MinValue;}}
    Task<object> SharedRead(string url) {
      if(!CacheEnabled)return ReadNetwork(url);
      lock(cacheGate) {
        var now=CacheNow();CachedRead saved;
        if(sharedReads.TryGetValue(url,out saved) && !saved.Value.IsFaulted && !saved.Value.IsCanceled && now>=saved.At && now-saved.At<TimeSpan.FromSeconds(45))return saved.Value;
        if(sharedReads.Count>256)foreach(var key in sharedReads.Where(p=>p.Value.Value.IsCompleted && now-p.Value.At>=TimeSpan.FromSeconds(45)).Select(p=>p.Key).ToList())sharedReads.Remove(key);
        var task=ReadNetwork(url);sharedReads[url]=new CachedRead{At=now,Value=task};return task;
      }
    }
    Task<List<Dictionary<string,object>>> CachedArchive() {
      if(!CacheEnabled)return ReadArchive();
      lock(cacheGate) {
        var now=CacheNow();if(archiveRead!=null && !archiveRead.IsFaulted && !archiveRead.IsCanceled && now>=archiveReadAt && now-archiveReadAt<TimeSpan.FromSeconds(45))return archiveRead;
        archiveReadAt=now;archiveRead=ReadArchive();return archiveRead;
      }
    }
    async Task<List<Dictionary<string,object>>> ReadArchive() {
      var now=CacheNow();var cached=CacheEnabled?Storage.Read<BulletinIndex>(Publications.Cache("bulletin-index")):null;
      bool canReuse=cached!=null && cached.Schema==1 && cached.Items!=null && cached.Total==cached.Items.Count && cached.Total>=0 && now>=cached.FullChecked && now-cached.FullChecked<TimeSpan.FromDays(1)
        && cached.Items.All(a=>a!=null && System.Text.RegularExpressions.Regex.IsMatch(Json.Str(a,"cid"),@"^\d+$")) && cached.Items.Select(a=>Json.Str(a,"cid")).Distinct().Count()==cached.Items.Count;
      var all=new Dictionary<string,Dictionary<string,object>>(StringComparer.Ordinal);int pages=1;long total=0;
      for(int page=1;page<=pages;page++) {
        var value=await Read(Api+"?"+Query+"&page="+page+"&pageSize=20").ConfigureAwait(false);
        if(page==1){total=Json.Num(value,"data","total");if(total<0 || total>int.MaxValue)throw new InvalidOperationException("Не вдалося перевірити повну історію банерів.");pages=Math.Max(1,(int)Math.Ceiling(total/20.0));}
        var entries=Json.At(value,"data","list") as object[];if(entries==null)throw new FormatException("The official archive list is missing or invalid.");
        var listed=entries.OfType<Dictionary<string,object>>().ToList();
        if(listed.Count!=entries.Length)throw new FormatException("The official archive contains an invalid entry.");
        if(listed.Count==0 && total>0)throw new FormatException("An official archive page is missing.");
        foreach(var item in listed){string cid=Json.Str(item,"cid");if(System.Text.RegularExpressions.Regex.IsMatch(cid,@"^\d+$"))all[cid]=item;}
        if(canReuse && total>=cached.Total) {
          var merged=cached.Items.ToDictionary(a=>Json.Str(a,"cid"),a=>a,StringComparer.Ordinal);foreach(var item in all)merged[item.Key]=item.Value;
          if(merged.Count==total && listed.Any(a=>cached.Items.Any(old=>Json.Str(old,"cid")==Json.Str(a,"cid")))) {
            var reused=merged.Values.OrderByDescending(a=>Json.Num(a,"displayTime")).ToList();TrySaveIndex(new BulletinIndex{Schema=1,Total=total,FullChecked=cached.FullChecked,Items=reused});return reused;
          }
        }
      }
      if(all.Count!=total)throw new FormatException("The official archive changed while it was being read.");
      var result=all.Values.OrderByDescending(a=>Json.Num(a,"displayTime")).ToList();if(CacheEnabled)TrySaveIndex(new BulletinIndex{Schema=1,Total=total,FullChecked=now,Items=result});return result;
    }
    static void TrySaveIndex(BulletinIndex value){try{Storage.Write(Publications.Cache("bulletin-index"),value);}catch{}}
  }
}
