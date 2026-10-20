using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Interop;

namespace EndfieldWidget {
  sealed class ShortcutSpec {
    public int Id;public string Text;public uint Modifiers,Key;
    public static bool TryCreate(int id,string text,out ShortcutSpec shortcut) {
      shortcut=null;if(string.IsNullOrWhiteSpace(text)){shortcut=new ShortcutSpec{Id=id,Text=""};return true;}
      try {
        var gesture=(KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(text);
        if(gesture==null || gesture.Key==System.Windows.Input.Key.None || gesture.Key==System.Windows.Input.Key.System || gesture.Key==System.Windows.Input.Key.Escape)return false;
        int key=KeyInterop.VirtualKeyFromKey(gesture.Key);if(key==0 || gesture.Modifiers==ModifierKeys.None && !(gesture.Key>=System.Windows.Input.Key.F1 && gesture.Key<=System.Windows.Input.Key.F24))return false;
        shortcut=new ShortcutSpec {Id=id,Text=gesture.GetDisplayStringForCulture(System.Globalization.CultureInfo.InvariantCulture),Modifiers=(uint)gesture.Modifiers,Key=(uint)key};return true;
      }catch{return false;}
    }
  }
  sealed class ShortcutRegistry {
    readonly Func<int,uint,uint,bool> register;readonly Action<int> unregister;
    public readonly Dictionary<int,ShortcutSpec> Active=new Dictionary<int,ShortcutSpec>();
    public readonly List<ShortcutSpec> Unavailable=new List<ShortcutSpec>();
    public ShortcutSpec Failed {get;private set;}
    public ShortcutRegistry(Func<int,uint,uint,bool> add,Action<int> remove){register=add;unregister=remove;}
    public static bool Validate(IEnumerable<ShortcutSpec> specs){var keys=new HashSet<string>();return specs.All(s=>s.Key==0 || keys.Add(s.Modifiers+":"+s.Key));}
    public void Clear(){foreach(int id in Active.Keys.ToArray())unregister(id);Active.Clear();}
    public void Initial(IEnumerable<ShortcutSpec> specs){Clear();Unavailable.Clear();foreach(var s in specs)if(s.Key!=0){if(register(s.Id,s.Modifiers|0x4000,s.Key))Active[s.Id]=s;else Unavailable.Add(s);}}
    public bool Replace(IEnumerable<ShortcutSpec> specs) {
      Failed=null;var requested=specs.ToList();if(!Validate(requested))return false;
      var previous=Active.Values.ToList();Clear();
      foreach(var s in requested)if(s.Key!=0) {
        if(!register(s.Id,s.Modifiers|0x4000,s.Key)){Failed=s;Clear();Unavailable.Clear();foreach(var old in previous){if(register(old.Id,old.Modifiers|0x4000,old.Key))Active[old.Id]=old;else Unavailable.Add(old);}return false;}
        Active[s.Id]=s;
      }
      Unavailable.Clear();return true;
    }
  }
  partial class Widget {
    const int PlayHotkey=0x4e03,ModeHotkey=0x4e04,RefreshHotkey=0x4e05;
    ShortcutRegistry shortcuts;
    string[] ShortcutValues(){return new[]{config.HotkeyToggle,config.HotkeyMenu,config.HotkeyPlay,config.HotkeyMode,config.HotkeyRefresh};}
    static readonly int[] ShortcutIds={ToggleHotkey,MenuHotkey,PlayHotkey,ModeHotkey,RefreshHotkey};
    static string[] ShortcutLabels {get{return new[]{L.T("Show / hide"),L.T("Widget menu"),L.T("Launch / open game"),L.T("Switch mode"),L.T("Refresh data")};}}
    string ShortcutLabel(int id){int index=Array.IndexOf(ShortcutIds,id);return index<0?L.T("Action"):ShortcutLabels[index];}
    List<ShortcutSpec> ParseShortcuts(string[] values) {
      var specs=new List<ShortcutSpec>();for(int i=0;i<ShortcutIds.Length;i++){ShortcutSpec spec;if(!ShortcutSpec.TryCreate(ShortcutIds[i],values[i],out spec))return null;specs.Add(spec);}return ShortcutRegistry.Validate(specs)?specs:null;
    }
    void InitializeConfiguredHotkeys() {
      IntPtr handle=keyboardSource==null?IntPtr.Zero:keyboardSource.Handle;
      shortcuts=new ShortcutRegistry((id,mods,key)=>preview || Storage.Isolated || RegisterHotKey(handle,id,mods,key),id=>{if(!preview && !Storage.Isolated)UnregisterHotKey(handle,id);});
      var specs=ParseShortcuts(ShortcutValues());if(specs==null)specs=ParseShortcuts(new[]{"Ctrl+Alt+H","Ctrl+Alt+E","Ctrl+Alt+G","Ctrl+Alt+M","Ctrl+Alt+R"});
      shortcuts.Initial(specs);UpdateShortcutFlags();
    }
    void UpdateShortcutFlags(){globalToggleKey=shortcuts!=null && shortcuts.Active.ContainsKey(ToggleHotkey);globalMenuKey=shortcuts!=null && shortcuts.Active.ContainsKey(MenuHotkey);UpdateTrayShortcuts();UpdateMenu();}
    bool SaveShortcuts(string[] values,out string feedback) {
      if(values==null || values.Length!=ShortcutIds.Length){feedback=L.T("Check the shortcut list.");return false;}
      var specs=new List<ShortcutSpec>();
      for(int i=0;i<values.Length;i++) {
        ShortcutSpec spec;if(!ShortcutSpec.TryCreate(ShortcutIds[i],values[i],out spec)){feedback="«"+ShortcutLabels[i]+L.T("”: invalid shortcut.");return false;}
        var same=specs.FirstOrDefault(s=>spec.Key!=0 && s.Key==spec.Key && s.Modifiers==spec.Modifiers);
        if(same!=null){feedback=L.T("Shortcut ")+spec.Text+L.T(" is used twice: “")+ShortcutLabel(same.Id)+L.T("” and “")+ShortcutLabels[i]+"».";return false;}specs.Add(spec);
      }
      if(shortcuts==null)InitializeConfiguredHotkeys();
      if(!shortcuts.Replace(specs)){UpdateShortcutFlags();var failed=shortcuts.Failed;feedback=failed==null?L.T("Could not change shortcuts."):L.T("Shortcut ")+failed.Text+L.T(" for “")+ShortcutLabel(failed.Id)+L.T("” is used by another application.");feedback+=shortcuts.Unavailable.Count==0?L.T(" Previous shortcuts were kept."):L.T(" Check the unavailable shortcuts.");return false;}
      config.HotkeyToggle=specs[0].Text;config.HotkeyMenu=specs[1].Text;config.HotkeyPlay=specs[2].Text;config.HotkeyMode=specs[3].Text;config.HotkeyRefresh=specs[4].Text;UpdateShortcutFlags();SavePosition();feedback=L.T("Shortcuts saved.");return true;
    }
    string UnavailableShortcuts(){return shortcuts==null?"":string.Join("\n",shortcuts.Unavailable.Select(s=>s.Text+" · "+ShortcutLabel(s.Id)+L.T(" — unavailable")));}
    async void DispatchShortcut(int id) {
      if(id==ToggleHotkey)ToggleWidget();else if(id==MenuHotkey)ShowMenu();else if(id==PlayHotkey)QueueGameLaunch();
      else if(id==ModeHotkey)SetWidgetMode(config.MicroMode?"full":config.Compact?"micro":"compact");else if(id==RefreshHotkey)await Refresh(true);
    }
  }
}
