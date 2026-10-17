using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  partial class BannerClient {
    const string EndDateToken=@"[A-Za-z]+\.?\s+\d{1,2}(?:,?\s+\d{4})?\s*(?:at\s*)?\d{1,2}:\d{2}(?:\s*\(UTC\s*[+-]\d{1,2}(?::\d{2})?\))?";
    public static string AvailabilityEnd(string text,bool asia) {
      string region=asia?@"Asia":@"(?:Americas\s*/\s*Europe|Europe\s*/\s*Americas|Europe)";
      var match=Regex.Match(text,region+@"\s*(?:Server)?\s*:\s*[^–—]+?[–—]\s*(?<end>"+EndDateToken+")",RegexOptions.IgnoreCase);
      if(match.Success)return match.Groups["end"].Value;
      if(Regex.IsMatch(text,@"(?:Asia|Europe)\s*(?:Server)?\s*:",RegexOptions.IgnoreCase))return null;
      match=Regex.Match(text,@"Availability\s*:\s*[^–—]+?[–—]\s*(?<end>"+EndDateToken+")",RegexOptions.IgnoreCase);
      return match.Success?match.Groups["end"].Value:null;
    }
    public static long ResolveEnd(string local,DateTimeOffset start,bool asia,int year) {
      if(string.IsNullOrWhiteSpace(local))return 0;
      try {
        TimeSpan offset=TimeSpan.FromHours(asia?8:-5);
        var utc=Regex.Match(local,@"UTC\s*(?<hours>[+-]\d{1,2})(?::(?<minutes>\d{2}))?",RegexOptions.IgnoreCase);
        if(utc.Success){int hours=int.Parse(utc.Groups["hours"].Value,CultureInfo.InvariantCulture),minutes=utc.Groups["minutes"].Success?int.Parse(utc.Groups["minutes"].Value,CultureInfo.InvariantCulture):0;offset=TimeSpan.FromMinutes(hours*60+(utc.Groups["hours"].Value.StartsWith("-")?-minutes:minutes));}
        var end=OfficialDates.End(local,offset,start).ToUniversalTime();
        return end>start && end-start<TimeSpan.FromDays(150)?end.ToUnixTimeSeconds():0;
      }catch{return 0;}
    }
    public static DateTimeOffset? End(Debut debut,bool asia) {
      if(debut==null || string.IsNullOrEmpty(debut.Source) || !Publications.Official(debut.Source))return null;
      long unix=asia?debut.AsiaEndUnix:debut.EuropeEndUnix;if(unix<=0)return null;
      try{var end=DateTimeOffset.FromUnixTimeSeconds(unix);return end>(asia?debut.AsiaStart:debut.EuropeStart)?(DateTimeOffset?)end:null;}catch{return null;}
    }
  }
}
