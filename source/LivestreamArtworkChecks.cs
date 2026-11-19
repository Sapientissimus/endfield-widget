using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckLivestreamArtwork(Action<bool,string> check,string folder,CalendarFeedState feed) {
      var entry=CalendarData.Entries(null,null,false,feed).Single();
      var video=feed.Events.Single();
      string file=Path.Combine(folder,"live-thumbnail.jpg");
      byte[] bytes;string type;
      if(File.Exists(file)){bytes=File.ReadAllBytes(file);type="image/jpeg";}
      else {
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Art.argent-flow-portrait"))
        using(var copy=new MemoryStream()){stream.CopyTo(copy);bytes=copy.ToArray();}
        type="image/png";
      }
      using(var handler=new CalendarArtworkFixture(bytes,type))
      using(var transport=new HttpClient(handler)) {
        check(await CalendarArtwork.Download(transport,video.ArtworkUrl)==null && handler.Requests==0,"An ordinary calendar image request cannot download a YouTube thumbnail");
        check(await CalendarArtwork.Download(transport,video.ArtworkUrl,video.Identity)!=null && handler.Requests==1,"The verified stream uses the bounded image downloader with its own thumbnail");
        var oldFeed=calendarFeed;var oldBanners=bannerState;var oldSchedule=schedule;var oldNow=calendarNow;string language=L.Code,query=calendarQuery;
        int mask=calendarFilterMask;
        try {
          if(calendarDialog!=null)calendarDialog.Close();
          calendarArtworkTransportOverride=transport;calendarFeed=feed;bannerState=null;schedule=null;
          calendarNow=()=>entry.At.AddDays(-2);calendarRemembered=false;calendarFilterMask=CalendarData.AllKinds;calendarQuery="";L.Set("ru");
          ShowCalendar();SelectCalendarDay(Clock.Local(entry.At).Date);
          calendarDialog.Width=1160;calendarDialog.Height=790;calendarDialog.UpdateLayout();LayoutCalendar();
          await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
          calendarDialog.UpdateLayout();
          var day=CalendarVisualElements<CalendarArtworkImage>(CalendarFind<ListBox>("CalendarEvents")).FirstOrDefault(i=>i.IsVisible && i.Source!=null);
          var upcoming=CalendarVisualElements<CalendarArtworkImage>(CalendarFind<ListBox>("CalendarAgenda")).FirstOrDefault(i=>i.IsVisible && i.Source!=null);
          check(VisibleStreamThumbnail(day) && VisibleStreamThumbnail(upcoming) &&
            ((CalendarCardModel)day.DataContext).Name=="Трансляция новой версии" &&
            ((CalendarCardModel)upcoming.DataContext).Name=="Трансляция новой версии","Both realized Russian stream cards display the verified horizontal thumbnail beside their localized title");
          if(File.Exists(file)) {
            SaveImage(calendarDialog,Path.Combine(folder,"livestream-thumbnail-wide.png"));
            calendarDialog.Width=580;calendarDialog.UpdateLayout();LayoutCalendar();
            await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            SaveImage(calendarDialog,Path.Combine(folder,"livestream-thumbnail-compact.png"));
          }
        }finally {
          if(calendarDialog!=null)calendarDialog.Close();
          calendarArtworkTransportOverride=null;calendarFeed=oldFeed;bannerState=oldBanners;schedule=oldSchedule;calendarNow=oldNow;
          calendarQuery=query;calendarFilterMask=mask;calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;L.Set(language);
        }
      }
    }
    static bool VisibleStreamThumbnail(CalendarArtworkImage image) {
      if(image==null || !image.IsVisible || image.Source==null || image.Stretch!=Stretch.UniformToFill)return false;
      var viewport=image.Parent as Border;
      var clip=viewport==null?null:viewport.Clip as RectangleGeometry;
      if(viewport==null || clip==null || viewport.ActualWidth!=136 || viewport.ActualHeight!=76 || clip.Rect!=new Rect(0,0,136,76))return false;
      // UniformToFill can arrange a portrait beyond the viewport. Verify the
      // clipped area the user sees and that the image covers every edge of it.
      var bounds=image.TransformToAncestor(viewport).TransformBounds(new Rect(image.RenderSize));
      return bounds.Left<=.01 && bounds.Top<=.01 && bounds.Right>=viewport.ActualWidth-.01 && bounds.Bottom>=viewport.ActualHeight-.01;
    }
  }
}
