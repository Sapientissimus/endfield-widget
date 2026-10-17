using System;
using System.Threading.Tasks;

namespace EndfieldWidget {
  partial class Widget {
    CalendarFeedState calendarFeed;
    bool calendarFeedLive,calendarCacheFailed;
    async Task ApplyCalendarFeed(Task<CalendarFeedState> operation,int epoch) {
      try {
        var result=await operation;if(!CanApplyResponse(epoch))return;
        calendarFeed=result;calendarFeedLive=result.SuccessCount>0;calendarCacheFailed=false;
        if(calendarDialog!=null)RenderCalendar();
        try{await Task.Run(()=>CalendarFeedStore.Save(result));}catch{calendarCacheFailed=true;}
      }catch{if(CanApplyResponse(epoch)){calendarFeedLive=false;if(calendarDialog!=null)RenderCalendar();}}
    }
  }
}
