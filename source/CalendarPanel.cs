using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndfieldWidget {
  public sealed class CalendarPanel:Border {
    public static readonly DependencyProperty CutLengthProperty=DependencyProperty.Register("CutLength",typeof(double),typeof(CalendarPanel),new FrameworkPropertyMetadata(7.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public double CutLength {get{return (double)GetValue(CutLengthProperty);}set{SetValue(CutLengthProperty,value);}}
    Geometry outline;
    Size outlineSize;
    double outlineCut=-1,outlineStroke=-1;
    Brush previousBrush;
    Pen stroke;
    protected override void OnRender(DrawingContext drawing) {
      double width=ActualWidth,height=ActualHeight;if(width<=0 || height<=0)return;
      double thickness=Math.Max(Math.Max(BorderThickness.Left,BorderThickness.Right),Math.Max(BorderThickness.Top,BorderThickness.Bottom));
      double cut=Math.Min(Math.Max(0,CutLength),Math.Min(width,height)/3),inset=thickness/2;
      if(outline==null || outlineSize.Width!=width || outlineSize.Height!=height || outlineCut!=cut || outlineStroke!=thickness) {
        var shape=new StreamGeometry();
        using(var path=shape.Open()) {
          path.BeginFigure(new Point(inset,inset),true,true);
          path.LineTo(new Point(width-cut-inset,inset),true,false);path.LineTo(new Point(width-inset,cut+inset),true,false);
          path.LineTo(new Point(width-inset,height-inset),true,false);path.LineTo(new Point(cut+inset,height-inset),true,false);
          path.LineTo(new Point(inset,height-cut-inset),true,false);
        }
        shape.Freeze();outline=shape;outlineSize=new Size(width,height);outlineCut=cut;outlineStroke=thickness;stroke=null;
      }
      if(stroke==null || !ReferenceEquals(previousBrush,BorderBrush)){previousBrush=BorderBrush;stroke=BorderBrush==null || thickness==0?null:new Pen(BorderBrush,thickness);if(stroke!=null && stroke.CanFreeze)stroke.Freeze();}
      drawing.DrawGeometry(Background,stroke,outline);
    }
  }
}
