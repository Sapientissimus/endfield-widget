using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace EndfieldWidget {
  sealed class CalendarDetailAction:ICommand {
    readonly Action action;
    public CalendarDetailAction(Action callback){action=callback;}
    public bool CanExecute(object parameter){return true;}
    public void Execute(object parameter){action();}
    public event EventHandler CanExecuteChanged{add{} remove{}}
  }
  public sealed class CalendarDetailState:INotifyPropertyChanged {
    internal readonly CalendarEntry Entry;
    readonly Action<CalendarDetailState,bool> request;
    bool expanded;
    internal bool Busy;
    CalendarDetailRecord record;
    string notice="";
    bool failed;
    public bool IsExpanded {get{return expanded;}set{if(expanded==value)return;expanded=value;Changed();if(value && request!=null)request(this,false);}}
    public string Label {get{return expanded?L.T("Collapse"):L.T("Details");}}
    public string Description {get{return record==null?"":record.Description;}}
    public string Participation {get{return record==null?"":record.Participation;}}
    public string Notice {get{return notice;}}
    public Visibility DescriptionVisibility {get{return string.IsNullOrWhiteSpace(Description)?Visibility.Collapsed:Visibility.Visible;}}
    public Visibility ParticipationVisibility {get{return string.IsNullOrWhiteSpace(Participation)?Visibility.Collapsed:Visibility.Visible;}}
    public Visibility NoticeVisibility {get{return notice.Length==0?Visibility.Collapsed:Visibility.Visible;}}
    public Visibility RetryVisibility {get{return failed && !Busy?Visibility.Visible:Visibility.Collapsed;}}
    public ICommand Retry {get;private set;}
    internal CalendarDetailState(CalendarEntry entry,Action<CalendarDetailState,bool> callback){Entry=entry;request=callback;Retry=new CalendarDetailAction(()=>{if(request!=null)request(this,true);});}
    internal void Loading(){Busy=true;failed=false;notice=L.T("Loading…");Changed();}
    internal void Apply(CalendarDetailRecord value){record=value;Busy=false;failed=false;notice=string.IsNullOrWhiteSpace(Description) && string.IsNullOrWhiteSpace(Participation)?L.T("No summary has been published. Read the full announcement."):"";Changed();}
    internal void Failure(){Busy=false;failed=true;notice=record==null?L.T("Could not load the description."):L.T("Showing the saved description.");Changed();}
    void Changed(){var handler=PropertyChanged;if(handler!=null)handler(this,new PropertyChangedEventArgs(""));}
    public event PropertyChangedEventHandler PropertyChanged;
  }
  partial class Widget {
    readonly Dictionary<string,CalendarDetailState> calendarDetailStates=new Dictionary<string,CalendarDetailState>();
    Dictionary<string,CalendarDetailRecord> calendarDetailCache;
    int calendarDetailEpoch;
    internal Func<CalendarEntry,Task<CalendarDetailRecord>> calendarDetailFetchOverride;
    CalendarDetailState CalendarDetailFor(CalendarEntry entry) {
      string key=CalendarDetailsData.Key(entry);CalendarDetailState state;
      if(calendarDetailStates.TryGetValue(key,out state))return state;
      if(calendarDetailStates.Count>=256){string evict=calendarDetailStates.FirstOrDefault(p=>!p.Value.Busy && !p.Value.IsExpanded).Key;if(evict!=null)calendarDetailStates.Remove(evict);}
      state=new CalendarDetailState(entry,RequestCalendarDetail);calendarDetailStates[key]=state;return state;
    }
    async void RequestCalendarDetail(CalendarDetailState state,bool force) {
      if(state.Busy || calendarDialog==null)return;
      int epoch=calendarDetailEpoch,recoveryEpoch=recovery.Epoch;string key=CalendarDetailsData.Key(state.Entry);
      if(calendarDetailCache==null)calendarDetailCache=CalendarDetailsStore.Load();CalendarDetailRecord cached;
      if(calendarDetailCache.TryGetValue(key,out cached)){state.Apply(cached);if(!force && cached.Checked<=DateTimeOffset.UtcNow && DateTimeOffset.UtcNow-cached.Checked<TimeSpan.FromHours(6))return;}
      state.Loading();
      try {
        var result=await (calendarDetailFetchOverride==null?banners.FetchCalendarDetail(state.Entry):calendarDetailFetchOverride(state.Entry));
        if(calendarDialog==null || epoch!=calendarDetailEpoch)return;
        if(!CanApplyResponse(recoveryEpoch)){state.Failure();return;}
        if(result==null || result.Key!=key || result.Source!=state.Entry.Source)throw new FormatException("Mismatched calendar detail.");
        state.Apply(result);calendarDetailCache[key]=result;
        try{await Task.Run(()=>CalendarDetailsStore.Save(result));}catch{}
      }catch{if(calendarDialog!=null && epoch==calendarDetailEpoch)state.Failure();}
    }
    void ReleaseCalendarDetails(){calendarDetailEpoch++;calendarDetailStates.Clear();}
    void AnimateCalendarChange(double direction) {if(calendarDialog==null)return;CalendarMotion.Play(CalendarFind<System.Windows.Controls.Grid>("CalendarMonthPane"),direction);CalendarMotion.Play(CalendarFind<System.Windows.Controls.Grid>("CalendarAgendaPane"),direction);}
  }
}
