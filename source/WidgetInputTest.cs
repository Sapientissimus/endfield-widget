using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EndfieldWidget {
  partial class Widget {
    static async Task TestTextInput(UIElement input,string text) {
      var composition=new TextComposition(InputManager.Current,input,text);
      input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,composition) {RoutedEvent=TextCompositionManager.TextInputEvent});
      await input.Dispatcher.InvokeAsync(new Action(()=>{}),DispatcherPriority.ContextIdle);
    }
    public void InputTest(string reportPath,string imagePath) {
      var dialog=SettingsWindow();dialog.WindowStartupLocation=WindowStartupLocation.Manual;dialog.Left=-10000;dialog.Top=-10000;
      dialog.Show();dialog.UpdateLayout();
      dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var report=new List<string>();bool passed=true;
        Action<bool,string> check=(condition,label)=>{report.Add((condition?"PASS: ":"FAILED: ")+label);passed&=condition;};
        try {
          var input=(PasswordBox)dialog.FindName("TokenInput");input.ApplyTemplate();dialog.UpdateLayout();
          var host=(ScrollViewer)input.Template.FindName("PART_ContentHost",input);
          check(host.ViewportHeight>=input.FontSize,"Token text viewport has room for text and caret ("+host.ViewportHeight.ToString("0.0",CultureInfo.InvariantCulture)+" px)");
          var hit=dialog.InputHitTest(input.TranslatePoint(new Point(input.ActualWidth/2,input.ActualHeight/2),dialog)) as DependencyObject;
          while(hit!=null && hit!=input)hit=VisualTreeHelper.GetParent(hit);
          check(input.IsEnabled && input.IsHitTestVisible && hit==input,"Token field receives pointer hits at its center");
          input.Focus();Keyboard.Focus(input);
          check(input.IsKeyboardFocused,"Token field accepts keyboard focus");
          int changes=0;input.PasswordChanged+=(s,e)=>changes++;
          foreach(char character in "demo-token")await TestTextInput(input,character.ToString());
          check(input.Password=="demo-token" && changes>0,"Typing edits the masked field and raises PasswordChanged");
          input.SelectAll();await TestTextInput(input,"{\"code\":0,\"data\":{\"content\":\"demo-replacement\"}}");
          check(SkportClient.NormalizeToken(input.Password)=="demo-replacement","Selection replacement accepts a complete dummy token response");
          input.SelectAll();await TestTextInput(input,"demo-masked");
          EditingCommands.Backspace.Execute(null,input);
          await input.Dispatcher.InvokeAsync(new Action(()=>{}),DispatcherPriority.ContextIdle);
          check(input.Password=="demo-maske","Backspace edits the token field");
          dialog.UpdateLayout();
          if(!string.IsNullOrEmpty(imagePath)) {
            var bitmap=new RenderTargetBitmap((int)dialog.ActualWidth,(int)dialog.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(dialog);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(imagePath))encoder.Save(stream);
          }
          input.Clear();check(input.Password.Length==0,"Clearing removes test input");
          report.Add("Dummy data only; no account requests, clipboard access, or user profile changes.");
        } catch(Exception ex) {passed=false;report.Add("FAILED: "+ex.ToString());}
        finally {
          File.WriteAllText(reportPath,string.Join(Environment.NewLine,report),Encoding.UTF8);if(!passed)Environment.ExitCode=1;
          closing=true;http.Dispose();dialog.Close();window.Close();Application.Current.Shutdown();
        }
      }));
    }
  }
}
