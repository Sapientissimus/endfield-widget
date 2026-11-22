using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
namespace EndfieldWidget {
  public sealed class VersionUpdateNotice {
    public string Name {get;set;}
    public string Identity {get;set;}
    public string Source {get;set;}
    public string Date {get;set;}
    public long Published {get;set;}
    internal DateTime? Day {get{DateTime value;return DateTime.TryParseExact(Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out value)?(DateTime?)value:null;}}
  }
  static class VersionUpdates {
    public static bool Eligible(string title){return Regex.IsMatch(title??"",@"\[[^\[\]]+\].*\b(?:Version\s+DEV\s+Comm|Version\s+Preview|Version\s+Trailer|Version\s+Update|New\s+Version\s+Guide)\b",RegexOptions.IgnoreCase);}
    public static string Identity(string title){var m=Regex.Match(LocalizedArticles.Quotes(title??""),@"\[([^\[\]]+)\]");return m.Success?CalendarData.Normalize(m.Groups[1].Value):null;}
    public static VersionUpdateNotice Parse(string html,string title,string canonicalTitle,string source,long published,string localizedTitle=null) {
      if(!Eligible(title) || !CalendarData.Official(source))return null;
      string text=NewsClient.StructuredText(html);
      var match=Regex.Match(text,@"(?:new\s+version\s*\[(?<name>[^\[\]\r\n]{1,160})\]|\[(?<name>[^\[\]\r\n]{1,160})\]\s*(?:new\s+)?version)\s+(?:will\s+)?(?:officially\s+)?(?:launch|release|be\s+(?:released|available))\s+(?:officially\s+)?on\s+(?<date>[A-Za-z]+\.?\s+\d{1,2},?\s+\d{4})(?!\d)",RegexOptions.IgnoreCase);
      if(!match.Success || CalendarData.Normalize(match.Groups["name"].Value)!=Identity(title) || Regex.IsMatch(text.Substring(match.Index+match.Length,Math.Min(35,text.Length-match.Index-match.Length)),@"^\s*(?:at\s*)?\d{1,2}:\d{2}",RegexOptions.IgnoreCase))return null;
      DateTime day;string raw=match.Groups["date"].Value.Replace(".","");
      if(!DateTime.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.None,out day))return null;
      var publication=DateTimeOffset.FromUnixTimeSeconds(published);
      if(day.Date<publication.UtcDateTime.Date.AddDays(-1) || day.Date>publication.UtcDateTime.Date.AddDays(180))return null;
      var displayNames=Regex.Matches(LocalizedArticles.Quotes(localizedTitle),@"\[([^\[\]\r\n]{1,160})\]").Cast<Match>().Select(m=>m.Groups[1].Value.Trim()).Where(n=>n.Length>0).ToArray();
      return new VersionUpdateNotice{Name=displayNames.LastOrDefault()??match.Groups["name"].Value,Identity=Identity(canonicalTitle)??Identity(title),Source=source,Date=day.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Published=published};
    }
    public static bool Valid(VersionUpdateNotice value,string source){return value==null || value.Source==source && value.Day.HasValue && !string.IsNullOrWhiteSpace(value.Name) && value.Name.Length<=160 && !string.IsNullOrWhiteSpace(value.Identity) && value.Identity.Length<=200;}
  }
}
