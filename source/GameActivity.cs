using System;
using System.Diagnostics;

namespace EndfieldWidget {
  sealed class GameObservation {
    public bool? Running;
    public DateTimeOffset? Started;
  }
  sealed class GameActivity {
    public bool Running {get;private set;}
    int missing;
    public bool Observe(bool? alive) {
      if(!alive.HasValue){missing=0;return false;}
      if(alive.Value){Running=true;missing=0;return false;}
      if(!Running)return false;
      if(++missing<2)return false;
      Running=false;missing=0;return true;
    }
    public static bool? Detect(){return Sample().Running;}
    public static GameObservation Sample(){return GameProcessSampler.Sample("Endfield.exe");}
  }
}
