using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EndfieldWidget {
  partial class Widget {
    Rect ReadingBounds(string name,Visual ancestor) {
      var element=Find<FrameworkElement>(name);return element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    }
    bool ReadingContains(string parent,string child) {
      var container=Find<FrameworkElement>(parent);var box=ReadingBounds(child,container);
      return box.Left>=-1 && box.Top>=-1 && box.Right<=container.ActualWidth+1 && box.Bottom<=container.ActualHeight+1;
    }
    void CheckReadingLayout(Action<bool,string> check,string folder) {
      check(Clock.After(TimeSpan.FromMinutes(1))=="1 minute" && Clock.After(TimeSpan.FromMinutes(21))=="21 minutes" && Clock.After(TimeSpan.FromMinutes(22))=="22 minutes" && Clock.After(TimeSpan.FromMinutes(14))=="14 minutes","Recovery durations use complete duration words and the correct case after 'through/after'");
      check(Clock.After(TimeSpan.FromHours(1))=="1 hour" && Clock.After(TimeSpan.FromHours(2))=="2 hours" && Clock.After(TimeSpan.FromHours(11))=="11 hours" && Clock.After(TimeSpan.FromHours(25))=="1 day 1 hour" && Clock.Remaining(TimeSpan.FromHours(25))=="1 day 1 hour","Hour and day durations handle singular, plural and the eleven-to-fourteen exception");
      string original=Json.Encode(config),oldToken=accountToken,oldError=sanityError;var oldSanity=sanity;var oldSchedule=schedule;var oldBanners=bannerState;var oldDisplays=displayOverride;bool oldAwaiting=awaitingFreshSanity;
      try {
        displayOverride=new List<DisplayArea>{new DisplayArea{Id="reading",Primary=true,Work=new Rect(0,0,2560,1600)}};
        config.MonitorId="reading";config.Scale=config.CompactScale=config.MicroScale=1;config.ShowUpdates=config.ShowNextDebut=config.ShowPlayButton=true;
        accountToken="offline-reading-fixture";awaitingFreshSanity=false;sanityError="";
        var now=DateTimeOffset.UtcNow;
        sanity=new Sanity{RoleId="reading-fixture",Nickname="Endministrator",Current=93,Maximum=360,Checked=now,ServerTime=now.ToUnixTimeSeconds(),FullAt=now.AddDays(1).AddHours(4).AddMinutes(29).ToUnixTimeSeconds()};
        schedule=new Schedule{Version="Sanctuary of Ink — official maintenance",Source=NewsClient.NewsUrl+"/9560",Start=now.AddDays(2),End=now.AddDays(2).AddHours(6),Checked=now};
        bannerState=new BannerState{Debuts=new List<Debut>{new Debut{Name="Argent Flow",Key="reading-fixture",Source=NewsClient.NewsUrl+"/9560",EuropeStart=now.AddDays(21).AddHours(8),AsiaStart=now.AddDays(21)}}};
        foreach(double text in new[]{.85,1.0,1.2})foreach(string style in new[]{"bar","ring","numbers"})foreach(string mode in new[]{"full","compact","micro"}) {
          config.TextScale=text;config.SanityStyle=style;config.MicroMode=mode=="micro";config.Compact=mode=="compact";ApplyTextScale();Paint();ApplyLayout(true);window.UpdateLayout();
          var root=Find<Grid>("WidgetLayout");string tag=mode+" / "+style+" / text "+text;
          if(mode=="micro") {
            check(Find<TextBlock>("MicroRecoveryHeading").Text=="Full Sanity recovery" && Find<TextBlock>("MicroRecovery").Text.StartsWith("In ") && ReadingContains("MicroView","MicroRecoveryHeading") && ReadingContains("MicroView","MicroRecovery"),"The ultra-small view spells out the recovery event and duration without clipping: "+tag);
          }else {
            string prefix=mode=="full"?"":"Compact";var current=Find<TextBlock>(prefix+"SanityNumber");var maximum=Find<TextBlock>(prefix+"SanityMaximum");
            var a=ReadingBounds(prefix+"SanityNumber",root);var b=ReadingBounds(prefix+"SanityMaximum",root);var bar=ReadingBounds(prefix+"SanityBar",root);
            bool aligned=current.FontSize==maximum.FontSize && current.FontWeight==maximum.FontWeight && Math.Abs(a.Top-b.Top)<.1 && Math.Abs(a.Height-b.Height)<.1;
            bool spacing=bar.Top-a.Bottom>=(mode=="full"?9:7) && ReadingContains(mode=="full"?"FullSanityCard":"CompactSanityLayout",prefix+"RecoveryLabel");
            check(aligned && spacing,"Sanity values share a baseline and type size, with clear space above the indicator and contained recovery text: "+tag);
            if(mode=="full") {
              var title=ReadingBounds("NextOperator",root);var countdown=ReadingBounds("NextBannerCountdown",root);var date=ReadingBounds("NextBannerDate",root);
              check(countdown.Top-title.Bottom>=4 && date.Top-countdown.Bottom>=4 && ReadingContains("UpcomingCard","NextBannerDate") && ReadingContains("FullUpdateCard","CountdownLabel"),"Debut title, full duration and Eastern European date occupy separate rows inside naturally sized cards: "+tag);
            }
          }
        }
        config.MicroMode=false;config.Compact=false;config.SanityStyle="bar";config.TextScale=1.2;ApplyTextScale();sanityError="Could not refresh Sanity. Check the network connection and try again.";sanity.Checked=now.AddHours(-1);Paint();window.UpdateLayout();
        check(ReadingContains("FullSanityCard","SanityStatus") && ReadingContains("FullSanityCard","RecoveryLabel"),"A multiline stale-reading explanation grows the card instead of overlapping its footer");
        sanityError="";sanity.Checked=DateTimeOffset.UtcNow;Paint();window.UpdateLayout();SaveImage(window,Path.Combine(folder,"polished-full.png"));
        config.MicroMode=true;config.Compact=false;Paint();ApplyLayout(true);window.UpdateLayout();SaveImage(window,Path.Combine(folder,"polished-micro.png"));
        double height=window.Height;var grid=Find<Grid>("MicroDragHandle");Text("MicroSanity","9999 / 9999");ApplyLayout(true);window.UpdateLayout();
        var counts=ReadingBounds("MicroSanity",grid);var heading=ReadingBounds("MicroRecoveryHeading",grid);
        check(heading.Left-counts.Right>=12 && window.Height>=height && ReadingContains("MicroView","MicroRecovery"),"Larger Sanity values leave a real gap before the full recovery heading and allow the small row to grow");
      }finally {
        var restored=new JavaScriptSerializer().Deserialize<Config>(original);foreach(var property in typeof(Config).GetProperties())if(property.CanRead && property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        accountToken=oldToken;sanityError=oldError;sanity=oldSanity;schedule=oldSchedule;bannerState=oldBanners;displayOverride=oldDisplays;awaitingFreshSanity=oldAwaiting;ApplyTextScale();Paint();ApplyLayout(true);
      }
    }
  }
}
