using System;
using System.Windows;
using System.Windows.Controls;

namespace EndfieldWidget {
  partial class Widget {
    bool cardLayoutDirty;
    static bool IsCardText(string name) {
      switch(name) {
        case "SanityNumber":case "SanityMaximum":case "RecoveryLabel":case "SanityStatus":
        case "VersionLabel":case "MaintenanceDate":case "MaintenanceRange":case "CountdownLabel":case "NewsStatus":
        case "NextOperator":case "NextBannerDate":case "NextBannerCountdown":case "NextBannerSummary":case "MicroRecovery":case "MicroSanity":return true;
        default:return false;
      }
    }
    double ContentHeight(string name,double width) {
      // Inactive Grid views can retain an arranged measure cache from another mode.
      var content=Find<FrameworkElement>(name);content.InvalidateMeasure();content.Measure(new Size(width,double.PositiveInfinity));return Math.Ceiling(content.DesiredSize.Height);
    }
    double CardHeight(string borderName,string contentName,double width) {
      var card=Find<Border>(borderName);var padding=card.Padding;var edge=card.BorderThickness;var margin=card.Margin;
      return ContentHeight(contentName,width-padding.Left-padding.Right-edge.Left-edge.Right-margin.Left-margin.Right)+padding.Top+padding.Bottom+edge.Top+edge.Bottom+margin.Top+margin.Bottom;
    }
    void SyncCompactText() {
      Text("CompactSanityNumber",Find<TextBlock>("SanityNumber").Text);Text("CompactSanityMaximum",Find<TextBlock>("SanityMaximum").Text);
      Text("CompactRecoveryLabel",Find<TextBlock>("RecoveryLabel").Text);Text("CompactVersion",Find<TextBlock>("VersionLabel").Text);
      Text("CompactMaintenanceDate",Find<TextBlock>("MaintenanceDate").Text);Text("CompactMaintenanceRange",Find<TextBlock>("MaintenanceRange").Text);Text("CompactCountdownLabel",Find<TextBlock>("CountdownLabel").Text);
    }
  }
}
