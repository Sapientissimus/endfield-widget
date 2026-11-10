using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckCalendarCardArtwork(Action<bool,string> check,string folder) {
      var oldFeed=calendarFeed;
      var search=CalendarFind<TextBox>("CalendarSearch");
      string originalSearch=search.Text;
      var originalDay=calendarDay;
      var originalWidth=calendarDialog.Width;
      var originalHeight=calendarDialog.Height;
      string coverPath=Path.Combine(folder,"calendar-published-cover.jpg");
      byte[] bytes;
      if(File.Exists(coverPath))bytes=File.ReadAllBytes(coverPath);
      else {
        var encoder=new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(96,54,96,96,PixelFormats.Bgr24,null,new byte[96*54*3],96*3)));
        using(var buffer=new MemoryStream()){encoder.Save(buffer);bytes=buffer.ToArray();}
      }
      using(var handler=new CalendarArtworkFixture(bytes))
      using(var transport=new HttpClient(handler))try {
        calendarArtworkTransportOverride=transport;
        calendarFeed=CalendarFeedState.Seed(bannerState);
        var overview=calendarFeed.Sources.Single(s=>s.Source.EndsWith("/9560",StringComparison.Ordinal));
        overview.IsVersionOverview=true;
        overview.ArtworkUrl="https://web-static.hg-cdn.com/upload/calendar.jpg";
        overview.ArtworkTitle="Official version overview";
        var camera=overview.Events.Single(e=>e.Name.Contains("Carly"));
        var ownSource=NewsClient.NewsUrl+"/90004";
        var dedicated=new CalendarSourceState{Source=ownSource,ArtworkUrl="https://web-static.hg-cdn.com/upload/event.jpg",
          Events=new List<GameEvent>{new GameEvent{Name=camera.Name,Source=ownSource,EuropeEndUnix=camera.EuropeEndUnix}}};
        var entry=new CalendarEntry{Name=camera.Name,Source=overview.Source,At=DateTimeOffset.FromUnixTimeSeconds(camera.EuropeEndUnix)};
        check(CalendarArtwork.CardArticle(new[]{overview,dedicated},entry)==dedicated,"An event's matching dedicated illustration takes priority over its version overview");
        entry.Source=ownSource;dedicated.ArtworkUrl=null;
        check(CalendarArtwork.CardArticle(new[]{overview,dedicated},entry)==overview,"An event with no dedicated artwork uses its matching official overview");
        calendarDialog.Width=1160;calendarDialog.Height=790;calendarDialog.UpdateLayout();LayoutCalendar();
        search.Text="Carly";ApplyCalendarSearch();SelectCalendarDay(originalDay);
        RenderCalendar();calendarDialog.UpdateLayout();
        await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        calendarDialog.UpdateLayout();
        var day=CalendarVisualElements<CalendarArtworkImage>(CalendarFind<ListBox>("CalendarEvents")).FirstOrDefault(i=>i.IsVisible && i.Source!=null);
        var agenda=CalendarVisualElements<CalendarArtworkImage>(CalendarFind<ListBox>("CalendarAgenda")).FirstOrDefault(i=>i.IsVisible && i.Source!=null);
        check(day!=null && agenda!=null && day.ActualWidth>day.ActualHeight && agenda.ActualWidth>agenda.ActualHeight &&
          ((CalendarCardModel)day.DataContext).ArtworkCaption==overview.ArtworkTitle,"Cards sourced from the official overview show an attributed horizontal thumbnail in both selected day and upcoming events");
        if(File.Exists(coverPath)) {
          SaveImage(calendarDialog,Path.Combine(folder,"calendar-artwork-wide.png"));
          calendarDialog.Width=580;calendarDialog.UpdateLayout();LayoutCalendar();
          await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          SaveImage(calendarDialog,Path.Combine(folder,"calendar-artwork-compact.png"));
        }
      }finally {
        calendarArtworkTransportOverride=null;
        calendarFeed=oldFeed;search.Text=originalSearch;ApplyCalendarSearch();SelectCalendarDay(originalDay);
        calendarDialog.Width=originalWidth;calendarDialog.Height=originalHeight;RenderCalendar();calendarDialog.UpdateLayout();LayoutCalendar();
      }
    }
  }
}
