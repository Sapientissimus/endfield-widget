using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;

namespace EndfieldWidget {
  sealed class CalendarSourceCommand:ICommand {
    readonly Action<CalendarEntry> open;
    public CalendarSourceCommand(Action<CalendarEntry> callback){open=callback;}
    public bool CanExecute(object parameter){var entry=parameter as CalendarEntry;return entry!=null && CalendarData.Official(entry.Source);}
    public void Execute(object parameter){if(CanExecute(parameter))open((CalendarEntry)parameter);}
    public event EventHandler CanExecuteChanged {add{CommandManager.RequerySuggested+=value;} remove{CommandManager.RequerySuggested-=value;}}
  }
  sealed class CalendarEntryCommand:ICommand {
    readonly ICommand source;
    readonly CalendarEntry entry;
    public CalendarEntryCommand(ICommand sourceCommand,CalendarEntry target){source=sourceCommand;entry=target;}
    public bool CanExecute(object parameter){return source.CanExecute(entry);}
    public void Execute(object parameter){if(CanExecute(null))source.Execute(entry);}
    // Each immutable card owns its target. Initial availability therefore does
    // not depend on the order in which WPF applies parameter/command bindings.
    public event EventHandler CanExecuteChanged {add{} remove{}}
  }
  public sealed class CalendarCardModel:INotifyPropertyChanged {
    internal readonly CalendarEntry Entry;
    public string Id {get{return Entry.Id;}}
    public string Name {get{return Entry.Name??Entry.Title;}}
    public string Category {get{return CalendarData.Label(Entry.Kind);}}
    public Brush CategoryBrush {get{return CalendarPresentation.BrushFor(Entry.Kind);}}
    public string Icon {get{return CalendarPresentation.Icon(Entry.Kind);}}
    public string TimeText {get;private set;}
    public string Status {get;private set;}
    public string DateHeading {get;private set;}
    public string PeriodText {get;private set;}
    public string PeriodLabel {get{return Entry.PeriodStart.HasValue && Entry.PeriodEnd.HasValue?L.T("PERIOD"):L.T(CalendarData.IsEnd(Entry.Kind)?"END":"START");}}
    readonly Func<CalendarEntry,CalendarDetailState> detailFactory;
    CalendarDetailState details;
    public CalendarDetailState Details {get{return details??(details=detailFactory(Entry));}}
    public string Source {get{return Entry.Source;}}
    public ImageSource Artwork {get;private set;}
    public string ArtworkCaption {get;private set;}
    internal string ArtworkRequestedUrl;
    internal void SetArtwork(ImageSource value,string caption) {
      if(ArtworkCaption!=caption){ArtworkCaption=caption;Changed("ArtworkCaption");}
      if(Artwork!=value){Artwork=value;Changed("Artwork");}
    }
    public string AccessibleName {get{return Category+" · "+Name+" · "+TimeText+L.T(" · Open official announcement");}}
    public object SourceTarget {get{return Entry;}}
    public ICommand OpenSource {get;private set;}
    public event PropertyChangedEventHandler PropertyChanged;
    internal CalendarCardModel(CalendarEntry entry,DateTimeOffset now,ICommand open):this(entry,now,open,e=>new CalendarDetailState(e,null)){}
    internal CalendarCardModel(CalendarEntry entry,DateTimeOffset now,ICommand open,Func<CalendarEntry,CalendarDetailState> factory){Entry=entry;detailFactory=factory;OpenSource=new CalendarEntryCommand(open,entry);Refresh(now);}
    internal void Refresh(DateTimeOffset now) {
      string time=CalendarPresentation.Time(Entry),status=CalendarPresentation.Status(Entry,now);
      string period=Entry.PeriodStart.HasValue && Entry.PeriodEnd.HasValue?Clock.Date(Entry.PeriodStart.Value)+L.T(" at ")+Clock.Time(Entry.PeriodStart.Value)+" — "+Clock.Date(Entry.PeriodEnd.Value)+L.T(" at ")+Clock.Time(Entry.PeriodEnd.Value):Clock.Date(Entry.At)+L.T(" at ")+Clock.Time(Entry.At);
      if(PeriodText!=period){PeriodText=period;Changed("PeriodText");}
      DateTime date=Clock.Local(Entry.At<now && Entry.End.HasValue && Entry.End>now?now:Entry.At).Date;
      string heading=CalendarPresentation.Heading(date,Clock.Local(now).Date);
      if(TimeText!=time){TimeText=time;Changed("TimeText");Changed("AccessibleName");}
      if(Status!=status){Status=status;Changed("Status");}if(DateHeading!=heading){DateHeading=heading;Changed("DateHeading");}
    }
    void Changed(string name){var handler=PropertyChanged;if(handler!=null)handler(this,new PropertyChangedEventArgs(name));}
  }
  static class CalendarPresentation {
    static readonly Brush EventBrush=Tone(255,203,87),MaintenanceBrush=Tone(207,171,250),DebutBrush=Tone(148,218,241),DeadlineBrush=Tone(248,166,168);
    static Brush Tone(byte red,byte green,byte blue){var brush=new SolidColorBrush(Color.FromRgb(red,green,blue));brush.Freeze();return brush;}
    public static Brush BrushFor(CalendarKind kind) {
      if(System.Windows.SystemParameters.HighContrast)return System.Windows.SystemColors.WindowTextBrush;
      switch(kind) {
        case CalendarKind.Maintenance:return MaintenanceBrush;
        case CalendarKind.Debut:
        case CalendarKind.RerunStart:
        case CalendarKind.WeaponStart:return DebutBrush;
        case CalendarKind.BannerEnd:
        case CalendarKind.RerunEnd:
        case CalendarKind.WeaponEnd:return DeadlineBrush;
        default:return EventBrush;
      }
    }
    public static string Heading(DateTime day,DateTime today) {
      var culture=L.Culture;string date=L.DateCaption(day,false);
      if(day.Date==today.Date)return L.T("Today · ")+date;
      if(day.Date==today.Date.AddDays(1))return L.T("Tomorrow · ")+date;
      string title=day.ToString("dddd",culture)+", "+date;return char.ToUpper(title[0],culture)+title.Substring(1);
    }
    public static string Time(CalendarEntry entry) {
      var end=entry.RangeEnd;
      if(!end.HasValue)return Clock.Time(entry.At);
      if(Clock.Local(entry.At).Date==Clock.Local(end.Value).Date)return Clock.Time(entry.At)+"–"+Clock.Time(end.Value);
      return Clock.Date(entry.At)+" · "+Clock.Time(entry.At)+" → "+Clock.Date(end.Value)+" · "+Clock.Time(end.Value);
    }
    public static string Status(CalendarEntry entry,DateTimeOffset now) {
      bool deadline=CalendarData.IsEnd(entry.Kind);
      var end=entry.RangeEnd;
      bool missingEnd=(entry.Kind==CalendarKind.Debut || entry.Kind==CalendarKind.EventStart || entry.Kind==CalendarKind.RerunStart || entry.Kind==CalendarKind.WeaponStart) && !end.HasValue;
      if(entry.At>now)return L.T(deadline?"Ends in ":"Starts in ")+Clock.After(entry.At-now)+(missingEnd?" · "+L.T("End date unknown"):"");
      if(end.HasValue && end>now)return L.T("Ongoing · until ")+(Clock.Local(end.Value).Date==Clock.Local(now).Date?Clock.Time(end.Value):Clock.Date(end.Value)+" · "+Clock.Time(end.Value));
      if(entry.Kind==CalendarKind.Debut && missingEnd)return L.T("Debut has started")+" · "+L.T("End date unknown");
      if(missingEnd)return L.T("End date unknown");
      if(entry.Kind==CalendarKind.Milestone)return L.T("Started");
      return L.T("Ended");
    }
    public static string Icon(CalendarKind kind) {
      switch(kind) {
        case CalendarKind.Maintenance:return "M7,0 L14,7 L7,14 L0,7 Z M7,4 L7,8 M7,10 L7.01,10";
        case CalendarKind.Debut:return "M7,0 L9,5 L14,5 L10,8 L12,14 L7,10 L2,14 L4,8 L0,5 L5,5 Z";
        case CalendarKind.BannerEnd:return "M1,1 L13,1 L13,13 L1,13 Z M4,4 L10,10 M10,4 L4,10";
        case CalendarKind.EventStart:return "M1,1 L13,7 L1,13 Z";
        case CalendarKind.Milestone:return "M3,13 L3,1 L12,1 L9,4 L12,7 L3,7";
        case CalendarKind.RerunStart:
        case CalendarKind.RerunEnd:return "M12,4 A5,5 0 1 0 12,10 M12,0 L12,5 L7,5";
        case CalendarKind.WeaponStart:
        case CalendarKind.WeaponEnd:return "M2,12 L12,2 M7,2 L12,2 L12,7 M1,7 L7,13";
        default:return "M1,1 L13,1 L13,13 L1,13 Z M4,7 L6,9 L10,5";
      }
    }
  }
}
