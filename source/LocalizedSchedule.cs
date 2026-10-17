using System;
using System.Linq;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class BannerClient {
    public async Task<Schedule> FetchSchedule(){return await InPublication(FetchScheduleCore).ConfigureAwait(false);}
    async Task<Schedule> FetchScheduleCore() {
      var archive=await Archive().ConfigureAwait(false);Schedule best=null;
      foreach(var article in archive.Where(a=>ArticleLanguage.MaintenanceTitle(Json.Str(a,"title")) || IsOverviewTitle(Json.Str(a,"title"))).Take(6)) {
        string cid=Json.Str(article,"cid");var value=await Read(Api+"/"+cid+"?"+Query).ConfigureAwait(false);
        try {
          long published=Json.Num(article,"displayTime");
          var schedule=NewsClient.ParseSchedule(Json.Str(value,"data","data"),FetchedTitle(value,article),NewsClient.NewsUrl+"/"+cid,DateTimeOffset.FromUnixTimeSeconds(published));schedule.PublishedUnix=published;
          if(best==null || schedule.Start>best.Start)best=schedule;
        }catch(FormatException){}
      }
      if(best==null)throw new FormatException(L.T("The maintenance schedule has not been announced yet."));return best;
    }
  }
}
