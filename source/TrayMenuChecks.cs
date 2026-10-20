using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Forms=System.Windows.Forms;

namespace EndfieldWidget {
  partial class Widget {
    internal void TrayMenuTest(string reportPath) {
      window.Show();window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(async()=>{
        var report=new System.Collections.Generic.List<string>();
        try {await CheckTrayMenu((ok,label)=>{if(!ok)throw new Exception(label);report.Add("PASS: "+label);},Path.GetDirectoryName(Path.GetFullPath(reportPath)));report.Add("ALL TRAY CHECKS PASSED. Isolated profile and launch stub; no real startup, game or notifications.");}
        catch(Exception error){report.Add("FAILED: "+error);Environment.ExitCode=1;}
        finally{File.WriteAllLines(reportPath,report);Exit();}
      }));
    }
    static Forms.ToolStripMenuItem TrayAction(Forms.ContextMenuStrip menu,string name){return (Forms.ToolStripMenuItem)menu.Items.Find(name,false).Single();}
    static void SaveTrayMenu(TrayMenu menu,string path) {
      Exception drawingError=null;System.Threading.ThreadExceptionEventHandler handler=(s,e)=>drawingError=e.Exception;
      Forms.Application.ThreadException+=handler;
      try {
        menu.Reflow();menu.CreateControl();menu.PerformLayout();
        using(var bitmap=new Bitmap(menu.Width,menu.Height,PixelFormat.Format32bppArgb)){menu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));if(drawingError!=null)throw new InvalidOperationException("Native tray rendering failed",drawingError);bitmap.Save(path,ImageFormat.Png);}
      }finally{Forms.Application.ThreadException-=handler;}
    }
    async Task CheckTrayMenu(Action<bool,string> check,string folder) {
      string saved=Json.Encode(config),language=L.Code;var oldTray=tray;var oldVisibility=trayVisibility;var oldClick=trayClickThrough;var oldPlay=trayGamePlay;var oldTheme=currentTheme;var oldLauncher=gameLauncher;var oldProbe=gameProbe;var oldActivity=gameActivity;bool visible=window.IsVisible,oldToggle=globalToggleKey,oldMenu=globalMenuKey;TrayMenu menu=null;
      try {
        gameProbe=()=>false;gameActivity=new GameActivity();int launches=0;gameLauncher=()=>{launches++;return Task.FromResult(0);};
        menu=CreateTrayMenu();tray=new Forms.NotifyIcon{Visible=false,ContextMenuStrip=menu};globalToggleKey=globalMenuKey=true;UpdateTrayPersonalization();UpdateTrayShortcuts();
        string[] names={"TrayVisibility","TrayWidgetMenu","TrayClickThrough","TrayPlay","TrayCalendar","TraySettings","TrayClose"};
        check(menu.Items.OfType<Forms.ToolStripMenuItem>().Select(i=>i.Name).SequenceEqual(names) && menu.Items.OfType<Forms.ToolStripSeparator>().Count()==2,"The themed tray retains all seven actual actions and both action-group separators");
        check(menu.AutoClose && menu.ShowImageMargin && !menu.ShowCheckMargin && menu.Items.OfType<Forms.ToolStripMenuItem>().All(i=>i.Image!=null && i.ShortcutKeys==Forms.Keys.None),"Native menu dismissal and image layout remain enabled without adding local shortcut handlers");
        check(TrayAction(menu,"TrayVisibility").Text==L.T("Hide widget") && TrayAction(menu,"TrayVisibility").ShortcutKeyDisplayString==config.HotkeyToggle && TrayAction(menu,"TrayWidgetMenu").ShortcutKeyDisplayString==config.HotkeyMenu,"Registered global shortcut hints occupy their own column instead of being appended to action labels");
        globalToggleKey=globalMenuKey=false;UpdateTrayPersonalization();UpdateTrayShortcuts();
        check(TrayAction(menu,"TrayVisibility").ShortcutKeyDisplayString=="" && TrayAction(menu,"TrayWidgetMenu").ShortcutKeyDisplayString=="","Unavailable global shortcuts have no misleading tray hints");
        globalToggleKey=globalMenuKey=true;UpdateTrayPersonalization();UpdateTrayShortcuts();
        foreach(var choice in L.Languages) {
          string previous=L.Code;L.Set(choice.Code);TranslateTray(menu.Items,previous);UpdateTrayPersonalization();UpdateGameLaunchControls();
          SaveTrayMenu(menu,Path.Combine(folder,"tray-menu-"+choice.Code+".png"));
          check(menu.Width>=248 && menu.Height>200 && menu.Items.OfType<Forms.ToolStripMenuItem>().All(i=>i.Bounds.Width>0 && i.Bounds.Height>=25 && i.Bounds.Right<=menu.ClientSize.Width && i.Bounds.Right>=menu.ClientSize.Width-2),"Localized native tray rows fill the menu without overflowing: "+choice.Code);
          check(!menu.Items[0].CanSelect && menu.Items[0].Name=="TrayHeader" && menu.Items[1].Bounds.Top>=menu.Items[0].Bounds.Bottom,"The noninteractive header has its own row above the first action: "+choice.Code);
          check(TrayAction(menu,"TraySettings").Text==L.T("Settings") && TrayAction(menu,"TrayClose").Text==L.T("Close"),"Tray actions use the selected interface language: "+choice.Code);
        }
        string previousLanguage=L.Code;L.Set(language);TranslateTray(menu.Items,previousLanguage);UpdateTrayPersonalization();UpdateGameLaunchControls();
        var initial=menu.Palette.Accent;currentTheme=OperatorTheme.FromAccent(210,.5,190);ApplyDesignPreset();
        check(menu.Palette.Accent!=initial && menu.Palette.Background.R==currentTheme.Background.R && menu.Palette.Background.G==currentTheme.Background.G,"An operator or style change updates the open tray's palette through the actual appearance path");
        TrayAction(menu,"TraySettings").Select();SaveTrayMenu(menu,Path.Combine(folder,"tray-menu-blue-selected.png"));
        menu.ApplyTheme(currentTheme,window.Resources,true);check(menu.Palette.HighContrast && menu.Palette.Background==SystemColors.Menu && menu.Palette.Selection==SystemColors.Highlight && menu.Palette.SelectedText==SystemColors.HighlightText,"High Contrast uses the Windows menu and selection colors instead of forcing the operator palette");
        SaveTrayMenu(menu,Path.Combine(folder,"tray-menu-high-contrast.png"));menu.ApplyTheme(currentTheme,window.Resources,false);
        foreach(float density in new[]{1f,1.5f,2f})using(var scaled=new TrayMenu(density)) {
          scaled.ApplyTheme(currentTheme,window.Resources,false);foreach(string name in names)scaled.Items.Add(new Forms.ToolStripMenuItem(name){Name=name});
          SaveTrayMenu(scaled,Path.Combine(folder,"tray-menu-scale-"+(int)(density*100)+".png"));
          check(scaled.MinimumSize.Width==(int)Math.Round(248*density) && scaled.ImageScalingSize.Width==(int)Math.Round(16*density) && scaled.Items.OfType<Forms.ToolStripMenuItem>().All(i=>i.Bounds.Height>=25*density),"Tray spacing and vector icon slots scale with display density: "+density);
        }
        var dialogKey=typeof(Forms.ToolStripDropDown).GetMethod("ProcessDialogKey",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        menu.Show(new Point((int)window.Left,(int)window.Top));TrayAction(menu,"TrayVisibility").Select();
        check((bool)dialogKey.Invoke(menu,new object[]{Forms.Keys.Down}) && TrayAction(menu,"TrayWidgetMenu").Selected,"Native arrow-key navigation moves between actions and skips the static header");
        check((bool)dialogKey.Invoke(menu,new object[]{Forms.Keys.Escape}) && !menu.Visible,"Native Escape dismisses the tray popup");
        bool click=config.ClickThrough;TrayAction(menu,"TrayClickThrough").PerformClick();
        check(config.ClickThrough!=click && trayClickThrough.Checked==config.ClickThrough,"The real native click-through action updates its visible check mark and widget setting");
        SaveTrayMenu(menu,Path.Combine(folder,"tray-menu-checked.png"));
        TrayAction(menu,"TrayClickThrough").PerformClick();check(config.ClickThrough==click,"Click-through can be switched back using the same native action");
        TrayAction(menu,"TrayVisibility").PerformClick();check(!window.IsVisible && trayVisibility.Text==L.T("Show widget"),"The native visibility action hides the widget and changes to Show");
        TrayAction(menu,"TrayVisibility").PerformClick();check(window.IsVisible && trayVisibility.Text==L.T("Hide widget"),"The native Show action restores the widget and changes back to Hide");
        TrayAction(menu,"TrayPlay").PerformClick();await gameLaunchTask;check(launches==1,"The themed tray Play action dispatches the shared launch stub exactly once");
        gameLaunching=true;UpdateGameLaunchControls();check(!trayGamePlay.Enabled,"A pending launch still disables the native tray Play action");gameLaunching=false;UpdateGameLaunchControls();
        TrayAction(menu,"TrayCalendar").PerformClick();check(calendarDialog!=null && calendarDialog.IsVisible,"The styled Calendar action still opens the real calendar window");calendarDialog.Close();
        TrayAction(menu,"TraySettings").PerformClick();check(settingsDialog!=null && settingsDialog.IsVisible,"The styled Settings action still opens the real settings window");settingsDialog.Close();
        TrayAction(menu,"TrayWidgetMenu").PerformClick();check(widgetMenu.IsOpen,"The styled Widget menu action still opens the widget's existing context menu");widgetMenu.IsOpen=false;
      }finally {
        if(settingsDialog!=null)settingsDialog.Close();if(calendarDialog!=null)calendarDialog.Close();gameLaunching=false;widgetMenu.IsOpen=false;
        if(tray!=oldTray){tray.ContextMenuStrip=null;tray.Dispose();}if(menu!=null)menu.Dispose();tray=oldTray;trayVisibility=oldVisibility;trayClickThrough=oldClick;trayGamePlay=oldPlay;currentTheme=oldTheme;globalToggleKey=oldToggle;globalMenuKey=oldMenu;
        var restored=new JavaScriptSerializer().Deserialize<Config>(saved);foreach(var property in typeof(Config).GetProperties())if(property.CanWrite)property.SetValue(config,property.GetValue(restored,null),null);
        L.Set(language);gameLauncher=oldLauncher;gameProbe=oldProbe;gameActivity=oldActivity;ApplyDesignPreset();SetClickThrough(config.ClickThrough);UpdateGameLaunchControls();if(visible)window.Show();else window.Hide();
      }
    }
  }
}
