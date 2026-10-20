using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Forms=System.Windows.Forms;
using Media=System.Windows.Media;

namespace EndfieldWidget {
  sealed class TrayMenuPalette {
    public Color Background,Text,Muted,Accent,Selection,Border,SelectedText;
    public bool HighContrast;
    static Color Convert(Media.Color color){return Color.FromArgb(color.R,color.G,color.B);}
    static Color Resource(System.Windows.ResourceDictionary resources,string name,Media.Color fallback) {
      var brush=resources[name] as Media.SolidColorBrush;return Convert(brush==null?fallback:brush.Color);
    }
    public static TrayMenuPalette Create(OperatorTheme theme,System.Windows.ResourceDictionary resources,bool highContrast) {
      if(highContrast)return new TrayMenuPalette{Background=SystemColors.Menu,Text=SystemColors.MenuText,Muted=SystemColors.GrayText,Accent=SystemColors.MenuText,Selection=SystemColors.Highlight,SelectedText=SystemColors.HighlightText,Border=SystemColors.WindowFrame,HighContrast=true};
      return new TrayMenuPalette{Background=Resource(resources,"ThemeBackgroundBrush",theme.Background),Text=Convert(theme.Text),Muted=Convert(theme.Muted),Accent=Convert(theme.Accent),Selection=Resource(resources,"ThemeSelectionBrush",theme.Selection),SelectedText=Convert(theme.Text),Border=Convert(theme.Border)};
    }
  }

