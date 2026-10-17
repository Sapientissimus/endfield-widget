using System;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  static class OfficialDates {
    internal const string Token=@"(?<month>[A-Za-z]+)\.?\s+(?<day>\d{1,2})(?:,?\s+(?<year>\d{4}))?\s*(?:at\s*)?(?<hour>\d{1,2}):(?<minute>\d{2})";
    internal static bool HasYear(string text){return Regex.Match(ArticleLanguage.Dates(text),Token,RegexOptions.IgnoreCase).Groups["year"].Success;}
    internal static DateTimeOffset Published(string text,TimeSpan offset,DateTimeOffset published) {
      var local=published.ToOffset(offset);var date=NewsClient.ParseDate(text,offset,local.Year);
      // Resolve an omitted January/February year in a November/December
      // announcement. Explicit years and the announced month/day/time win.
      if(!HasYear(text) && local.Month>=11 && date.Month<=2 && date<local)date=date.AddYears(1);
      return date;
    }
    internal static DateTimeOffset End(string text,TimeSpan offset,DateTimeOffset start) {
      var local=start.ToOffset(offset);var date=NewsClient.ParseDate(text,offset,local.Year);
      if(!HasYear(text) && local.Month>=11 && date.Month<=2 && date<start)date=date.AddYears(1);
      return date;
    }
  }
}
