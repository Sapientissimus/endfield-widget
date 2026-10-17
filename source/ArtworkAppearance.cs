using System;
using System.Windows;
using System.Windows.Controls;

namespace EndfieldWidget {
  partial class Widget {
    void ApplyArtworkAppearance() {
      Find<Grid>("PortraitFade").Opacity=config.PortraitOpacity;
      Find<Grid>("CompactPortraitFade").Opacity=config.PortraitOpacity;
      Find<TextBlock>("ArtWatermark").Visibility=config.ShowWatermark?Visibility.Visible:Visibility.Collapsed;
    }
    void WireArtworkAppearance(Window dialog) {
      var opacity=(Slider)dialog.FindName("PortraitOpacitySlider");
      var value=(TextBlock)dialog.FindName("PortraitOpacityValue");
      var watermark=(CheckBox)dialog.FindName("WatermarkSwitch");
      opacity.Value=config.PortraitOpacity*100;value.Text=Math.Round(opacity.Value)+"%";watermark.IsChecked=config.ShowWatermark;
      opacity.ValueChanged+=(s,e)=>{config.PortraitOpacity=opacity.Value/100;value.Text=Math.Round(opacity.Value)+"%";ApplyArtworkAppearance();SavePosition();};
      watermark.Click+=(s,e)=>{config.ShowWatermark=watermark.IsChecked==true;ApplyArtworkAppearance();SavePosition();};
    }
  }
}
