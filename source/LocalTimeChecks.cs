using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndfieldWidget {
  partial class Widget {
    static IEnumerable<TextBlock> TimeLabels(DependencyObject root) {
      var text=root as TextBlock;if(text!=null)yield return text;
      for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in TimeLabels(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    async Task CheckLocalTimeViews(Action<bool,string> check,Window dialog) {
      var previousZone=Clock.ZoneOverride;var previousNow=calendarNow;string saved=Json.Encode(config);var oldSanity=sanity;var oldSchedule=schedule;var oldBanners=bannerState;string oldToken=accountToken,oldError=sanityError;bool oldAwaiting=awaitingFreshSanity,oldVisible=window.IsVisible;
      try {
        var moment=new DateTimeOffset(2026,10,15,0,30,0,TimeSpan.Zero);Clock.ZoneOverride=null;
        check(Clock.Local(moment).EqualsExact(TimeZoneInfo.ConvertTime(moment,TimeZoneInfo.Local)),"The default display zone is the actual Windows local zone, including its daylight-saving preference");
        Clock.RefreshSystemZone();check(Clock.Local(moment).EqualsExact(TimeZoneInfo.ConvertTime(moment,TimeZoneInfo.Local)),"Refreshing cached system time-zone data retains the Windows local-zone contract");
        string[] zones={"UTC","FLE Standard Time","Eastern Standard Time","India Standard Time"};string[] dates={"10/15/2026","10/15/2026","10/14/2026","10/15/2026"},times={"00:30","03:30","20:30","06:00"};
        for(int i=0;i<zones.Length;i++) {
          var zone=TimeZoneInfo.FindSystemTimeZoneById(zones[i]);Clock.ZoneOverride=()=>zone;
          check(Clock.Date(moment)==dates[i] && Clock.Time(moment)==times[i],"The same UTC event converts to the explicit expected local date and time: "+zones[i]);
          var entry=new CalendarEntry{Title="Fixture",At=moment};var day=DateTime.ParseExact(dates[i],"M/d/yyyy",System.Globalization.CultureInfo.InvariantCulture);
          check(CalendarData.OnDay(new[]{entry},day).Count()==1 && !CalendarData.OnDay(new[]{entry},day.AddDays(1)).Any() && CalendarData.NearestDay(new[]{entry},moment.AddMinutes(-5))==day,"Calendar membership and nearest-event navigation follow the local calendar day: "+zones[i]);
        }
        var europe=TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");Clock.ZoneOverride=()=>europe;
        check(Clock.Local(new DateTimeOffset(2026,3,29,0,59,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(2) && Clock.Local(new DateTimeOffset(2026,3,29,1,1,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(3),"A European spring transition uses the offset belonging to each event instant");
        check(Clock.Local(new DateTimeOffset(2026,10,25,0,59,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(3) && Clock.Local(new DateTimeOffset(2026,10,25,1,1,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(2),"A European autumn transition preserves the repeated local hour without changing UTC instants");
        var america=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");Clock.ZoneOverride=()=>america;
        check(Clock.Local(new DateTimeOffset(2026,3,8,6,59,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(-5) && Clock.Local(new DateTimeOffset(2026,3,8,7,1,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(-4),"A US spring transition follows US rules rather than European dates");
        check(Clock.Local(new DateTimeOffset(2026,11,1,5,59,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(-4) && Clock.Local(new DateTimeOffset(2026,11,1,6,1,0,TimeSpan.Zero)).Offset==TimeSpan.FromHours(-5),"A US autumn transition follows its own event-date offset");
        var fixedZone=TimeZoneInfo.CreateCustomTimeZone("fixed-fixture",TimeSpan.FromHours(2),"fixed-fixture","fixed-fixture");Clock.ZoneOverride=()=>fixedZone;
        check(Clock.Local(moment).Offset==TimeSpan.FromHours(2),"A zone without daylight-saving rules does not acquire a guessed seasonal offset");

        var start=new DateTimeOffset(2026,11,5,17,0,0,TimeSpan.Zero);var noticeNow=start.AddMinutes(-30);
        var update=new Schedule{Version="Fixture",Source=NewsClient.NewsUrl+"/9560",Checked=noticeNow,Start=start,End=start.AddHours(6)};var options=new Config{NotifyMaintenance=true,MaintenanceLeadMinutes=60};var policy=new NotificationPolicy(null);
        Clock.ZoneOverride=()=>america;var notice=policy.Next(options,null,update,noticeNow,false,true,false);
        check(notice!=null && notice.Message.Contains("11/5/2026, 12:00–18:00") && notice.Message.EndsWith("12:00–18:00"),"An existing maintenance alert shows local event times without a city label");
        policy.Deliver(notice,noticeNow,false,n=>true);Clock.ZoneOverride=()=>TimeZoneInfo.Utc;
        check(policy.Next(options,null,update,noticeNow,false,true,false)==null && policy.History.Sent.Count==1,"Changing display zones does not duplicate an already delivered UTC-keyed maintenance alert");
        var resource=new Sanity{RoleId="fixture",Current=99,Maximum=100,Checked=noticeNow,ServerTime=noticeNow.ToUnixTimeSeconds(),FullAt=start.ToUnixTimeSeconds()};options=new Config{RoleId="fixture",NotifySanity=true};policy=new NotificationPolicy(null);
        notice=policy.Next(options,resource,null,noticeNow,true,false,true);policy.Deliver(notice,noticeNow,false,n=>true);Clock.ZoneOverride=()=>america;
        check(notice!=null && policy.Next(options,resource,null,noticeNow,true,false,true)==null && policy.History.Sent.Count==1,"A resource notification also retains its single recovery-cycle marker across a zone change");

        var now=DateTimeOffset.UtcNow;config.MicroMode=config.Compact=false;config.ShowNextDebut=config.ShowUpdates=true;accountToken="local-time-fixture";awaitingFreshSanity=false;sanityError="";
        sanity=new Sanity{RoleId="fixture",Current=93,Maximum=360,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddHours(2).ToUnixTimeSeconds()};schedule=update;
        bannerState=new BannerState{Debuts=new List<Debut>{new Debut{Name="Argent Flow",Key="local-fixture",Source=NewsClient.NewsUrl+"/9560",EuropeStart=start,AsiaStart=start}}};
        Clock.ZoneOverride=()=>TimeZoneInfo.Utc;Paint();window.UpdateLayout();int epoch=recovery.Epoch;long resourceDeadline=sanity.FullAt;var originalStart=schedule.Start;
        check(Find<TextBlock>("NextBannerDate").Text=="11/5/2026 at 17:00","The widget initially formats the stored debut instant in the selected display-zone fixture");
        Clock.ZoneOverride=()=>america;TimeSettingsChanged(null,EventArgs.Empty);await Task.Delay(100);window.UpdateLayout();
        check(Find<TextBlock>("NextBannerDate").Text=="11/5/2026 at 12:00" && recovery.Epoch==epoch && sanity.FullAt==resourceDeadline && schedule.Start==originalStart,"A time-settings event repaints local dates without changing source timestamps or invalidating account data");
        check(window.FindName("ClockBlock")==null && window.FindName("ClockLabel")==null && window.FindName("CompactClockLabel")==null && dialog.FindName("ShowClockSwitch")==null,"The full and compact digital clocks and their settings switch are removed");
        check(TimeLabels(window).All(t=>!t.Text.Contains("UTC+") && !t.Text.Contains("GMT+")),"The widget contains no repeated offset badges");
        string before=Find<TextBlock>("RecoveryLabel").Text;window.Hide();var india=TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");Clock.ZoneOverride=()=>india;ApplyLocalTimeSettings();
        check(!window.IsVisible && Find<TextBlock>("RecoveryLabel").Text==before,"A system-zone update does not reveal or repaint a hidden widget");
        ShowWidget();window.UpdateLayout();check(Find<TextBlock>("NextBannerDate").Text=="11/5/2026 at 22:30","Showing the widget uses the new local zone immediately, including fractional-hour offsets");
        calendarNow=()=>moment;Clock.ZoneOverride=()=>america;ShowCalendar();((Button)calendarDialog.FindName("CalendarToday")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(calendarDay==new DateTime(2026,10,14),"Calendar Today uses the local date even when it differs from the UTC day");
        ApplyLocalTimeSettings();calendarDialog.UpdateLayout();check(TimeLabels(calendarDialog).All(t=>!t.Text.Contains("UTC+") && !t.Text.Contains("GMT+")),"The open calendar refreshes without timezone reminders");
      }finally {
        if(calendarDialog!=null)calendarDialog.Close();calendarNow=previousNow;Clock.ZoneOverride=previousZone;
        var restored=new JavaScriptSerializer().Deserialize<Config>(saved);foreach(var property in typeof(Config).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        sanity=oldSanity;schedule=oldSchedule;bannerState=oldBanners;accountToken=oldToken;sanityError=oldError;awaitingFreshSanity=oldAwaiting;Paint();ApplyLayout(true);if(oldVisible)window.Show();else window.Hide();
      }
    }
  }
}
