using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EndfieldWidget {
  public sealed class CalendarReveal:Decorator {
    public static readonly DependencyProperty IsOpenProperty=DependencyProperty.Register("IsOpen",typeof(bool),typeof(CalendarReveal),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsMeasure,Changed));
    public bool IsOpen {get{return (bool)GetValue(IsOpenProperty);}set{SetValue(IsOpenProperty,value);}}
    public CalendarReveal(){ClipToBounds=true;IsHitTestVisible=false;KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.None);}
    static void Changed(DependencyObject value,DependencyPropertyChangedEventArgs args) {
      var view=(CalendarReveal)value;bool open=(bool)args.NewValue;
      view.IsHitTestVisible=open;KeyboardNavigation.SetTabNavigation(view,open?KeyboardNavigationMode.Continue:KeyboardNavigationMode.None);
    }
    protected override Size MeasureOverride(Size available) {
      if(Child==null || !IsOpen)return new Size();Child.Measure(new Size(available.Width,double.PositiveInfinity));return Child.DesiredSize;
    }
    protected override Size ArrangeOverride(Size finalSize){if(Child!=null)Child.Arrange(new Rect(finalSize));return finalSize;}
  }
}
