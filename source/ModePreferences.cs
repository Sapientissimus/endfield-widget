using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace EndfieldWidget {
  public sealed class ModePreferences {
    public string MonitorId {get;set;}
    public double MonitorX {get;set;}
    public double MonitorY {get;set;}
    public bool HasPosition {get;set;}
    public double Left {get;set;}
    public double Top {get;set;}
    public string PositionAnchor {get;set;}
    public int EdgeMarginX {get;set;}
    public int EdgeMarginY {get;set;}
    public bool ShowPlayButton {get;set;}
    public bool ShowClock {get;set;}
    public bool ShowNextDebut {get;set;}
    public bool ShowUpdates {get;set;}
    public static ModePreferences Capture(Config c) {
      return new ModePreferences{MonitorId=c.MonitorId,MonitorX=c.MonitorX,MonitorY=c.MonitorY,HasPosition=c.HasPosition,Left=c.Left,Top=c.Top,PositionAnchor=c.PositionAnchor,EdgeMarginX=c.EdgeMarginX,EdgeMarginY=c.EdgeMarginY,ShowPlayButton=c.ShowPlayButton,ShowClock=c.ShowClock,ShowNextDebut=c.ShowNextDebut,ShowUpdates=c.ShowUpdates};
    }
    public void Apply(Config c) {
      c.MonitorId=MonitorId;c.MonitorX=MonitorX;c.MonitorY=MonitorY;c.HasPosition=HasPosition;c.Left=Left;c.Top=Top;c.PositionAnchor=PositionAnchor;c.EdgeMarginX=EdgeMarginX;c.EdgeMarginY=EdgeMarginY;
      c.ShowPlayButton=ShowPlayButton;c.ShowClock=ShowClock;c.ShowNextDebut=ShowNextDebut;c.ShowUpdates=ShowUpdates;
    }
  }
  public partial class Config {
    public Dictionary<string,ModePreferences> ModeProfiles {get;set;}
    public string CurrentMode(){return MicroMode?"micro":Compact?"compact":"full";}
    public void EnsureModeProfiles() {
      if(ModeProfiles==null)ModeProfiles=new Dictionary<string,ModePreferences>();
      foreach(string mode in new[]{"full","compact","micro"})if(!ModeProfiles.ContainsKey(mode) || ModeProfiles[mode]==null)ModeProfiles[mode]=ModePreferences.Capture(this);
    }
    public void RememberMode(){EnsureModeProfiles();ModeProfiles[CurrentMode()]=ModePreferences.Capture(this);}
    public void ActivateMode(string mode){EnsureModeProfiles();ModeProfiles[mode].Apply(this);MicroMode=mode=="micro";Compact=mode=="compact";NormalizeStudio();}
    public ModePreferences Mode(string mode){EnsureModeProfiles();return ModeProfiles[mode];}
  }
  public sealed class ModeBlocks {
    public bool ShowPlayButton {get;set;}
    public bool ShowClock {get;set;}
    public bool ShowNextDebut {get;set;}
    public bool ShowUpdates {get;set;}
  }
  public sealed class AppearanceState {
    public double Scale {get;set;}
    public double CompactScale {get;set;}
    public double MicroScale {get;set;}
    public double BackgroundOpacity {get;set;}
    public double PortraitOpacity {get;set;}
    public bool ShowWatermark {get;set;}
    public bool Compact {get;set;}
    public bool MicroMode {get;set;}
    public double TextScale {get;set;}
    public string DesignPreset {get;set;}
    public string SanityStyle {get;set;}
    public bool FadeWhenIdle {get;set;}
    public double IdleOpacity {get;set;}
    public Dictionary<string,PortraitFrame> PortraitFrames {get;set;}
    public Dictionary<string,ModeBlocks> Blocks {get;set;}
    static Dictionary<string,PortraitFrame> CopyFrames(Dictionary<string,PortraitFrame> frames) {
      if(frames==null)return null;
      var copy=new Dictionary<string,PortraitFrame>(frames.Comparer);
      foreach(var pair in frames)copy[pair.Key]=pair.Value==null?null:new PortraitFrame{Zoom=pair.Value.Zoom,X=pair.Value.X,Y=pair.Value.Y};
      return copy;
    }
    public static AppearanceState Capture(Config c) {
      c.RememberMode();var result=new AppearanceState{Scale=c.Scale,CompactScale=c.CompactScale,MicroScale=c.MicroScale,BackgroundOpacity=c.BackgroundOpacity,PortraitOpacity=c.PortraitOpacity,ShowWatermark=c.ShowWatermark,Compact=c.Compact,MicroMode=c.MicroMode,TextScale=c.TextScale,DesignPreset=c.DesignPreset,SanityStyle=c.SanityStyle,FadeWhenIdle=c.FadeWhenIdle,IdleOpacity=c.IdleOpacity,PortraitFrames=CopyFrames(c.PortraitFrames)};
      result.Blocks=new Dictionary<string,ModeBlocks>();foreach(var pair in c.ModeProfiles)result.Blocks[pair.Key]=new ModeBlocks{ShowPlayButton=pair.Value.ShowPlayButton,ShowClock=pair.Value.ShowClock,ShowNextDebut=pair.Value.ShowNextDebut,ShowUpdates=pair.Value.ShowUpdates};
      return result;
    }
    public void Apply(Config c) {
      c.EnsureModeProfiles();c.RememberMode();
      c.Scale=Scale;c.CompactScale=CompactScale;c.MicroScale=MicroScale;c.BackgroundOpacity=BackgroundOpacity;c.PortraitOpacity=PortraitOpacity;c.ShowWatermark=ShowWatermark;c.Compact=Compact;c.MicroMode=MicroMode;c.TextScale=TextScale;c.DesignPreset=DesignPreset;c.SanityStyle=SanityStyle;c.FadeWhenIdle=FadeWhenIdle;c.IdleOpacity=IdleOpacity;c.PortraitFrames=CopyFrames(PortraitFrames);
      if(Blocks!=null)foreach(var pair in Blocks)if(c.ModeProfiles.ContainsKey(pair.Key)){var mode=c.ModeProfiles[pair.Key];mode.ShowPlayButton=pair.Value.ShowPlayButton;mode.ShowClock=pair.Value.ShowClock;mode.ShowNextDebut=pair.Value.ShowNextDebut;mode.ShowUpdates=pair.Value.ShowUpdates;}
      c.ActivateMode(c.CurrentMode());c.NormalizeScales();c.NormalizePreferences();
    }
  }
  sealed class AppearanceHistory {
    public string Current,Previous;
    string lastChanged="";DateTimeOffset lastChange;bool gesture,observedGesture;
    public bool CanUndo {get{return Previous!=null;}}
    public bool InGesture {get{return gesture;}}
    public void Reset(AppearanceState state){Current=Json.Encode(state);Previous=null;lastChanged="";gesture=observedGesture=false;}
    public void BeginGesture(){gesture=true;observedGesture=false;}
    public void EndGesture(){gesture=false;lastChanged="";}
    public void Observe(AppearanceState state,DateTimeOffset now) {
      string value=Json.Encode(state);if(Current==null){Current=value;return;}if(value==Current)return;
      var old=Json.Decode(Current) as Dictionary<string,object>;var next=Json.Decode(value) as Dictionary<string,object>;var names=new List<string>();
      foreach(var pair in next)if(!old.ContainsKey(pair.Key) || Json.Encode(old[pair.Key])!=Json.Encode(pair.Value))names.Add(pair.Key);
      string changed=string.Join("|",names);
      if(gesture?!observedGesture:changed!=lastChanged || now-lastChange>TimeSpan.FromMilliseconds(750))Previous=Current;
      observedGesture=true;lastChanged=changed;lastChange=now;Current=value;
    }
    public AppearanceState Take(){if(Previous==null)return null;var state=new JavaScriptSerializer().Deserialize<AppearanceState>(Previous);Reset(state);return state;}
  }
}
