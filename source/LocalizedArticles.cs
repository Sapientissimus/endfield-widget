using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace EndfieldWidget {
  // Dates and structural grammar are read from the English edition of the same
  // official publication ID. Human names and excerpts come from its selected
  // edition. Nothing guesses a date from the order of translated sentences.
  static class LocalizedArticles {
    sealed class Paragraph {
      public string Text,Marker;
      public HashSet<string> Numbers;
      public Paragraph(string text){Text=text;Marker=Mark(text);Numbers=new HashSet<string>(Regex.Matches(text,@"\d+").Cast<Match>().Select(m=>m.Value));}
      static string Mark(string text){var numbered=Regex.Match(text,@"^\s*(\d+)[.)、．]");if(numbered.Success)return "number:"+numbered.Groups[1].Value;var marker=Regex.Match(text,@"^\s*([■▼※·•・])");return marker.Success?marker.Groups[1].Value.Replace("•","·").Replace("・","·"):"text";}
    }
    public static string Quotes(string value) {
      return (value??"").Replace('「','[').Replace('」',']').Replace('【','[').Replace('】',']').Replace('『','[').Replace('』',']').Replace('«','[').Replace('»',']').Replace('“','[').Replace('”',']').Replace('［','[').Replace('］',']');
    }
    static List<Paragraph> Paragraphs(string html) {
      var matches=Regex.Matches(html??"",@"<p\b[^>]*>(.*?)</p>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
      var lines=matches.Cast<Match>().Select(m=>NewsClient.PlainText(m.Groups[1].Value).Trim()).Where(t=>t.Length>0).ToList();
      if(lines.Count==0)lines=NewsClient.PlainText(html??"").Split('\n').Select(t=>t.Trim()).Where(t=>t.Length>0).ToList();
      if(lines.Count>1200)throw new FormatException("The official article has too many paragraphs.");return lines.Select(t=>new Paragraph(t)).ToList();
    }
    static double Cost(Paragraph a,Paragraph b) {
      double cost=a.Marker==b.Marker?0:4;
      if((a.Marker.StartsWith("number:") || b.Marker.StartsWith("number:")) && a.Marker!=b.Marker)cost+=12;
      if((a.Marker=="■" || a.Marker=="▼" || b.Marker=="■" || b.Marker=="▼") && a.Marker!=b.Marker)cost+=8;
      if(a.Numbers.Count>0 && b.Numbers.Count>0)cost+=1.5*(1-a.Numbers.Intersect(b.Numbers).Count()/(double)Math.Max(a.Numbers.Count,b.Numbers.Count));
      else if(a.Numbers.Count!=b.Numbers.Count)cost+=.7;
      return cost;
    }
    static Dictionary<int,int> Align(List<Paragraph> english,List<Paragraph> localized) {
      int n=english.Count,m=localized.Count;var costs=new double[n+1,m+1];var moves=new byte[n+1,m+1];
      for(int i=1;i<=n;i++){costs[i,0]=i*2.4;moves[i,0]=1;}for(int j=1;j<=m;j++){costs[0,j]=j*2.4;moves[0,j]=2;}
      for(int i=1;i<=n;i++)for(int j=1;j<=m;j++) {
        double match=costs[i-1,j-1]+Cost(english[i-1],localized[j-1]),drop=costs[i-1,j]+2.4,insert=costs[i,j-1]+2.4;
        if(match<=drop && match<=insert){costs[i,j]=match;moves[i,j]=0;}else if(drop<=insert){costs[i,j]=drop;moves[i,j]=1;}else{costs[i,j]=insert;moves[i,j]=2;}
      }
      var result=new Dictionary<int,int>();for(int i=n,j=m;i>0 || j>0;){byte move=moves[i,j];if(move==0){i--;j--;if(Cost(english[i],localized[j])<4)result[i]=j;}else if(move==1)i--;else j--;}
      return result;
    }
    static string[] Names(string text){return Regex.Matches(Quotes(text),@"\[([^\[\]\r\n]{1,180})\]").Cast<Match>().Select(m=>m.Groups[1].Value.Trim()).ToArray();}
    public static object Project(object canonical,object localized) {
      if(Json.Str(canonical,"data","cid")!=Json.Str(localized,"data","cid") || string.IsNullOrWhiteSpace(Json.Str(localized,"data","title")))throw new FormatException("Unmatched localized publication.");
      var english=Paragraphs(Json.Str(canonical,"data","data"));var selected=Paragraphs(Json.Str(localized,"data","data"));
      if(english.Count==0 || selected.Count<3 || selected.Count<english.Count*.55)throw new FormatException("Localized official article is incomplete.");
      var alignment=Align(english,selected);var votes=new Dictionary<string,Dictionary<string,int>>(StringComparer.Ordinal);
      Action<string,string,int> vote=(a,b,weight)=>{
        if(string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || Regex.IsMatch(a,@"^\d+$"))return;
        Dictionary<string,int> choices;if(!votes.TryGetValue(a,out choices)){choices=new Dictionary<string,int>(StringComparer.Ordinal);votes[a]=choices;}int old;choices.TryGetValue(b,out old);choices[b]=old+weight;
      };
      var enTitle=Names(Json.Str(canonical,"data","title"));var localTitle=Names(Json.Str(localized,"data","title"));
      if(enTitle.Length==1 && localTitle.Length==1)vote(enTitle[0],Publications.DisplayText(localTitle[0]),30);
      foreach(var pair in alignment) {
        var a=Names(english[pair.Key].Text);var b=Names(selected[pair.Value].Text);
        if(a.Length==b.Length)for(int i=0;i<a.Length;i++)vote(a[i],Publications.DisplayText(b[i]),english[pair.Key].Marker.StartsWith("number:")?6:2);
      }
      var names=votes.ToDictionary(p=>p.Key,p=>p.Value.OrderByDescending(v=>v.Value).ThenBy(v=>v.Key,StringComparer.Ordinal).First().Key,StringComparer.Ordinal);
      Func<string,string> replace=text=>Regex.Replace(Quotes(text),@"\[([^\[\]\r\n]+)\]",m=>"["+(names.ContainsKey(m.Groups[1].Value)?names[m.Groups[1].Value]:m.Groups[1].Value)+"]");
      var translations=new List<Dictionary<string,object>>();var html=new StringBuilder();
      for(int i=0;i<english.Count;i++) {
        string plain=replace(english[i].Text);html.Append("<p>").Append(WebUtility.HtmlEncode(plain).Replace("\n","<br>" )).Append("</p>");int j;
        if(alignment.TryGetValue(i,out j))translations.Add(new Dictionary<string,object>{{"canonical",plain},{"localized",Publications.DisplayText(selected[j].Text)}});
      }
      var data=(Dictionary<string,object>)((Dictionary<string,object>)canonical)["data"];
      data["title"]=replace(Json.Str(canonical,"data","title"));data["data"]=html.ToString();data["translatedParagraphs"]=translations;
      data["localizedTitle"]=Publications.DisplayText(Json.Str(localized,"data","title"));data["publicationCode"]=Publications.Current.Code;
      return canonical;
    }
    static string Clean(string text){return Regex.Replace(Quotes(text??""),@"\s+"," ").Trim(' ','·','•','・');}
    static string Value(string text,bool stripLabel) {
      text=Clean(text);return stripLabel?Regex.Replace(text,@"^[^:：]{1,90}[:：]\s*",""):text;
    }
    public static string TranslateExcerpt(object article,string excerpt,string separator) {
      var pairs=Json.Items(Json.At(article,"data","translatedParagraphs")).OfType<Dictionary<string,object>>().ToList();if(pairs.Count==0)return excerpt;
      var output=new List<string>();
      foreach(string chunk in (excerpt??"").Split(new[]{separator},StringSplitOptions.RemoveEmptyEntries)) {
        string target=Clean(chunk);if(target.Length==0)continue;Dictionary<string,object> found=null;bool foundLabel=false;
        foreach(var pair in pairs) {
          string canonical=Json.Str(pair,"canonical");bool label=Regex.IsMatch(Clean(canonical),@"^(?:Event (?:Details|Description|Rules)|Participation(?: Criteria| Requirements)?|Requirements|Eligibility)\s*[:：]",RegexOptions.IgnoreCase);
          string value=Value(canonical,label);
          if(value==target || target.EndsWith("…") && value.StartsWith(target.TrimEnd('…'),StringComparison.Ordinal)){found=pair;foundLabel=label;break;}
        }
        if(found!=null){string translated=Regex.Replace(Json.Str(found,"localized"),@"\s+"," ").Trim(' ','·','•','・');if(foundLabel)translated=Regex.Replace(translated,@"^[^:：]{1,90}[:：]\s*","");if(translated.Length>0)output.Add(translated);}
      }
      string result=string.Join(separator,output);int limit=separator=="\n\n"?1600:1000;return result.Length<=limit?result:result.Substring(0,limit-1)+"…";
    }
  }
}
