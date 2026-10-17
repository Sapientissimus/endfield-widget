using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    readonly Dictionary<TextBlock,double> originalFonts=new Dictionary<TextBlock,double>();
    readonly Dictionary<Border,CornerRadius> originalCorners=new Dictionary<Border,CornerRadius>();
    readonly Dictionary<Border,Thickness> originalBorders=new Dictionary<Border,Thickness>();
    Action syncCropSettings;
    DispatcherTimer modeTransitionTimer;
    int transitionVersion,hoverVersion;
    void CaptureAppearance(DependencyObject node) {
      var text=node as TextBlock;if(text!=null && !originalFonts.ContainsKey(text) && text.Name!="ArtWatermark")originalFonts[text]=text.FontSize;
      var border=node as Border;if(border!=null && !originalCorners.ContainsKey(border)){originalCorners[border]=border.CornerRadius;originalBorders[border]=border.BorderThickness;}
      foreach(object child in LogicalTreeHelper.GetChildren(node)){var dependency=child as DependencyObject;if(dependency!=null)CaptureAppearance(dependency);}
    }
    void InitializeStudio() {
      config.NormalizeStudio();CaptureAppearance(window);ApplyDesignPreset();ApplyTextScale();
      window.MouseEnter+=(s,e)=>ApplyHoverOpacity();window.MouseLeave+=(s,e)=>ApplyHoverOpacity();
      window.IsKeyboardFocusWithinChanged+=(s,e)=>ApplyHoverOpacity();
      Find<Button>("MicroSettingsButton").Click+=(s,e)=>Settings();
      Find<Button>("FullCalendarButton").Click+=(s,e)=>ShowCalendar();Find<Button>("CompactCalendarButton").Click+=(s,e)=>ShowCalendar();
      ApplyHoverOpacity();
    }
    bool AnimateSurface(){return config.AnimateTransitions && SystemParameters.ClientAreaAnimation && (!preview && !Storage.Isolated || diagnosticView);}
    void ApplyHoverOpacity() {
      double opacity=config.FadeWhenIdle && !window.IsMouseOver && !window.IsKeyboardFocusWithin?config.IdleOpacity:1;
      int version=++hoverVersion;double from=window.Opacity;window.BeginAnimation(UIElement.OpacityProperty,null);window.Opacity=opacity;
      if(AnimateSurface() && window.IsVisible && Math.Abs(from-opacity)>.001){var fade=new DoubleAnimation(from,opacity,TimeSpan.FromMilliseconds(140)){FillBehavior=FillBehavior.Stop};fade.Completed+=(s,e)=>{if(version==hoverVersion)window.BeginAnimation(UIElement.OpacityProperty,null);};window.BeginAnimation(UIElement.OpacityProperty,fade);}
    }
    static Color BlendColor(Color a,Color b,double amount){return Color.FromRgb((byte)(a.R+(b.R-a.R)*amount),(byte)(a.G+(b.G-a.G)*amount),(byte)(a.B+(b.B-a.B)*amount));}
    void ApplyDesignPreset() {
      if(currentTheme==null)return;currentTheme.Apply(window.Resources);
      foreach(var item in originalCorners) {
        double limit=config.DesignPreset=="strict"?4:config.DesignPreset=="minimal"?9:100;
        var c=item.Value;item.Key.CornerRadius=new CornerRadius(Math.Min(c.TopLeft,limit),Math.Min(c.TopRight,limit),Math.Min(c.BottomRight,limit),Math.Min(c.BottomLeft,limit));
        item.Key.BorderThickness=config.DesignPreset=="minimal"?new Thickness(0):originalBorders[item.Key];
      }
      if(config.DesignPreset=="minimal") {
        window.Resources["ThemeCardBrush"]=new SolidColorBrush(BlendColor(currentTheme.Background,currentTheme.Card,.55));
        window.Resources["ThemeRaisedBrush"]=new SolidColorBrush(BlendColor(currentTheme.Background,currentTheme.Raised,.65));
      }
      ApplyBackgroundOpacity();
    }
    void ApplyTextScale() {
      foreach(var item in originalFonts)item.Key.FontSize=item.Value*config.TextScale;
      cardLayoutDirty=true;
    }
    void ApplyPortraitCrop() {
      if(string.IsNullOrEmpty(currentArtKey))return;
      foreach(bool compact in new[]{false,true}) {
        var frame=config.Frame(currentArtKey,compact);var target=Find<Grid>(compact?"CompactPortraitTransform":"FullPortraitTransform");
        double width=compact?79:265,height=compact?104:338;
        target.RenderTransformOrigin=new Point(.5,.35);
        var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(frame.Zoom,frame.Zoom));transform.Children.Add(new TranslateTransform(frame.X*width/200,frame.Y*height/200));target.RenderTransform=transform;
      }
      if(syncCropSettings!=null)syncCropSettings();
    }
    string paintedSanityStyle;
    double paintedRingPercentage=double.NaN;
    void UpdateSanityStyle() {
      bool ring=config.SanityStyle=="ring",bar=config.SanityStyle=="bar";
      if(paintedSanityStyle!=config.SanityStyle) {
        cardLayoutDirty=true;
        Find<ProgressBar>("SanityBar").Visibility=bar?Visibility.Visible:Visibility.Hidden;
        Find<ProgressBar>("CompactSanityBar").Visibility=bar?Visibility.Visible:Visibility.Hidden;
        Find<Grid>("FullSanityRing").Visibility=ring?Visibility.Visible:Visibility.Collapsed;
        Find<Grid>("CompactSanityRing").Visibility=ring?Visibility.Visible:Visibility.Collapsed;
        Find<TextBlock>("SanityPercent").Visibility=ring?Visibility.Collapsed:Visibility.Visible;
        Find<TextBlock>("CompactSanityCaption").Visibility=ring?Visibility.Collapsed:Visibility.Visible;
        paintedSanityStyle=config.SanityStyle;
      }
      if(!ring)return;
      double percentage=Find<ProgressBar>("SanityBar").Value;
      if(percentage==paintedRingPercentage)return;
      paintedRingPercentage=percentage;
      foreach(string name in new[]{"FullRingArc","CompactRingArc"}) {
        var path=Find<System.Windows.Shapes.Path>(name);double radius=name=="FullRingArc"?22:12,c=radius+3;
        if(percentage<=0){path.Data=Geometry.Empty;continue;}
        if(percentage>=100){var circle=new EllipseGeometry(new Point(c,c),radius,radius);circle.Freeze();path.Data=circle;continue;}
        double angle=percentage/100*2*Math.PI;
        var figure=new PathFigure {StartPoint=new Point(c,3),IsClosed=false};figure.Segments.Add(new ArcSegment(new Point(c+radius*Math.Sin(angle),c-radius*Math.Cos(angle)),new Size(radius,radius),0,percentage>50,SweepDirection.Clockwise,true));
        var geometry=new PathGeometry();geometry.Figures.Add(figure);geometry.Freeze();path.Data=geometry;
      }
    }
    string WidgetMode(){return config.MicroMode?"micro":config.Compact?"compact":"full";}
    void SetWidgetMode(string mode) {
      double width=window.ActualWidth,height=window.ActualHeight,left=window.Left,top=window.Top;
      int version=++transitionVersion;if(modeTransitionTimer!=null)modeTransitionTimer.Stop();
      foreach(var property in new[]{Window.WidthProperty,Window.HeightProperty,Window.LeftProperty,Window.TopProperty})window.BeginAnimation(property,null);
      config.RememberMode();config.ActivateMode(mode);ApplyLayout(true);SavePosition();
      if(AnimateSurface() && window.IsVisible && width>0 && height>0) {
        var duration=TimeSpan.FromMilliseconds(200);
        foreach(var pair in new[]{Tuple.Create(Window.WidthProperty,width,window.Width),Tuple.Create(Window.HeightProperty,height,window.Height),Tuple.Create(Window.LeftProperty,left,window.Left),Tuple.Create(Window.TopProperty,top,window.Top)})
          window.BeginAnimation(pair.Item1,new DoubleAnimation(pair.Item2,pair.Item3,duration){FillBehavior=FillBehavior.Stop,EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseInOut}});
        modeTransitionTimer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromMilliseconds(235)};
        modeTransitionTimer.Tick+=(s,e)=>{modeTransitionTimer.Stop();if(version==transitionVersion)foreach(var property in new[]{Window.WidthProperty,Window.HeightProperty,Window.LeftProperty,Window.TopProperty})window.BeginAnimation(property,null);};modeTransitionTimer.Start();
      }
    }
    void PaintStudio(DateTimeOffset now) {
      UpdateSanityStyle();PaintSession(now);
      Text("MicroSanity",Find<TextBlock>("SanityNumber").Text+Find<TextBlock>("SanityMaximum").Text);
      bool fresh=sanity!=null && accountToken.Length>0 && !awaitingFreshSanity && sanityError.Length==0 && now>=sanity.Checked && now-sanity.Checked<=TimeSpan.FromMinutes(15);
      var full=sanity==null?(DateTimeOffset?)null:sanity.LocalFullAt();
      Text("MicroRecovery",!fresh?L.T("Data is out of date"):sanity.Estimate(now)>=sanity.Maximum?L.T("Fully restored"):full.HasValue?L.T("In ")+Clock.After(full.Value-now):L.T("Time unavailable"));
      Find<TextBlock>("MicroRecovery").ToolTip=Find<TextBlock>("RecoveryLabel").Text;PaintBannerEnd(now);
    }
  }
}
