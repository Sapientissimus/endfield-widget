using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EndfieldWidget {
  sealed class PublicationContext {
    public string Code,Locale;
    public bool Native;
    public PublicationContext(string code,string locale,bool native){Code=code;Locale=locale;Native=native;}
  }
  static class Publications {
    static readonly AsyncLocal<PublicationContext> ambient=new AsyncLocal<PublicationContext>();
    public static PublicationContext Current {get{return ambient.Value??ForLanguage(L.Code);}}
    public static string Locale {get{return Current.Locale;}}
    public static bool Native {get{return Current.Native;}}
    public static PublicationContext ForLanguage(string code) {
      string locale=code=="uk" || code=="en"?"en-us":code=="zh-Hans" || code=="zh-Hant"?"zh-tw":code=="es-419"?"es-mx":code=="pt-BR"?"pt-br":code=="fr"?"fr-fr":code=="de"?"de-de":code=="it"?"it-it":code=="ru"?"ru-ru":code=="id"?"id-id":code=="vi"?"vi-vn":code=="th"?"th-th":code=="ja"?"ja-jp":"ko-kr";
      return new PublicationContext(code,locale,false);
    }
    sealed class Restore:IDisposable {readonly PublicationContext previous;public Restore(PublicationContext value){previous=ambient.Value;ambient.Value=value;}public void Dispose(){ambient.Value=previous;}}
    public static IDisposable Enter(PublicationContext value){return new Restore(value);}
    internal static IDisposable NativeFixture(string locale){return Enter(new PublicationContext(locale=="ru-ru"?"ru":"en",locale,true));}
    public static string NewsUrl {get{return "https://endfield.gryphline.com/"+Locale+"/news";}}
    public static string Query {get{return "lang="+(Native?Locale:"en-us")+"&code=arknights_endfield_official";}}
    public static string Cache(string name){return Cache(name,Current.Code);}
    public static string Cache(string name,string code){code=L.Normalize(code);return name+"-"+(code=="uk"?"en":code.ToLowerInvariant())+".json";}
    public static bool Official(string url){return url!=null && Regex.IsMatch(url,@"^https://endfield\.gryphline\.com/(?:en-us|fr-fr|de-de|it-it|ru-ru|es-mx|pt-br|id-id|vi-vn|th-th|ja-jp|ko-kr|zh-tw)/news/\d+$",RegexOptions.CultureInvariant);}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern int LCMapStringEx(string locale,uint flags,string source,int count,StringBuilder target,int capacity,IntPtr version,IntPtr reserved,IntPtr sort);
    public static string DisplayText(string value) {
      if(Current.Code!="zh-Hans" || string.IsNullOrEmpty(value))return value;
      var result=new StringBuilder(value.Length*2+16);int count=LCMapStringEx("zh-CN",0x02000000,value,-1,result,result.Capacity,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
      if(count==0)throw new InvalidOperationException("Chinese script conversion failed.");return result.ToString(0,count-1);
    }
  }
}