  sealed class TrayMenuRenderer:Forms.ToolStripRenderer {
    internal TrayMenuPalette Palette;
    readonly Font headerFont;
    readonly float scale;
    public TrayMenuRenderer(float scale,Font headerFont){this.scale=scale;this.headerFont=headerFont;}
    int S(float value){return Math.Max(1,(int)Math.Round(value*scale));}
    static GraphicsPath Rounded(Rectangle rectangle,int radius) {
      int diameter=Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height));var path=new GraphicsPath();
      path.AddArc(rectangle.Left,rectangle.Top,diameter,diameter,180,90);path.AddArc(rectangle.Right-diameter,rectangle.Top,diameter,diameter,270,90);path.AddArc(rectangle.Right-diameter,rectangle.Bottom-diameter,diameter,diameter,0,90);path.AddArc(rectangle.Left,rectangle.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
    }
    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) {
      e.Graphics.Clear(Palette.Background);
    }
    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e) {
      using(var border=new Pen(Palette.Border))e.Graphics.DrawRectangle(border,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);
    }
    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e) {
      if(!e.Item.Selected || !e.Item.Enabled)return;
      var rectangle=new Rectangle(2,1,Math.Max(1,e.Item.Width-4),Math.Max(1,e.Item.Height-2));
      using(var fill=new SolidBrush(Palette.Selection)) {
        if(Palette.HighContrast)e.Graphics.FillRectangle(fill,rectangle);
        else {var old=e.Graphics.SmoothingMode;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Rounded(rectangle,S(5)))e.Graphics.FillPath(fill,path);e.Graphics.SmoothingMode=old;}
      }
    }
    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e) {
      using(var line=new Pen(Palette.Border))e.Graphics.DrawLine(line,S(9),e.Item.Height/2,e.Item.Width-S(9),e.Item.Height/2);
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e) {
      if(e.Item.Name=="TrayHeader") {
        var bounds=new Rectangle(S(2),0,Math.Max(1,e.Item.Width-S(10)),Math.Max(1,e.Item.Height-S(3)));
        Forms.TextRenderer.DrawText(e.Graphics,"ENDFIELD",headerFont,bounds,Palette.Accent,Forms.TextFormatFlags.Left|Forms.TextFormatFlags.VerticalCenter|Forms.TextFormatFlags.NoPadding);
        Forms.TextRenderer.DrawText(e.Graphics,BuildInfo.Version,headerFont,bounds,Palette.Muted,Forms.TextFormatFlags.Right|Forms.TextFormatFlags.VerticalCenter|Forms.TextFormatFlags.NoPadding);
        return;
      }
      var action=e.Item as Forms.ToolStripMenuItem;if(action==null || e.Text!=action.Text)return;
      Color color=!action.Enabled?Palette.Muted:action.Selected?Palette.SelectedText:Palette.Text;
      string shortcut=action.ShowShortcutKeys?action.ShortcutKeyDisplayString:"";
      int shortcutWidth=string.IsNullOrEmpty(shortcut)?0:Forms.TextRenderer.MeasureText(e.Graphics,shortcut,action.Font).Width;
      var flags=Forms.TextFormatFlags.VerticalCenter|Forms.TextFormatFlags.SingleLine|Forms.TextFormatFlags.NoPadding|Forms.TextFormatFlags.NoPrefix;
      var labelBounds=new Rectangle(S(34),0,Math.Max(1,action.Width-S(48)-(shortcutWidth>0?shortcutWidth+S(20):0)),action.Height);
      Forms.TextRenderer.DrawText(e.Graphics,action.Text,action.Font,labelBounds,color,flags|Forms.TextFormatFlags.Left);
      if(shortcutWidth>0)Forms.TextRenderer.DrawText(e.Graphics,shortcut,action.Font,new Rectangle(action.Width-S(12)-shortcutWidth,0,shortcutWidth,action.Height),action.Enabled && !action.Selected?Palette.Muted:color,flags|Forms.TextFormatFlags.Right);
    }
    Color IconColor(Forms.ToolStripItem item){return !item.Enabled?Palette.Muted:item.Selected && Palette.HighContrast?Palette.SelectedText:Palette.Accent;}
    protected override void OnRenderItemImage(Forms.ToolStripItemImageRenderEventArgs e) {
      var item=e.Item as Forms.ToolStripMenuItem;if(item==null || item.Checked)return;DrawIcon(e.Graphics,item.Name,IconBounds(item),IconColor(item));
    }
    Rectangle IconBounds(Forms.ToolStripItem item){return new Rectangle(S(8),(item.Height-S(16))/2,S(16),S(16));}
    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e) {DrawIcon(e.Graphics,"Checked",IconBounds(e.Item),IconColor(e.Item));}
    static void DrawIcon(Graphics graphics,string name,Rectangle rectangle,Color color) {
      if(rectangle.Width<=0 || rectangle.Height<=0)return;
      var state=graphics.Save();
      try {
        graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.TranslateTransform(rectangle.Left,rectangle.Top);graphics.ScaleTransform(rectangle.Width/16f,rectangle.Height/16f);
        using(var pen=new Pen(color,1.35f)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
          switch(name) {
            case "TrayVisibility":
              using(var eye=new GraphicsPath()){eye.AddBezier(1,8,5,2,11,2,15,8);eye.AddBezier(15,8,11,14,5,14,1,8);graphics.DrawPath(pen,eye);}graphics.DrawEllipse(pen,6,6,4,4);break;
            case "TrayWidgetMenu": foreach(int x in new[]{2,9})foreach(int y in new[]{2,9})graphics.DrawRectangle(pen,x,y,5,5);break;
            case "TrayClickThrough": graphics.DrawPolygon(pen,new[]{new PointF(3,1),new PointF(3,13),new PointF(6.5f,10),new PointF(9,15),new PointF(11,14),new PointF(8.5f,9),new PointF(13,9)});break;
            case "TrayPlay": using(var brush=new SolidBrush(color))graphics.FillPolygon(brush,new[]{new PointF(4,2),new PointF(14,8),new PointF(4,14)});break;
            case "TrayCalendar": graphics.DrawRectangle(pen,2,4,12,10);graphics.DrawLine(pen,2,7,14,7);graphics.DrawLine(pen,5,2,5,5);graphics.DrawLine(pen,11,2,11,5);break;
            case "TraySettings": graphics.DrawEllipse(pen,4,4,8,8);graphics.DrawEllipse(pen,6.5f,6.5f,3,3);for(int i=0;i<8;i++){double angle=i*Math.PI/4;graphics.DrawLine(pen,(float)(8+4*Math.Cos(angle)),(float)(8+4*Math.Sin(angle)),(float)(8+6.7*Math.Cos(angle)),(float)(8+6.7*Math.Sin(angle)));}break;
            case "TrayClose": graphics.DrawArc(pen,2,2,12,12,-55,290);graphics.DrawLine(pen,8,1,8,7);break;
            case "Checked": graphics.DrawLines(pen,new[]{new PointF(2,8),new PointF(6,12),new PointF(14,3)});break;
          }
        }
      }finally{graphics.Restore(state);}
    }
  }

  sealed class TrayMenu:Forms.ContextMenuStrip {
    readonly Bitmap iconSlot=new Bitmap(16,16,PixelFormat.Format32bppArgb);
    readonly Font menuFont=new Font("Segoe UI",9f,FontStyle.Regular,GraphicsUnit.Point);
    readonly Font headerFont=new Font("Segoe UI",8f,FontStyle.Bold,GraphicsUnit.Point);
    readonly TrayMenuRenderer themedRenderer;
    readonly float scale;
    bool arranging;
    static float ScreenScale(){using(var graphics=Graphics.FromHwnd(IntPtr.Zero))return graphics.DpiX/96f;}
    public TrayMenu():this(ScreenScale()) { }
    internal TrayMenu(float scale) {
      this.scale=Math.Max(.75f,Math.Min(4,scale));themedRenderer=new TrayMenuRenderer(this.scale,headerFont);Renderer=themedRenderer;
      Font=menuFont;ShowImageMargin=true;ShowCheckMargin=false;ShowItemToolTips=true;AutoClose=true;AccessibleName="Endfield";
      AutoSize=false;Padding=new Forms.Padding(S(6));MinimumSize=new Size(S(248),0);ImageScalingSize=new Size(S(16),S(16));
      ItemAdded+=(s,e)=>{var item=e.Item as Forms.ToolStripMenuItem;if(item!=null){item.Image=iconSlot;item.Padding=new Forms.Padding(S(4),0,S(10),0);item.ShowShortcutKeys=true;item.ShortcutKeys=Forms.Keys.None;}e.Item.TextChanged+=(sender,args)=>Reflow();Reflow();};
      Items.Add(new Forms.ToolStripLabel("ENDFIELD"){Name="TrayHeader",Font=headerFont,AccessibleRole=Forms.AccessibleRole.StaticText});
    }
    int S(float value){return Math.Max(1,(int)Math.Round(value*scale));}
    internal TrayMenuPalette Palette {get{return themedRenderer.Palette;}}
    internal void Reflow() {
      if(arranging || IsDisposed)return;arranging=true;SuspendLayout();
      try {
        int label=0,shortcut=0;
        foreach(Forms.ToolStripItem item in Items){var action=item as Forms.ToolStripMenuItem;if(action==null)continue;label=Math.Max(label,Forms.TextRenderer.MeasureText(action.Text,Font).Width);if(!string.IsNullOrEmpty(action.ShortcutKeyDisplayString))shortcut=Math.Max(shortcut,Forms.TextRenderer.MeasureText(action.ShortcutKeyDisplayString,Font).Width);}
        int width=Math.Max(S(248),S(56)+label+(shortcut>0?S(28)+shortcut:0)),height=Padding.Vertical;
        foreach(Forms.ToolStripItem item in Items){int row=item.Name=="TrayHeader"?S(30):item is Forms.ToolStripSeparator?S(11):Math.Max(S(34),Math.Max(Font.Height,ImageScalingSize.Height)+S(12));item.AutoSize=false;item.Margin=Forms.Padding.Empty;item.Size=new Size(width-(item is Forms.ToolStripMenuItem?2:Padding.Horizontal),row);height+=row;}
        Size=new Size(width,height);
      }finally{ResumeLayout(true);arranging=false;}
    }
    internal void ApplyTheme(OperatorTheme theme,System.Windows.ResourceDictionary resources,bool? highContrast=null) {
      themedRenderer.Palette=TrayMenuPalette.Create(theme,resources,highContrast??Forms.SystemInformation.HighContrast);
      BackColor=Palette.Background;ForeColor=Palette.Text;Reflow();Invalidate();
    }
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){iconSlot.Dispose();menuFont.Dispose();headerFont.Dispose();}}
  }

  partial class Widget {
    TrayMenu CreateTrayMenu() {
      var menu=new TrayMenu();var header=menu.Items[0];menu.Items.RemoveAt(0);
      menu.Items.Add(L.T("Show widget"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>ShowWidget()))).Name="TrayVisibility";
      menu.Items.Add(L.T("Settings"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>Settings()))).Name="TraySettings";
      menu.Items.Add(L.T("Close"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>Exit()))).Name="TrayClose";
      menu.Items.Insert(1,new Forms.ToolStripMenuItem(L.T("Widget menu"),null,(s,e)=>window.Dispatcher.Invoke(new Action(()=>ShowMenu()))){Name="TrayWidgetMenu"});
      BuildTrayPersonalization(menu);BuildGameTrayControl(menu);menu.Items.Insert(0,header);menu.ApplyTheme(currentTheme,window.Resources);UpdateTrayShortcuts(menu);
      menu.Opening+=(s,e)=>{UpdateTrayPersonalization();UpdateTrayShortcuts(menu);menu.ApplyTheme(currentTheme,window.Resources);};return menu;
    }
    void UpdateTrayTheme(){var menu=tray==null?null:tray.ContextMenuStrip as TrayMenu;if(menu!=null)menu.ApplyTheme(currentTheme,window.Resources);}
    void UpdateTrayShortcuts(Forms.ContextMenuStrip target=null) {
      var menu=target??(tray==null?null:tray.ContextMenuStrip);if(menu==null)return;
      foreach(Forms.ToolStripItem item in menu.Items){var action=item as Forms.ToolStripMenuItem;if(action==null)continue;
        if(item.Name=="TrayVisibility")action.ShortcutKeyDisplayString=globalToggleKey?config.HotkeyToggle:"";
        else if(item.Name=="TrayWidgetMenu")action.ShortcutKeyDisplayString=globalMenuKey?config.HotkeyMenu:"";
        else if(item.Name=="TrayPlay")action.ShortcutKeyDisplayString=shortcuts!=null && shortcuts.Active.ContainsKey(PlayHotkey)?config.HotkeyPlay:"";
      }
      var themed=menu as TrayMenu;if(themed!=null)themed.Reflow();
    }
  }
}
