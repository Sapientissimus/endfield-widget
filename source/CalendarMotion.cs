using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace EndfieldWidget {
  static class CalendarMotion {
    internal static bool? Override;
    public static bool Enabled {get{return Override??SystemParameters.ClientAreaAnimation;}}
    public static void Play(FrameworkElement target,double direction) {
      if(target==null)return;Stop(target);if(!Enabled || !target.IsLoaded)return;
      var shift=new TranslateTransform();target.RenderTransform=shift;
      var duration=TimeSpan.FromMilliseconds(145);var ease=new QuadraticEase{EasingMode=EasingMode.EaseOut};
      shift.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(direction*10,0,duration){EasingFunction=ease,FillBehavior=FillBehavior.Stop},HandoffBehavior.SnapshotAndReplace);
      var fade=new DoubleAnimation(.65,1,duration){FillBehavior=FillBehavior.Stop};fade.Completed+=(s,e)=>{if(ReferenceEquals(target.RenderTransform,shift))Stop(target);};
      target.BeginAnimation(UIElement.OpacityProperty,fade,HandoffBehavior.SnapshotAndReplace);
    }
    public static void Stop(FrameworkElement target){if(target==null)return;target.BeginAnimation(UIElement.OpacityProperty,null);var shift=target.RenderTransform as TranslateTransform;if(shift!=null)shift.BeginAnimation(TranslateTransform.XProperty,null);target.RenderTransform=Transform.Identity;}
  }
  public sealed class CalendarReveal:Decorator {
    public static readonly DependencyProperty IsOpenProperty=DependencyProperty.Register("IsOpen",typeof(bool),typeof(CalendarReveal),new PropertyMetadata(false,Changed));
    static readonly DependencyProperty ProgressProperty=DependencyProperty.Register("Progress",typeof(double),typeof(CalendarReveal),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsMeasure));
    public bool IsOpen {get{return (bool)GetValue(IsOpenProperty);}set{SetValue(IsOpenProperty,value);}}
    double Progress {get{return (double)GetValue(ProgressProperty);}}
    int generation;
    public CalendarReveal(){ClipToBounds=true;IsHitTestVisible=false;KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.None);Unloaded+=(s,e)=>Reset();DataContextChanged+=(s,e)=>Reset();}
    void Reset(){generation++;BeginAnimation(ProgressProperty,null);SetValue(ProgressProperty,IsOpen?1.0:0.0);}
    static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args) {
      var view=(CalendarReveal)value;int version=++view.generation;double from=view.Progress,to=(bool)args.NewValue?1:0;view.BeginAnimation(ProgressProperty,null);view.SetValue(ProgressProperty,to);
      if(view.IsLoaded && CalendarMotion.Enabled){var animation=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(to>from?165:125)){EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};animation.Completed+=(s,e)=>{if(view.generation==version)view.BeginAnimation(ProgressProperty,null);};view.BeginAnimation(ProgressProperty,animation,HandoffBehavior.SnapshotAndReplace);}
      view.IsHitTestVisible=(bool)args.NewValue;
      KeyboardNavigation.SetTabNavigation(view,(bool)args.NewValue?KeyboardNavigationMode.Continue:KeyboardNavigationMode.None);
    }
    protected override Size MeasureOverride(Size available) {
      if(Child==null || Progress<=0)return new Size();Child.Measure(new Size(available.Width,double.PositiveInfinity));return new Size(Child.DesiredSize.Width,Child.DesiredSize.Height*Math.Max(0,Math.Min(1,Progress)));
    }
    protected override Size ArrangeOverride(Size finalSize){if(Child!=null)Child.Arrange(new Rect(0,0,finalSize.Width,Math.Max(finalSize.Height,Child.DesiredSize.Height)));return finalSize;}
  }
}
