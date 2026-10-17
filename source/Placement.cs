using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  sealed class DisplayArea {
    public string Id;
    public Rect Work,Bounds;
    public bool Primary;
  }
  static class Placement {
    public static bool Finite(double value){return !double.IsNaN(value) && !double.IsInfinity(value);}
    public static double Clamp(double value,double low,double high){return Math.Max(low,Math.Min(high,value));}
    public static List<DisplayArea> Displays(){return Forms.Screen.AllScreens.Select(s=>new DisplayArea {Id=s.DeviceName,Primary=s.Primary,Work=new Rect(s.WorkingArea.X,s.WorkingArea.Y,s.WorkingArea.Width,s.WorkingArea.Height),Bounds=new Rect(s.Bounds.X,s.Bounds.Y,s.Bounds.Width,s.Bounds.Height)}).ToList();}
    public static DisplayArea Preferred(Config config,IList<DisplayArea> screens){return screens.FirstOrDefault(s=>s.Id==config.MonitorId)??screens.FirstOrDefault(s=>s.Primary)??screens[0];}
    public static DisplayArea At(Rect rectangle,IList<DisplayArea> screens) {
      return screens.OrderByDescending(s=>{Rect r=Rect.Intersect(s.Work,rectangle);return r.IsEmpty?0:r.Width*r.Height;})
        .ThenBy(s=>Math.Pow(s.Work.X+s.Work.Width/2-rectangle.X-rectangle.Width/2,2)+Math.Pow(s.Work.Y+s.Work.Height/2-rectangle.Y-rectangle.Height/2,2)).First();
    }
    public static Point Restore(Config config,DisplayArea screen,Size size) {
      double x=Finite(config.MonitorX)?Clamp(config.MonitorX,0,1):1,y=Finite(config.MonitorY)?Clamp(config.MonitorY,0,1):0;
      if(config.PositionAnchor=="tl" || config.PositionAnchor=="bl")x=0;if(config.PositionAnchor=="tr" || config.PositionAnchor=="br")x=1;
      if(config.PositionAnchor=="tl" || config.PositionAnchor=="tr")y=0;if(config.PositionAnchor=="bl" || config.PositionAnchor=="br")y=1;
      double mx=Clamp(config.EdgeMarginX,0,Math.Max(0,(screen.Work.Width-size.Width)/2)),my=Clamp(config.EdgeMarginY,0,Math.Max(0,(screen.Work.Height-size.Height)/2));
      return new Point(screen.Work.Left+mx+Math.Max(0,screen.Work.Width-size.Width-2*mx)*x,screen.Work.Top+my+Math.Max(0,screen.Work.Height-size.Height-2*my)*y);
    }
    public static void Remember(Config config,DisplayArea screen,Rect rectangle) {
      config.MonitorId=screen.Id;
      config.MonitorX=Clamp((rectangle.Left-screen.Work.Left-config.EdgeMarginX)/Math.Max(1,screen.Work.Width-rectangle.Width-2*config.EdgeMarginX),0,1);
      config.MonitorY=Clamp((rectangle.Top-screen.Work.Top-config.EdgeMarginY)/Math.Max(1,screen.Work.Height-rectangle.Height-2*config.EdgeMarginY),0,1);
      config.HasPosition=true;
    }
    public static Point Snap(Rect rectangle,DisplayArea screen) {
      return Snap(rectangle,screen,12,12);
    }
    public static Point Snap(Rect rectangle,DisplayArea screen,double mx,double my) {
      double x=rectangle.Left,y=rectangle.Top;
      double right=screen.Work.Right-rectangle.Width-mx,bottom=screen.Work.Bottom-rectangle.Height-my;
      if(Math.Abs(x-(screen.Work.Left+mx))<=18)x=screen.Work.Left+mx;
      else if(Math.Abs(x-right)<=18)x=right;
      if(Math.Abs(y-(screen.Work.Top+my))<=18)y=screen.Work.Top+my;
      else if(Math.Abs(y-bottom)<=18)y=bottom;
      return new Point(x,y);
    }
  }
}
