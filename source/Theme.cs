using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EndfieldWidget {
  class OperatorTheme {
    public Color Accent, SecondaryAccent, Background, Card, Raised, Input, Border;
    public Color Text, SecondaryText, Muted, Subtle, Ink, Glow, Badge, Selection, Track;
    public double Hue;
    static double Clamp(double value,double low,double high) {return Math.Max(low,Math.Min(high,value));}
    static Color Blend(Color a,Color b,double fraction) {
      return Color.FromRgb((byte)Math.Round(a.R+(b.R-a.R)*fraction),(byte)Math.Round(a.G+(b.G-a.G)*fraction),(byte)Math.Round(a.B+(b.B-a.B)*fraction));
    }
    static Color Alpha(Color color,byte alpha) {return Color.FromArgb(alpha,color.R,color.G,color.B);}
    public static Color Hsv(double hue,double saturation,double value) {
      hue=(hue%360+360)%360;double c=value*saturation,x=c*(1-Math.Abs(hue/60%2-1)),m=value-c;
      double r=0,g=0,b=0;
      if(hue<60){r=c;g=x;}else if(hue<120){r=x;g=c;}else if(hue<180){g=c;b=x;}else if(hue<240){g=x;b=c;}else if(hue<300){r=x;b=c;}else{r=c;b=x;}
      return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    static void ToHsv(byte r,byte g,byte b,out double h,out double s,out double v) {
      double rr=r/255.0,gg=g/255.0,bb=b/255.0,max=Math.Max(rr,Math.Max(gg,bb)),min=Math.Min(rr,Math.Min(gg,bb)),delta=max-min;
      v=max;s=max==0?0:delta/max;
      if(delta==0)h=0;else if(max==rr)h=60*((gg-bb)/delta%6);else if(max==gg)h=60*((bb-rr)/delta+2);else h=60*((rr-gg)/delta+4);
      if(h<0)h+=360;
    }
    static double Linear(byte component) {double c=component/255.0;return c<=0.04045?c/12.92:Math.Pow((c+0.055)/1.055,2.4);}
    static double Luminance(Color c) {return 0.2126*Linear(c.R)+0.7152*Linear(c.G)+0.0722*Linear(c.B);}
    public static double Contrast(Color a,Color b) {double aa=Luminance(a),bb=Luminance(b);return (Math.Max(aa,bb)+0.05)/(Math.Min(aa,bb)+0.05);}
    static Color Readable(Color foreground,Color background,double target) {
      for(int i=0;i<20 && Contrast(foreground,background)<target;i++)foreground=Blend(foreground,Colors.White,0.055);
      return foreground;
    }
    public static OperatorTheme FromAccent(double hue,double saturation,double secondaryHue) {
      var theme=new OperatorTheme {Hue=hue};
      theme.Background=Hsv(hue,0.26,0.082);
      theme.Card=Hsv(hue,0.23,0.137);
      theme.Raised=Hsv(hue,0.22,0.177);
      theme.Input=Hsv(hue,0.19,0.198);
      theme.Border=Hsv(hue,0.18,0.30);
      theme.Accent=Readable(Hsv(hue,Clamp(saturation,0.36,0.56),0.95),theme.Raised,5.0);
      theme.SecondaryAccent=Readable(Hsv(secondaryHue,0.35,0.90),theme.Raised,5.0);
      theme.Text=Blend(Colors.White,theme.Accent,0.07);
      theme.SecondaryText=Readable(Hsv(hue,0.08,0.83),theme.Raised,4.5);
      theme.Muted=Readable(Hsv(hue,0.10,0.66),theme.Raised,4.5);
      theme.Subtle=Readable(Hsv(hue,0.12,0.55),theme.Raised,4.5);
      theme.Ink=Hsv(hue,0.24,0.092);
      theme.Glow=Blend(theme.Background,Hsv(hue,0.45,0.45),0.85);
      theme.Badge=Blend(theme.Raised,theme.Accent,0.15);
      theme.Selection=Blend(theme.Input,theme.Accent,0.14);
      theme.Track=Hsv(hue,0.16,0.285);
      return theme;
    }
    public static OperatorTheme FromPortrait(BitmapSource image) {
      var bitmap=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
      int width=bitmap.PixelWidth,height=bitmap.PixelHeight,stride=width*4;
      // Only sampled rows are needed. Keep the original sampling coordinates
      // and palette exactly, without an image-sized temporary pixel buffer.
      byte[] pixels=new byte[stride];
      const int bins=24;
      var scores=new double[bins];var cosine=new double[bins];var sine=new double[bins];var saturations=new double[bins];
      int step=Math.Max(1,(int)Math.Sqrt(width*height/9000.0));
      for(int y=0;y<height;y+=step) {
        bitmap.CopyPixels(new Int32Rect(0,y,width,1),pixels,stride,0);
        for(int x=0;x<width;x+=step) {
        int p=x*4;if(pixels[p+3]<150)continue;
        double h,s,v;ToHsv(pixels[p+2],pixels[p+1],pixels[p],out h,out s,out v);
        if(s<0.20 || v<0.19 || (v>0.96 && s<0.32))continue;
        double weight=Math.Pow(s,1.35)*(0.5+0.5*v)*(pixels[p+3]/255.0);
        // A portrait's upper central area is usually the character. Translucent
        // backgrounds, neutral clothing and skin contribute less to the palette.
        double dx=x/(double)width-0.5,dy=y/(double)height-0.36;
        weight*=0.6+1.4*Math.Exp(-dx*dx/0.12-dy*dy/0.15);
        if(h<55 && s<0.56 && v>0.45)weight*=0.30;
        int bin=(int)(h/15)%bins;double angle=h*Math.PI/180;
        scores[bin]+=weight;cosine[bin]+=Math.Cos(angle)*weight;sine[bin]+=Math.Sin(angle)*weight;saturations[bin]+=s*weight;
      }}
      double highest=0;int dominant=0;
      for(int i=0;i<bins;i++){double score=scores[(i+bins-1)%bins]+scores[i]+scores[(i+1)%bins];if(score>highest){highest=score;dominant=i;}}
      if(highest<0.01)return FromAccent(215,0.38,190);
      double cx=0,sy=0,sat=0,total=0;
      for(int offset=-1;offset<=1;offset++){int i=(dominant+offset+bins)%bins;cx+=cosine[i];sy+=sine[i];sat+=saturations[i];total+=scores[i];}
      double hue=(Math.Atan2(sy,cx)*180/Math.PI+360)%360;
      int secondary=dominant;double secondScore=0;
      for(int i=0;i<bins;i++) {
        double distance=Math.Min(Math.Abs(i-dominant),bins-Math.Abs(i-dominant));
        if(distance<3)continue;
        if(scores[i]>secondScore){secondScore=scores[i];secondary=i;}
      }
      double secondHue=secondScore>highest*0.06 ? (secondary+0.5)*15:hue;
      return FromAccent(hue,sat/Math.Max(total,0.001)+0.06,secondHue);
    }
    static void Brush(ResourceDictionary resources,string name,Color color) {
      var brush=new SolidColorBrush(color);brush.Freeze();resources[name]=brush;
    }
    public void Apply(ResourceDictionary resources) {
      var values=new Dictionary<string,Color> {
        {"ThemeBackgroundBrush",Background},{"ThemeCardBrush",Card},{"ThemeRaisedBrush",Raised},
        {"ThemeInputBrush",Input},{"ThemeBorderBrush",Border},{"ThemeTextBrush",Text},
        {"ThemeSecondaryTextBrush",SecondaryText},{"ThemeMutedBrush",Muted},{"ThemeSubtleBrush",Subtle},
        {"ThemeAccentBrush",Accent},{"ThemeSecondaryAccentBrush",SecondaryAccent},
        {"ThemeSoftAccentBrush",Blend(SecondaryText,Accent,0.30)},{"ThemeInkBrush",Ink},
        {"ThemeBadgeBrush",Badge},{"ThemeSelectionBrush",Selection},{"ThemeTrackBrush",Track},
        {"ThemeChromeBrush",Alpha(Background,155)},{"ThemeWatermarkBrush",Alpha(Accent,13)},
        {"ThemeLineBrush",Alpha(Accent,28)}
      };
      foreach(var item in values)Brush(resources,item.Key,item.Value);
      resources["ThemeBackgroundColor"]=Background;resources["ThemeGlowColor"]=Glow;
      resources["ThemeClearColor"]=Alpha(Background,0);resources["ThemeFadeColor"]=Alpha(Background,240);
      resources["ThemeShadeColor"]=Alpha(Background,181);resources["ThemeAccentColor"]=Accent;
      resources["ThemeSecondaryAccentColor"]=SecondaryAccent;
    }
  }
}
