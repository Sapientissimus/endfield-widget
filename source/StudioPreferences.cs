using System;
using System.Collections.Generic;

namespace EndfieldWidget {
  public sealed class PortraitFrame {
    public PortraitFrame(){Zoom=1;}
    public double Zoom {get;set;}
    public double X {get;set;}
    public double Y {get;set;}
    public void Normalize(){Zoom=Placement.Finite(Zoom)?Placement.Clamp(Zoom,.6,1.6):1;X=Placement.Finite(X)?Placement.Clamp(X,-100,100):0;Y=Placement.Finite(Y)?Placement.Clamp(Y,-100,100):0;}
  }
  public partial class Config {
    public bool MicroMode {get;set;}
    public double MicroScale {get;set;}
    public double TextScale {get;set;}
    public string SanityStyle {get;set;}
    public string DesignPreset {get;set;}
    public bool FadeWhenIdle {get;set;}
    public double IdleOpacity {get;set;}
    public bool AnimateTransitions {get;set;}
    public bool ShowSessionTime {get;set;}
    public string PositionAnchor {get;set;}
    public int EdgeMarginX {get;set;}
    public int EdgeMarginY {get;set;}
    public string HotkeyToggle {get;set;}
    public string HotkeyMenu {get;set;}
    public string HotkeyPlay {get;set;}
    public string HotkeyMode {get;set;}
    public string HotkeyRefresh {get;set;}
    public Dictionary<string,PortraitFrame> PortraitFrames {get;set;}
    void StudioDefaults(){MicroScale=TextScale=1;SanityStyle="bar";DesignPreset="soft";IdleOpacity=.75;AnimateTransitions=true;ShowSessionTime=true;PositionAnchor="free";EdgeMarginX=EdgeMarginY=12;HotkeyToggle="Ctrl+Alt+H";HotkeyMenu="Ctrl+Alt+E";HotkeyPlay="Ctrl+Alt+G";HotkeyMode="Ctrl+Alt+M";HotkeyRefresh="Ctrl+Alt+R";PortraitFrames=new Dictionary<string,PortraitFrame>();}
    public void NormalizeStudio() {
      MicroScale=Placement.Finite(MicroScale) && MicroScale>0?Placement.Clamp(MicroScale,.75,1.3):1;
      TextScale=Placement.Finite(TextScale) && TextScale>0?Placement.Clamp(TextScale,.85,1.2):1;
      IdleOpacity=Placement.Finite(IdleOpacity)?Placement.Clamp(IdleOpacity,.3,1):.75;
      if(SanityStyle!="ring" && SanityStyle!="numbers")SanityStyle="bar";
      if(DesignPreset!="strict" && DesignPreset!="minimal")DesignPreset="soft";
      if(PositionAnchor!="tl" && PositionAnchor!="tr" && PositionAnchor!="bl" && PositionAnchor!="br")PositionAnchor="free";
      EdgeMarginX=Math.Max(0,Math.Min(300,EdgeMarginX));EdgeMarginY=Math.Max(0,Math.Min(300,EdgeMarginY));
      if(MicroMode)Compact=false;
      if(PortraitFrames==null)PortraitFrames=new Dictionary<string,PortraitFrame>();
      foreach(var frame in PortraitFrames.Values)if(frame!=null)frame.Normalize();
      HotkeyToggle=HotkeyToggle??"Ctrl+Alt+H";HotkeyMenu=HotkeyMenu??"Ctrl+Alt+E";HotkeyPlay=HotkeyPlay??"Ctrl+Alt+G";HotkeyMode=HotkeyMode??"Ctrl+Alt+M";HotkeyRefresh=HotkeyRefresh??"Ctrl+Alt+R";
    }
    public PortraitFrame Frame(string key,bool compact) {
      key=BannerClient.ArtKey(key??"");string suffix="|"+(compact?"compact":"full"),id=key+suffix;PortraitFrame frame;
      if(!PortraitFrames.TryGetValue(id,out frame) || frame==null) {
        if(!PortraitFrames.TryGetValue(BannerClient.OfficialArtKey(key)+suffix,out frame) || frame==null){frame=new PortraitFrame();PortraitFrames[id]=frame;}
      }
      frame.Normalize();return frame;
    }
  }
}
