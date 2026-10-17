using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  // Russian is the requested publication language. Normalize only the structural
  // grammar consumed by the date parsers; names and descriptive prose stay Russian.
  static class ArticleLanguage {
    public static string Locale {get{return Publications.Locale;}}
    const RegexOptions Ignore=RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;
    static readonly Dictionary<string,int> Months=MonthNames();
    static readonly string MonthPattern=string.Join("|",Months.Keys.OrderByDescending(s=>s.Length).Select(Regex.Escape));
    static readonly string DayMonth=@"(?<day>\d{1,2})\s+(?<month>"+MonthPattern+@")\s*(?:(?<year>\d{4})\s*(?:г(?:ода|\.)?)?)?";
    static Dictionary<string,int> MonthNames() {
      var result=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);var format=CultureInfo.GetCultureInfo("ru-RU").DateTimeFormat;
      foreach(var names in new[]{format.MonthNames,format.MonthGenitiveNames,format.AbbreviatedMonthNames,format.AbbreviatedMonthGenitiveNames})for(int i=0;i<12;i++)if(!string.IsNullOrEmpty(names[i]))result[names[i]]=i+1;
      return result;
    }
    static string Quotes(string text){return (text??"").Replace('«','[').Replace('»',']').Replace('「','[').Replace('」',']').Replace('“','[').Replace('”',']');}
    public static string Title(string text) {
      string value=Quotes(text);var name=Regex.Match(value,@"\[([^\[\]\r\n]+)\]");if(!name.Success)return value;
      string bracket="["+name.Groups[1].Value+"]";
      if(Regex.IsMatch(value,@"Описание\s+(?:обновления|новой\s+версии)|Примечания\s+к\s+обновлению",Ignore))return bracket+" Version Update Notes";
      if(Regex.IsMatch(value,@"(?:предварительн\w*\s+загрузк\w*.*обновлен|обновлен\w*.*(?:техобслужив|техническ\w*\s+обслужив))",Ignore))return bracket+" Version Pre-Download & Update Notice";
      if(Regex.IsMatch(value,@"Рефакторинг|повторн\w*\s+(?:наем|наём)",Ignore))return value+" RE-Factor";
      if(Regex.IsMatch(value,@"специальн\w*\s+на[еёй]м",Ignore))return bracket+" Chartered Headhunting";
      if(Regex.IsMatch(value,@"(?:событи\w*\s+вход\w*|ежедневн|еженедельн|(?:снабжен|поставк)\w*\s+рассудк|боев\w*\s+пропуск|веб[- ]?событи)",Ignore))return value+" Sign-In Daily";
      string kind=null;
      if(Regex.IsMatch(value,@"развлекательн\w*\s+событи",Ignore))kind="Fun & Games";
      else if(Regex.IsMatch(value,@"(?:сюжетн|повествовательн)\w*\s+событи",Ignore))kind="Narrative";
      else if(Regex.IsMatch(value,@"событи\w*\s+справочник|справочн\w*\s+событи",Ignore))kind="Guide";
      else if(Regex.IsMatch(value,@"фото(?:событи|графическ)|событи\w*\s+фото",Ignore))kind="Camera";
      else if(Regex.IsMatch(value,@"событи\w*\s+с\s+испытани",Ignore))kind="Challenge";
      else if(Regex.IsMatch(value,@"временн\w*\s+событи",Ignore))kind="Limited-Time";
      else if(Regex.IsMatch(value,@"(?:исследовательск|боев|кооперативн|праздничн)\w*\s+событи",Ignore))kind="Exploration";
      return kind==null?value:bracket+" "+kind+" Event Details";
    }
    public static bool MaintenanceTitle(string title){return Regex.IsMatch(Title(title),@"Pre-Download.*Update|Update.*Maintenance|Maintenance.*(?:Notice|Preview)",Ignore);}
    static string DatePart(Match match,string hour,string minute) {
      return CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(Months[match.Groups["month"].Value])+". "+match.Groups["day"].Value+(match.Groups["year"].Success?", "+match.Groups["year"].Value:"")+" at "+hour+":"+minute;
    }
    public static string Dates(string text) {
      string value=Regex.Replace(text,@"\bс\s+(?<first>\d{1,2}):(?<fm>\d{2})\s+до\s+(?<last>\d{1,2}):(?<lm>\d{2})\s+"+DayMonth,
        m=>DatePart(m,m.Groups["first"].Value,m.Groups["fm"].Value)+" – "+DatePart(m,m.Groups["last"].Value,m.Groups["lm"].Value),Ignore);
      value=Regex.Replace(value,@"(?<hour>\d{1,2}):(?<minute>\d{2})\s*,?\s*"+DayMonth,m=>DatePart(m,m.Groups["hour"].Value,m.Groups["minute"].Value),Ignore);
      value=Regex.Replace(value,DayMonth+@"\s*[, ]\s*(?:в\s*)?(?<hour>\d{1,2}):(?<minute>\d{2})",m=>DatePart(m,m.Groups["hour"].Value,m.Groups["minute"].Value),Ignore);
      value=Regex.Replace(value,@"После\s+(?:обновления\s+версии|выхода\s+обновления|выпуска\s+версии|выхода\s+версии)\s*\[([^\]]+)\]",m=>"After ["+m.Groups[1].Value+"] version update",Ignore);
      value=Regex.Replace(value,@"\s+до\s+", " – ",Ignore);
      value=Regex.Replace(value,@"\bс\s+(?=[A-Za-z]+\.?\s+\d{1,2})","",Ignore);
      value=Regex.Replace(value,@"\((?:по\s+времени\s+сервера|часовой\s+пояс\s+сервера)\)","(Server Time)",Ignore);
      return value;
    }
    static readonly string[,] Labels={
      {"Доступность","Availability"},{"Длительность","Availability"},{"Длительность события","Event Duration"},{"Расписание события","Event Time"},{"Время проведения события","Event Time"},{"Время проведения","Event Time"},{"Время события","Event Time"},{"Период события","Event Period"},{"Период проведения","Event Period"},
      {"Время обновления содержимого события","Event Content Update Time"},{"Критерии участия","Participation Criteria"},{"Требования к участию","Participation Requirements"},{"Условия участия","Participation Criteria"},{"Требование","Requirements"},
      {"Сведения о событии","Event Details"},{"Описание события","Event Description"},{"Правила события","Event Rules"},{"Награды события","Event Rewards"},{"Вознаграждения события","Event Rewards"},{"Вознаграждения","Event Rewards"},{"Подробности","Event Details"},{"Время обновления","Event Content Update Time"}
    };
    static readonly string[,] Sections={{"Новые оперативники","New Operators"},{"Новые события найма и поставки","New Headhunting and Arsenal"},{"Новые события","New Events"},{"Новые истории","New Story"},{"Новое оружие","New Weapons"},{"Контент новой версии","New Content"},{"Оптимизация","Optimizations"},{"Исправление ошибок","Bug Fixes"}};
    public static string Text(string text,bool normalizeDates) {
      string value=Quotes(text);
      value=Regex.Replace(value,@"\bUTC\s*[−–—](?=\s*\d)","UTC-",Ignore);
      value=Regex.Replace(value,@"(?:Часовой\s+пояс\s+сервер(?:а|ов)|Сервер)\s+(?<region>Americas\s*/\s*Europe|Europe\s*/\s*Americas|Europe|Asia)\s*:",m=>m.Groups["region"].Value+" Server:",Ignore);
      value=Regex.Replace(value,@"(?<region>Asia|Americas\s*/\s*Europe)\s+сервер\s*:",m=>m.Groups["region"].Value+" Server:",Ignore);
      var lines=value.Split('\n');
      for(int i=0;i<lines.Length;i++) {
        string line=lines[i];var prefix=Regex.Match(line,@"^(?<prefix>\s*(?:[·•■]\s*|▼\s*//\s*)?)(?<body>.*)$");string body=prefix.Groups["body"].Value;bool replaced=false;
        for(int j=0;j<Labels.GetLength(0);j++) {
          var label=Regex.Match(body,"^"+Regex.Escape(Labels[j,0])+@"\s*(?:[:：]\s*(?<rest>.*)|$)",Ignore);
          if(label.Success){line=prefix.Groups["prefix"].Value+Labels[j,1]+": "+label.Groups["rest"].Value;replaced=true;break;}
        }
        if(!replaced) {
          for(int j=0;j<Sections.GetLength(0);j++)if(string.Equals(body.Trim(),Sections[j,0],StringComparison.OrdinalIgnoreCase)){line=prefix.Groups["prefix"].Value+Sections[j,1];replaced=true;break;}
          if(!replaced) {
            var numbered=Regex.Match(line,@"^(?<prefix>\s*\d+[.)]\s*)(?<body>.*)$");
            if(numbered.Success)line=numbered.Groups["prefix"].Value+Title(numbered.Groups["body"].Value);
            else if(line.TrimStart().StartsWith("▼"))line=prefix.Groups["prefix"].Value+Title(body);
          }
        }
        lines[i]=line;
      }
      value=string.Join("\n",lines);
      return normalizeDates?Dates(value):value;
    }
  }
}
