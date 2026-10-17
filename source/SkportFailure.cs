using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace EndfieldWidget {
  enum SkportStage { Login, Session, Refresh, Profile, Characters, Sanity }
  enum SkportProblem { Rejected, Network, Timeout, Response, MissingCharacters }
  class SkportException : InvalidOperationException {
    public readonly SkportStage Stage;
    public readonly SkportProblem Problem;
    public readonly int HttpStatus;
    public readonly long ApiCode;
    public SkportException(SkportStage stage,SkportProblem problem,int httpStatus=0,long apiCode=-1)
      : base(SkportFailure.Message(stage,problem,httpStatus,apiCode)) { Stage=stage;Problem=problem;HttpStatus=httpStatus;ApiCode=apiCode; }
  }
  static class SkportFailure {
    static string StageName(SkportStage stage) {
      switch(stage) {
        case SkportStage.Login:return L.T("login");
        case SkportStage.Session:return L.T("session");
        case SkportStage.Refresh:return L.T("session refresh");
        case SkportStage.Profile:return L.T("profile");
        case SkportStage.Characters:return L.T("character");
        default:return L.T("Sanity");
      }
    }
    public static string Message(SkportStage stage,SkportProblem problem,int httpStatus,long apiCode) {
      if(problem==SkportProblem.Network)return L.T("Cannot reach SKPORT. Check your internet connection.");
      if(problem==SkportProblem.Timeout)return L.T("SKPORT did not respond in time. Try again.");
      if(problem==SkportProblem.MissingCharacters)return L.T("No Endfield character in SKPORT. Check your game link on the website.");
      string message;
      if(httpStatus>=500 || httpStatus==429)message=L.T("SKPORT is temporarily unavailable. Try later.");
      else if(problem==SkportProblem.Response)message=L.T("SKPORT returned an unexpected response.");
      else if(stage==SkportStage.Login && (httpStatus==401 || apiCode==3))message=L.T("SKPORT rejected the token. Reload the token page and copy a fresh token.");
      else if(stage==SkportStage.Login && httpStatus==400)message=L.T("SKPORT did not accept the token format. Copy the token value or the entire response.");
      else if(httpStatus==403)message=L.T("SKPORT rejected the connection. Open the website and check your login.");
      else if(apiCode==10003)message=L.T("SKPORT rejected the request time. Check the Windows date and time.");
      else if(httpStatus==401 || apiCode==10000)message=L.T("Could not restore the SKPORT session. Try connecting again.");
      else message=L.T("SKPORT could not finish connecting. Try again.");
      string code=httpStatus>0?httpStatus.ToString(CultureInfo.InvariantCulture):"";
      if(apiCode>=0)code+=(code.Length>0?"/":"")+apiCode.ToString(CultureInfo.InvariantCulture);
      return message+" ("+StageName(stage)+(code.Length>0?": "+code:"")+")";
    }
    public static string Describe(Exception error) {
      var failure=error as SkportException;
      if(failure!=null)return failure.Message;
      if(error is TaskCanceledException)return L.T("SKPORT did not respond in time. Try again.");
      if(error is HttpRequestException)return L.T("Cannot reach SKPORT. Check your internet connection.");
      if(error is IOException || error is UnauthorizedAccessException)return L.T("Could not save the connection. Check access to the widget folder.");
      return L.T("Could not finish connecting. Try again.");
    }
    public static string Diagnostic(Exception error) {
      var failure=error as SkportException;
      return "UTC="+DateTimeOffset.UtcNow.ToString("o",CultureInfo.InvariantCulture)+"\nType="+error.GetType().Name+
        (failure==null?"":"\nStage="+failure.Stage+"\nProblem="+failure.Problem+"\nHTTP="+failure.HttpStatus+"\nAPI="+failure.ApiCode)+"\n";
    }
    public static void Record(Exception error) {
      try{Directory.CreateDirectory(Storage.DirectoryPath);File.WriteAllText(Path.Combine(Storage.DirectoryPath,"skport-error.log"),Diagnostic(error),Encoding.UTF8);}catch{}
    }
  }
}
