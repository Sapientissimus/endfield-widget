using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  // Syntactically valid JSON can still be an interrupted or unrelated document.
  // Public caches are disposable; a personal account is recovered separately.
  static class CachedData {
    static bool Source(string url){return Publications.Official(url) && url.StartsWith(Publications.NewsUrl+"/",StringComparison.Ordinal);}
    public static Sanity Sanity(string role) {
      var value=Storage.Read<Sanity>("sanity.json");
      return value!=null && !string.IsNullOrEmpty(role) && value.RoleId==role && value.Current>=0 && value.Current<=100000 && value.Maximum>0 && value.Maximum<=100000 && value.Checked>DateTimeOffset.FromUnixTimeSeconds(0)?value:null;
    }
    public static Schedule Schedule() {
      var value=Storage.Read<Schedule>(Publications.Cache("schedule"));
      return value!=null && !string.IsNullOrWhiteSpace(value.Version) && value.Version.Length<=300 && Source(value.Source) && value.Start!=DateTimeOffset.MinValue && value.End>value.Start && value.End-value.Start<=TimeSpan.FromDays(3)?value:EndfieldWidget.Schedule.Seed();
    }
    public static BannerState Banners() {
      var value=Storage.Read<BannerState>(Publications.Cache("banners"));if(value==null)return BannerState.Seed();
      value.Debuts=(value.Debuts??new List<Debut>()).Where(d=>d!=null && !string.IsNullOrWhiteSpace(d.Name) && d.Name.Length<=200 && Source(d.Source) && d.EuropeStart!=DateTimeOffset.MinValue && d.AsiaStart!=DateTimeOffset.MinValue).Take(4096).ToList();
      value.Notices=(value.Notices??new List<BannerNotice>()).Where(n=>n!=null && !string.IsNullOrWhiteSpace(n.Name) && n.Name.Length<=200 && Regex.IsMatch(n.Cid??"",@"^\d+$") && (!string.IsNullOrWhiteSpace(n.ReleaseVersion) || !string.IsNullOrWhiteSpace(n.LocalStart))).Take(4096).ToList();
      value.GameEvents=(value.GameEvents??new List<GameEvent>()).Where(e=>e!=null && !string.IsNullOrWhiteSpace(e.Name) && e.Name.Length<=160 && Source(e.Source)).Take(4096).ToList();return value;
    }
    public static List<BannerArt> Portraits() {
      var value=Storage.Read<List<BannerArt>>(Publications.Cache("portraits"));
      return value==null?null:value.Where(a=>a!=null && !string.IsNullOrWhiteSpace(a.Name) && a.Name.Length<=200 && Regex.IsMatch(a.Key??"",@"^[A-Za-z0-9][A-Za-z0-9_-]{0,100}$") && BannerClient.IsOfficialImage(a.Portrait)).Take(4096).ToList();
    }
  }
}
