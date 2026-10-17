using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace EndfieldWidget {
  public sealed class LanguageChoice {
    public string Code {get;private set;}
    public string Name {get;private set;}
    public LanguageChoice(string code,string name){Code=code;Name=name;}
    public override string ToString(){return Name;}
  }
  static class L {
    public static readonly LanguageChoice[] Languages={
      new LanguageChoice("en","English"),new LanguageChoice("fr","Français"),
      new LanguageChoice("de","Deutsch"),new LanguageChoice("it","Italiano"),new LanguageChoice("es-419","Español (Latinoamérica)"),
      new LanguageChoice("pt-BR","Português (Brasil)"),new LanguageChoice("ru","Русский"),new LanguageChoice("id","Bahasa Indonesia"),
      new LanguageChoice("vi","Tiếng Việt"),new LanguageChoice("th","ไทย"),new LanguageChoice("zh-Hans","简体中文"),
      new LanguageChoice("zh-Hant","繁體中文"),new LanguageChoice("ja","日本語"),new LanguageChoice("ko","한국어"),new LanguageChoice("uk","Українська")};
    static readonly Dictionary<string,Dictionary<string,string>> catalogs=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string> resourceKeys=new Dictionary<string,string>(StringComparer.Ordinal);
    static readonly ResourceDictionary resources=new ResourceDictionary();
    static string code="en";
    static CultureInfo culture=CreateCulture("en");
    public static string Code {get{return code;}}
    public static CultureInfo Culture {get{return culture;}}
    public static ResourceDictionary Resources {get{return resources;}}
    public static string Normalize(string value){var match=Languages.FirstOrDefault(l=>string.Equals(l.Code,value,StringComparison.OrdinalIgnoreCase));return match==null?"en":match.Code;}
    public static string DateCaption(DateTime date,bool year) {
      string pattern=code=="ja" || code.StartsWith("zh-")?(year?"yyyy年M月d日":"M月d日"):code=="ko"?(year?"yyyy년 M월 d일":"M월 d일"):code=="en"?(year?"MMMM d, yyyy":"MMMM d"):year?"d MMMM yyyy":"d MMMM";
      return date.ToString(pattern,culture);
    }
    static CultureInfo CreateCulture(string language) {
      string name=language=="en"?"en-US":language=="uk"?"uk-UA":language=="zh-Hans"?"zh-CN":language=="zh-Hant"?"zh-TW":language=="es-419"?"es-MX":language=="pt-BR"?"pt-BR":language=="ja"?"ja-JP":language=="ko"?"ko-KR":language=="th"?"th-TH":language=="id"?"id-ID":language=="vi"?"vi-VN":language=="fr"?"fr-FR":language=="de"?"de-DE":language=="it"?"it-IT":"ru-RU";
      var result=(CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
      // Game dates always use Gregorian years, including Thai Windows locales.
      result.DateTimeFormat.Calendar=new GregorianCalendar();return result;
    }
    public static Dictionary<string,string> Catalog(string language) {
      lock(catalogs) {
        Dictionary<string,string> found;if(catalogs.TryGetValue(language,out found))return found;
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Locale."+language+".json")) {
          if(stream==null)throw new InvalidOperationException("Missing embedded language catalog: "+language);
          using(var reader=new StreamReader(stream,Encoding.UTF8))found=new JavaScriptSerializer{MaxJsonLength=2000000}.Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
        }
        catalogs[language]=found;return found;
      }
    }
    public static string T(string key) {
      if(string.IsNullOrEmpty(key))return key;string value;
      if(Catalog(code).TryGetValue(key,out value) && !string.IsNullOrEmpty(value))return value;
      if(code!="en" && Catalog("en").TryGetValue(key,out value) && !string.IsNullOrEmpty(value))return value;
      return key;
    }
    public static string Format(string key,params object[] values){return string.Format(culture,T(key),values);}
    public static string ResourceKey(string key) {
      string found;if(resourceKeys.TryGetValue(key,out found))return found;
      using(var sha=SHA256.Create())found="Locale."+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","").Substring(0,24).ToLowerInvariant();resourceKeys[key]=found;return found;
    }
    public static void Set(string language) {
      code=Normalize(language);culture=CreateCulture(code);
      foreach(string key in Catalog("en").Keys)resources[ResourceKey(key)]=T(key);
      string font=code=="ja"?"Yu Gothic UI":code=="ko"?"Malgun Gothic":code=="zh-Hans"?"Microsoft YaHei UI":code=="zh-Hant"?"Microsoft JhengHei UI":code=="th"?"Leelawadee UI":"Segoe UI";
      resources["LocaleFontFamily"]=new FontFamily(font);resources["LocaleXmlLanguage"]=XmlLanguage.GetLanguage(culture.Name);
    }
    public static string ReText(string text,string previousLanguage) {
      if(string.IsNullOrEmpty(text) || previousLanguage==code)return text;
      var previous=Catalog(previousLanguage);string exact=previous.FirstOrDefault(p=>p.Value==text).Key;if(exact!=null)return T(exact);
      // Cached UI errors can have a diagnostic stage/code suffix. Only replace
      // complete known phrases; operator names and official prose never enter here.
      foreach(var pair in previous.Where(p=>p.Value.Length>12).OrderByDescending(p=>p.Value.Length))if(text.Contains(pair.Value))text=text.Replace(pair.Value,T(pair.Key));
      return text;
    }
    public static string Unit(int value,string one,string few,string many) {
      int n=Math.Abs(value%100),last=Math.Abs(value%10);
      if(code=="uk" || code=="ru")return n>=11 && n<=14?many:last==1?one:last>=2 && last<=4?few:many;
      return (code=="fr"?Math.Abs(value)<=1:Math.Abs(value)==1)?one:many;
    }
  }
}
