using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckRussianArticles(Action<bool,string> check,string folder) {
      Func<string,object> fixture=cid=>Json.Decode(File.ReadAllText(Path.Combine(folder,cid+"-ru.json")));
      object overview=fixture("9560"),dedicated=fixture("4489"),preload=fixture("0744");
      string html=Json.Str(overview,"data","data"),title=Json.Str(overview,"data","title");long published=1792026000;
      var schedule=NewsClient.ParseSchedule(html,title,NewsClient.NewsUrl+"/9560",2026);
      check(NewsClient.NewsUrl.Contains("/ru-ru/") && ArticleLanguage.Locale=="ru-ru","Official publication links use the requested Russian locale");
      check(schedule.Version=="Нарисованный приют" && schedule.Start==new DateTimeOffset(2026,10,14,22,0,0,TimeSpan.Zero) && schedule.End==new DateTimeOffset(2026,10,15,4,0,0,TimeSpan.Zero),"Actual Russian version notes retain the exact Europe maintenance instants and Russian name");
      var announced=NewsClient.ParseSchedule(Json.Str(preload,"data","data"),Json.Str(preload,"data","title"),NewsClient.NewsUrl+"/0744",2026);
      check(announced.Version==schedule.Version && announced.Start==schedule.Start && announced.End==schedule.End,"The Russian pre-download notice independently agrees with the maintenance schedule");
      check(ArticleLanguage.MaintenanceTitle(Json.Str(preload,"data","title")) && BannerClient.IsOverviewTitle(title),"Russian metadata is discovered automatically without changing article IDs or maintaining a title list");
      var notices=BannerClient.ParseOverview(html,title,"9560",published);var next=notices.Single(n=>n.Name=="Серебристый Поток");
      check(notices.Count==2 && notices.Any(n=>n.Name=="Си" && n.Banner=="Преображение") && next.Banner=="Звездный поток бежит навстречу очагу","Russian overview parsing finds the two debut operators and their original localized banner names");
      check(!BannerClient.IsDebutTitle("Наем [Рефакторинг: Новый поток родника предков] #1") && !notices.Any(n=>n.Name=="Тантан"),"Russian reruns cannot replace the debut art");
      check(NewsClient.ParseDate(next.LocalStart,TimeSpan.FromHours(-5),2026).ToUniversalTime()==new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero),"The Russian future debut starts at the correct Europe server instant");
      check(NewsClient.ParseDate(next.AsiaLocalStart,TimeSpan.FromHours(8),2026).ToUniversalTime()==new DateTimeOffset(2026,11,5,4,0,0,TimeSpan.Zero) && next.LocalEnd==null,"Asia stays independent and an unannounced next-version end is not invented");
      var first=BannerClient.ParseNotice(Json.Str(dedicated,"data","data"),Json.Str(dedicated,"data","title"),"4489",1791972000);
      check(first.Name=="Си" && first.ReleaseVersion==schedule.Version && BannerClient.ResolveEnd(first.LocalEnd,schedule.End,false,2026)==new DateTimeOffset(2026,11,5,16,59,0,TimeSpan.Zero).ToUnixTimeSeconds(),"The standalone Russian banner resolves its featured operator, release dependency and final minute");
      var events=GameEventDates.Parse(html,title,"9560",published);
      check(events.Count==4 && events.Any(e=>e.Name=="Все оттенки чернил") && events.Any(e=>e.Name=="Игровая арена") && events.Any(e=>e.Name=="Новая эра для сонма душ"),"Russian gameplay events retain official names and exact published deadlines");
      check(!events.Any(e=>e.Name.Contains("Поставка") || e.Name.Contains("подготовка") || e.Name.Contains("Возрождение") || e.Name.Contains("Пылающие")),"Russian daily, sign-in and undated events remain excluded");
      var camera=events.Single(e=>e.Name=="Зеленая антология Карли");
      check(camera.End(false)==new DateTimeOffset(2026,11,5,9,0,0,TimeSpan.Zero) && camera.End(true)==new DateTimeOffset(2026,11,4,20,0,0,TimeSpan.Zero),"Russian time-first dates and explicit server zones give the same confirmed instants");
      var catalogue=BannerClient.ParseLocalizedCatalogue(File.ReadAllText(Path.Combine(folder,"operators-ru.html")));
      check(catalogue.Count>=30 && catalogue.Any(a=>a.Name=="Си" && a.Key=="jsspsi") && catalogue.Any(a=>a.Name=="Серебристый Поток" && a.Key=="minghe"),"The live Russian operator page maps localized names to stable portrait keys without a manual translation table");
      using(var backend=new RussianArticleFixture(overview,dedicated,File.ReadAllText(Path.Combine(folder,"operators-ru.html"))))using(var http=new HttpClient(backend)) {
        var client=new BannerClient(http);var state=await client.Fetch(null,schedule,false);var before=new DateTimeOffset(2026,10,16,8,0,0,TimeSpan.Zero);var upcoming=BannerClient.Next(state,before,false);
        check(upcoming!=null && upcoming.Name=="Серебристый Поток" && BannerClient.Select(state,before,false).Name=="Си" && state.Debuts.All(d=>d.Source.StartsWith(NewsClient.NewsUrl+"/")),"Full discovery uses Russian articles across current art, upcoming debut and official links");
        check(state.Debuts.All(d=>!string.IsNullOrEmpty(d.Portrait)) && state.ArtError==null,"Localized names retain working current and upcoming art");
        check(backend.Requests.Count>0 && backend.Requests.All(u=>u.Contains("lang=ru-ru") || u.Contains("/ru-ru/operator")),"The complete discovery pipeline makes no English publication request");
        var entry=CalendarData.Entries(schedule,state,false).Single(e=>e.Kind==CalendarKind.Debut && e.Name==upcoming.Name);
        var details=await client.FetchCalendarDetail(entry);
        check(details.Description.Contains("Серебристый Поток") && !details.Description.Contains("Тантан") && details.Source.Contains("/ru-ru/"),"Russian inline debut details select only the requested operator");
        var ending=CalendarData.Entries(schedule,state,false).Single(e=>e.Kind==CalendarKind.BannerEnd && e.Name=="Си");
        var separate=await client.FetchCalendarDetail(ending);
        check(separate.Participation.Contains("основную миссию") && separate.Description.Contains("Си"),"Russian participation requirements and banner description remain readable Russian text");
      }
      var cameraEntry=new CalendarEntry{Kind=CalendarKind.EventEnd,Name=camera.Name,Source=camera.Source,At=camera.End(false).Value};
      var cameraDetail=CalendarDetailsData.Parse(html,title,cameraEntry,DateTimeOffset.UtcNow);
      check(cameraDetail.Description.Contains("фотомиссии") && !cameraDetail.Description.Contains("Серебристый") && cameraDetail.Participation.Length==0,"A Russian event gets its own original description and no fabricated participation criteria");
      string future="<p>▼// Расписание события</p><p>С 12:00 21 декабря 2028 года до обновления версии и обслуживания</p><p>▼// Награды события</p><p>Срок магазина: 5 января 2029 года, 12:00.</p><p>Часовой пояс сервера Americas / Europe: UTC-5</p>";
      check(GameEventDates.Parse(future,"Сведения о событии с испытанием [Новый эксперимент]","900001",new DateTimeOffset(2028,12,20,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds()).Count==0,"Unknown Russian event boundaries cannot borrow a shop date or invent a future deadline");
      check(CalendarDetailsData.Key(cameraEntry)!=CalendarDetailsData.Key(new CalendarEntry{Kind=cameraEntry.Kind,Name=cameraEntry.Name,Source=cameraEntry.Source.Replace("/ru-ru/","/en-us/"),At=cameraEntry.At}),"English and Russian detail keys remain isolated during language migration");
      check(NewsClient.ParseDate("29 февраля 2028 года, 06:30",TimeSpan.FromHours(8),2027)==new DateTimeOffset(2028,2,29,6,30,0,TimeSpan.FromHours(8)),"Russian genitive month dates retain the explicit leap year and offset");
      check(BannerClient.FeaturedName("у шестизвёздного оперативника Новая Героиня появляется высокий шанс!")=="Новая Героиня" && BannerClient.FeaturedName("повышен шанс получить 6★ оперативника Новая Героиня!")=="Новая Героиня" && BannerClient.FeaturedName("включает оперативника 6★ с высоким шансом — [Новая Героиня].")=="Новая Героиня","Historical Russian featured-operator grammar also handles previously unknown names");
      check(BannerClient.IsDebutTitle("Специальный наем [Возвращение звезды]"),"A new debut title containing an ordinary return word is not mistaken for a rerun");
      string updateOnly="<p>▼// Время обновления</p><p>26 июня 2026 г., 12:00 (часовой пояс сервера)</p><p>▼// Сведения об обновлении</p><p>Новый этап.</p><p>Часовой пояс сервера Americas / Europe: UTC–5</p>";
      check(GameEventDates.Parse(updateOnly,"Сведения о сюжетном событии [Новый этап]","900002",1782432000L).Count==0 && GameEventDates.DeclaredNames(updateOnly,"Сведения о сюжетном событии [Новый этап]").Count==0,"An explicit content-update time cannot become a deadline or withdraw another article's confirmed event");
      string unicode="<p>▼// Время проведения</p><p>С 21 декабря 2028 г., 12:00 до 27 декабря 2028 г., 04:00 (по времени сервера)</p><p>▼// Вознаграждения</p><p>Награды.</p><p>Часовой пояс серверов Americas / Europe: UTC–5</p>";
      var unicodeEvent=GameEventDates.Parse(unicode,"Сведения о событии с испытанием [Новый эксперимент]","900003",new DateTimeOffset(2028,12,20,0,0,0,TimeSpan.Zero).ToUnixTimeSeconds());
      check(unicodeEvent.Count==1 && unicodeEvent[0].End(false)==new DateTimeOffset(2028,12,27,9,0,0,TimeSpan.Zero) && unicodeEvent[0].End(true)==null,"A Russian Unicode minus preserves the explicit Europe zone and cannot fabricate an Asia zone");
      var operatorLabel=Find<System.Windows.Controls.TextBlock>("OperatorName");
      check(operatorLabel.TextTrimming==System.Windows.TextTrimming.None && operatorLabel.Parent is System.Windows.Controls.Viewbox,"Long localized operator names fit the hero label without replacing the ending with an ellipsis");
    }
  }
  sealed class RussianArticleFixture:HttpMessageHandler {
    readonly object overview,dedicated;readonly string operators;
    public readonly List<string> Requests=new List<string>();
    public RussianArticleFixture(object overview,object dedicated,string operators){this.overview=overview;this.dedicated=dedicated;this.operators=operators;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
      string url=request.RequestUri.ToString();lock(Requests)Requests.Add(url);string body=null;
      if(url.Contains("/api/bulletin") && url.Contains("lang=ru-ru")) {
        if(request.RequestUri.AbsolutePath.EndsWith("/9560"))body=Json.Encode(overview);
        else if(request.RequestUri.AbsolutePath.EndsWith("/4489"))body=Json.Encode(dedicated);
        else if(request.RequestUri.AbsolutePath.EndsWith("/bulletin"))body=Json.Encode(new{code=0,data=new{total=2,list=new[]{new{cid="9560",title=Json.Str(overview,"data","title"),displayTime=1792026000L},new{cid="4489",title=Json.Str(dedicated,"data","title"),displayTime=1791972000L}}}});
      }else if(url.Contains("/ru-ru/operator"))body=operators;
      return Task.FromResult(new HttpResponseMessage(body==null?HttpStatusCode.NotFound:HttpStatusCode.OK){Content=new StringContent(body??"{}",Encoding.UTF8,"application/json")});
    }
  }
}
