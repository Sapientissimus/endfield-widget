using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EndfieldWidget {
  static class GameProcessSampler {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Entry {
      public uint Size,Usage,Id;
      public UIntPtr Heap;
      public uint Module,Threads,Parent;
      public int Priority;
      public uint Flags;
      [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Executable;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags,uint process);
    [DllImport("kernel32.dll",EntryPoint="Process32FirstW",SetLastError=true)] static extern bool First(SafeFileHandle snapshot,IntPtr entry);
    [DllImport("kernel32.dll",EntryPoint="Process32NextW",SetLastError=true)] static extern bool Next(SafeFileHandle snapshot,IntPtr entry);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ProcessIdToSessionId(uint process,out uint session);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll",SetLastError=true)] static extern SafeFileHandle OpenProcess(uint access,bool inherit,uint process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessTimes(SafeFileHandle process,out long created,out long exited,out long kernel,out long user);
    static readonly int EntrySize=Marshal.SizeOf(typeof(Entry));
    static readonly int NameOffset=Marshal.OffsetOf(typeof(Entry),"Executable").ToInt32();
    static readonly int IdOffset=Marshal.OffsetOf(typeof(Entry),"Id").ToInt32();
    static bool Matches(IntPtr entry,string name) {
      for(int i=0;i<name.Length;i++)if(char.ToUpperInvariant((char)Marshal.ReadInt16(entry,NameOffset+i*2))!=char.ToUpperInvariant(name[i]))return false;
      return Marshal.ReadInt16(entry,NameOffset+name.Length*2)==0;
    }
    public static GameObservation Sample(string executable) {
      var result=new GameObservation();IntPtr entry=IntPtr.Zero;
      if(string.IsNullOrEmpty(executable) || executable.Length>=260)return result;
      try {
        uint session;if(!ProcessIdToSessionId(GetCurrentProcessId(),out session))return result;
        using(var snapshot=CreateToolhelp32Snapshot(2,0)) {
          if(snapshot.IsInvalid)return result;
          entry=Marshal.AllocHGlobal(EntrySize);Marshal.WriteInt32(entry,EntrySize);
          bool have=First(snapshot,entry),unknown=false,found=false;
          while(have) {
            if(Matches(entry,executable)) {
              uint id=unchecked((uint)Marshal.ReadInt32(entry,IdOffset)),owner;
              if(!ProcessIdToSessionId(id,out owner))unknown=true;
              else if(owner==session) {
                found=true;
                using(var process=OpenProcess(0x1000,false,id)) {
                  long created,exited,kernel,user;
                  if(!process.IsInvalid && GetProcessTimes(process,out created,out exited,out kernel,out user)) {
                    var start=new DateTimeOffset(DateTime.FromFileTimeUtc(created));
                    if(!result.Started.HasValue || start<result.Started)result.Started=start;
                  }
                }
              }
            }
            have=Next(snapshot,entry);
          }
          if(Marshal.GetLastWin32Error()!=18)unknown=true;
          result.Running=found?(bool?)true:unknown?null:(bool?)false;
        }
      }catch{result.Running=null;}
      finally{if(entry!=IntPtr.Zero)Marshal.FreeHGlobal(entry);}
      return result;
    }
  }
}
