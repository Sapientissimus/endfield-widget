using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    async Task CheckCalendarDetailsAndMotion(Action<bool,string> check,string folder) {
      string directory=Storage.DirectoryPath;var oldNow=calendarNow;var oldFeed=calendarFeed;var oldBanners=bannerState;var oldSchedule=schedule;var oldCache=calendarDetailCache;var oldZone=Clock.ZoneOverride;
      try {
        Storage.DirectoryPath=Path.Combine(folder,"details-isolated-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Storage.DirectoryPath);calendarDetailCache=null;Clock.ZoneOverride=()=>TimeZoneInfo.Utc;
        var now=new DateTimeOffset(2029,3,1,12,0,0,TimeSpan.Zero);var entry=new CalendarEntry{Id="detail",Name="New Machine",Kind=CalendarKind.EventEnd,Source=NewsClient.NewsUrl+"/80010",At=now.AddDays(4)};
        string html="<script>private fake script</script><p>· Event Details: Explore the new facility and solve its puzzles.</p><p>· Participation Criteria: Complete [A New Beginning].</p><p>· Event Time: Mar. 2 – Mar. 5.</p>";
        var parsed=CalendarDetailsData.Parse(html,"[New Machine] Challenge Event Details",entry,now);
        check(parsed.Description.Contains("facility") && !parsed.Description.Contains("script") && !parsed.Description.Contains("Mar. 2"),"Details use official descriptive text and exclude scripts and unconverted server dates");
        check(parsed.Participation=="Complete [A New Beginning].","Participation requirements are separated from the description and time fields");
        var overviewJson=Json.Decode(File.ReadAllText(Path.Combine(folder,"version-9560.json")));string overview=Json.Str(overviewJson,"data","data");
        var carly=new CalendarEntry{Id="carly",Name="Carly's Verdant Anthology",Kind=CalendarKind.EventEnd,Source=NewsClient.NewsUrl+"/9560",At=now};
        var excerpt=CalendarDetailsData.Parse(overview,Json.Str(overviewJson,"data","title"),carly,now);
        check(excerpt.Description.Contains("1500") && !excerpt.Description.Contains("Arena Points") && !excerpt.Description.Contains("every day"),"A version overview yields only the requested event instead of neighboring missions or other events");
        var argent=new CalendarEntry{Name="Argent Flow",Kind=CalendarKind.Debut,Source=carly.Source,At=now};var debutExcerpt=CalendarDetailsData.Parse(overview,Json.Str(overviewJson,"data","title"),argent,now);
        check(debutExcerpt.Description.Contains("Argent Flow") && !debutExcerpt.Description.Contains("Portrait Frame: Carly"),"An overview-only future debut is matched by its featured operator rather than by the banner title");
        bool rejected=false;try{CalendarDetailsData.Parse(html,"[Other Event] Challenge Event Details",entry,now);}catch(FormatException){rejected=true;}
        check(rejected,"A source for a different event is never attached to the selected card");
        var bannerJson=Json.Decode(File.ReadAllText(Path.Combine(Path.GetDirectoryName(folder),"research","banners","4489.json")));
        var si=new CalendarEntry{Id="si-detail",Name="Si",Kind=CalendarKind.Debut,Source=NewsClient.NewsUrl+"/4489",At=now};
        var bannerDetail=CalendarDetailsData.Parse(Json.Str(bannerJson,"data","data"),Json.Str(bannerJson,"data","title"),si,now);
        check(bannerDetail.Description.Contains("Rate-UP") && !bannerDetail.Description.Contains("Urgent Recruitment") && bannerDetail.Participation.Contains("Break the Siege"),"Banner details isolate the operator announcement and its actual entry requirement");
        var longDetail=CalendarDetailsData.Parse("<p>· Event Details: "+new string('a',5000)+"</p>","[New Machine] Challenge Event Details",entry,now);
        check(longDetail.Description.Length<=1600,"Very long official descriptions are bounded for the inline card");
        var empty=CalendarDetailsData.Parse("<p>· Event Time: Announced later.</p>","[New Machine] Challenge Event Details",entry,now);
        check(empty.Description.Length==0 && empty.Participation.Length==0,"Missing descriptions and participation criteria stay absent rather than being invented");
        using(var fixture=new CalendarBulletinFixture())using(var client=new HttpClient(fixture)) {
          fixture.Add("80010","[New Machine] Challenge Event Details",now,html);var reader=new BannerClient(client);var read=await reader.FetchCalendarDetail(entry);
          check(read.Description==parsed.Description && fixture.Requests==1 && fixture.Pages==0,"Opening details reads one official article without reloading the calendar archive");
        }
        CalendarDetailsStore.Save(parsed);var loaded=CalendarDetailsStore.Load();check(loaded.ContainsKey(parsed.Key) && loaded[parsed.Key].Participation==parsed.Participation,"Public detail descriptions and requirements survive a restart-shaped cache read");
        CalendarDetailsStore.Save(bannerDetail);File.WriteAllText(Path.Combine(Storage.DirectoryPath,"calendar-details-ru.json"),"{broken");
        check(CalendarDetailsStore.Load().ContainsKey(parsed.Key),"A damaged detail cache recovers a valid backup");
        CalendarDetailsStore.Save(parsed);var corrected=new CalendarEntry{Id=entry.Id,Name=entry.Name,Source=entry.Source,Kind=entry.Kind,At=entry.At.AddDays(1)};
        check(CalendarDetailsData.Key(corrected)!=parsed.Key,"A corrected event date cannot reuse a brief cached for the old occurrence");
        var alien=new CalendarEntry{Name="New Machine",Source="https://example.com/1",At=now};rejected=false;try{CalendarDetailsData.Parse(html,"[New Machine] Challenge Event Details",alien,now);}catch(FormatException){rejected=true;}
        check(rejected,"Untrusted detail URLs are rejected before fetching or displaying content");
        calendarNow=()=>now;calendarFeed=null;schedule=null;bannerState=new BannerState{GameEvents=new List<GameEvent>{CalendarEvent(entry.Name,"80010",now.AddDays(-1),now.AddDays(1),entry.At)}};calendarRemembered=false;calendarQuery="";calendarFilterMask=15;calendarDetailCache=new Dictionary<string,CalendarDetailRecord>();
        int requests=0;var pending=new TaskCompletionSource<CalendarDetailRecord>();calendarDetailFetchOverride=e=>{requests++;return pending.Task;};
        ShowCalendar();SelectCalendarDay(entry.At.Date);calendarDialog.UpdateLayout();await calendarDialog.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var day=calendarDayModels.Single();var agenda=calendarAgendaModels.Single();var state=day.Details;
        check(ReferenceEquals(state,agenda.Details) && requests==0,"Day and agenda share one detail state and do not fetch descriptions until requested");
        var toggle=CalendarVisualElements<ToggleButton>(CalendarFind<ListBox>("CalendarEvents")).First(t=>t.DataContext==day);toggle.IsChecked=true;
        check(state.IsExpanded && state.Busy && requests==1,"The real detail toggle starts a non-blocking article load inside its card");
        state.IsExpanded=false;state.IsExpanded=true;check(requests==1,"Rapid collapse and expansion coalesce an in-flight article request");
        parsed.Key=CalendarDetailsData.Key(day.Entry);parsed.Checked=DateTimeOffset.UtcNow;pending.SetResult(parsed);await Task.Delay(80);calendarDialog.UpdateLayout();
        check(state.Description.Contains("facility") && state.Participation.Contains("Beginning") && !state.Busy,"Completed details populate both inline descriptions and requirements");
        check(day.PeriodText.Contains("3/2/2029") && day.PeriodText.Contains("3/5/2029"),"The expanded period uses confirmed beginning and ending in the computer's timezone");
        string opened=null;calendarOpenOverride=url=>opened=url;state.IsExpanded=false;state.IsExpanded=true;check(opened==null && requests==1,"Expanding a cached detail does not open a browser or repeat its network request");day.OpenSource.Execute(day.SourceTarget);check(opened==entry.Source,"The separate source action still opens the official announcement");
        SelectCalendarDay(entry.At.Date.AddMonths(1));var pane=CalendarFind<Grid>("CalendarMonthPane");check(!pane.HasAnimatedProperties && pane.Opacity==1,"Changing month applies immediately at full opacity");
        SelectCalendarDay(entry.At.Date);SelectCalendarDay(entry.At.Date.AddMonths(-1));SelectCalendarDay(entry.At.Date);
        check(!pane.HasAnimatedProperties && pane.Opacity==1 && pane.RenderTransform==Transform.Identity,"Rapid month changes preserve the final month without animation clocks or offsets");
        CalendarFilterClicked(CalendarFind<ToggleButton>("CalendarAll"),new RoutedEventArgs());check(!pane.HasAnimatedProperties && pane.Opacity==1,"Filter changes apply immediately without animation");
        state.IsExpanded=true;calendarDialog.UpdateLayout();await Task.Delay(30);SaveImage(calendarDialog,Path.Combine(folder,"calendar-details-expanded.png"));
        state.IsExpanded=false;calendarDialog.UpdateLayout();var reveals=CalendarVisualElements<CalendarReveal>(CalendarFind<ListBox>("CalendarEvents")).ToList();
        check(reveals.Count>0 && reveals.All(v=>v.ActualHeight<1 && !v.IsHitTestVisible && KeyboardNavigation.GetTabNavigation(v)==KeyboardNavigationMode.None),"Collapsed detail panels occupy no space and do not capture pointer or keyboard navigation");
        state.IsExpanded=true;calendarDialog.UpdateLayout();check(reveals.Any(v=>v.ActualHeight>30) && reveals.All(v=>!v.HasAnimatedProperties),"Expanding a loaded description reveals all content immediately without animation");
        var failure=new TaskCompletionSource<CalendarDetailRecord>();calendarDetailFetchOverride=e=>failure.Task;state.Retry.Execute(null);failure.SetException(new HttpRequestException("offline"));await Task.Delay(30);
        check(state.Description.Contains("facility") && state.Notice.Contains("saved") && state.RetryVisibility==Visibility.Visible,"An offline refresh keeps the previously confirmed description and offers retry");
        var late=new TaskCompletionSource<CalendarDetailRecord>();calendarDetailFetchOverride=e=>late.Task;state.Retry.Execute(null);calendarDialog.Close();late.SetResult(parsed);await Task.Delay(30);
        check(calendarDialog==null && calendarDetailStates.Count==0,"Closing the calendar discards view state and safely ignores late detail responses");
        calendarDetailCache=new Dictionary<string,CalendarDetailRecord>();calendarDetailFetchOverride=e=>Task.FromException<CalendarDetailRecord>(new HttpRequestException("offline"));ShowCalendar();SelectCalendarDay(entry.At.Date);var missing=calendarDayModels.Single().Details;missing.IsExpanded=true;await Task.Delay(30);
        check(missing.Description=="" && missing.RetryVisibility==Visibility.Visible && missing.Notice.Contains("Could not"),"A failed uncached request shows a clear failure instead of fabricated event content");
        calendarDetailFetchOverride=e=>Task.FromResult(parsed);missing.Retry.Execute(null);await Task.Delay(30);check(missing.Description.Contains("facility") && missing.RetryVisibility==Visibility.Collapsed,"Retry recovers inline details without reopening the calendar");
        SaveImage(calendarDialog,Path.Combine(folder,"calendar-details-recovered.png"));
      }finally {
        if(calendarDialog!=null)calendarDialog.Close();calendarDetailFetchOverride=null;calendarDetailCache=oldCache;Storage.DirectoryPath=directory;calendarNow=oldNow;calendarFeed=oldFeed;bannerState=oldBanners;schedule=oldSchedule;Clock.ZoneOverride=oldZone;calendarOpenOverride=null;calendarRemembered=false;calendarSnapshot=null;calendarRecords=null;calendarQuery="";calendarFilterMask=15;
      }
    }
  }
}
