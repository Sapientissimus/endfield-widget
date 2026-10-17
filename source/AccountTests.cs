using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EndfieldWidget {
  static class AccountTests {
    const string DummyToken="ZHVtbXktd2lkZ2V0LXRlc3QtdG9rZW4=";
    static void Require(bool condition,string label) {if(!condition)throw new Exception(label);}
    static HttpResponseMessage Reply(int status,string body) {return new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(body,Encoding.UTF8,"application/json")};}
    class Handler : HttpMessageHandler {
      public Func<HttpRequestMessage,Task<HttpResponseMessage>> Respond;
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {return Respond(request);}
    }
    class SessionFixture {
      public bool MissingSalt,MissingUserId,ExpiredKey,WrongTime,RepeatedExpiration;
      public int Grants,Exchanges,Refreshes,Bindings;
      string activeSalt="dummy-original-key";
      long clockOffset;
      long Timestamp {get{return DateTimeOffset.UtcNow.ToUnixTimeSeconds()+clockOffset;}}
      void CheckSignature(HttpRequestMessage request) {
        Require(request.Headers.GetValues("cred").Single()=="dummy-cred","Session credential is sent to SKPORT");
        string time=request.Headers.GetValues("timestamp").Single();
        Require(Math.Abs(long.Parse(time,CultureInfo.InvariantCulture)-Timestamp)<5,"Request uses calibrated server time");
        string query=request.RequestUri.Query.TrimStart('?');
        Require(request.Headers.GetValues("sign").Single()==SkportClient.Sign(request.RequestUri.AbsolutePath,query,time,activeSalt),"Request uses the current session key");
      }
      public async Task<HttpResponseMessage> Respond(HttpRequestMessage request) {
        string path=request.RequestUri.AbsolutePath;
        switch(path) {
          case "/user/oauth2/v2/grant":
            Grants++;Require(!request.Headers.Contains("cred"),"SKPORT credentials never go to Gryphline");
            var grant=Json.Decode(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            Require(Json.Str(grant,"appCode")=="6eb76d4e13aa36e6" && Json.Num(grant,"type")==0,"Official OAuth application and grant type");
            return Reply(200,"{\"status\":0,\"data\":{\"code\":\"dummy-grant\"}}");
          case "/web/v1/user/auth/generate_cred_by_code":
            Exchanges++;activeSalt="dummy-original-key";
            var body=Json.Decode(await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            Require(Json.Str(body,"code")=="dummy-grant" && Json.Num(body,"kind")==1,"OAuth code is exchanged for a web session");
            var data=new Dictionary<string,object>{{"cred","dummy-cred"}};
            if(!MissingSalt)data.Add("token",activeSalt);
            if(!MissingUserId)data.Add("userId","dummy-skport-user");
            return Reply(200,Json.Encode(new {code=0,timestamp=Timestamp.ToString(CultureInfo.InvariantCulture),data=data}));
          case "/web/v1/auth/refresh":
            Refreshes++;Require(Refreshes<=2,"Session refresh is bounded");
            Require(request.Headers.GetValues("cred").Single()=="dummy-cred" && !request.Headers.Contains("sign"),"Refresh sends cred without depending on an expired signing key");
            activeSalt="dummy-refreshed-key";
            return Reply(200,Json.Encode(new {code=0,timestamp=Timestamp.ToString(CultureInfo.InvariantCulture),data=new {token=activeSalt}}));
          case "/web/v2/user":
            CheckSignature(request);
            return Reply(200,"{\"code\":0,\"data\":{\"user\":{\"basicUser\":{\"id\":\"dummy-skport-user\"}}}}");
          case "/api/v1/game/player/binding":
            Bindings++;Require(Bindings<=3,"Failed role requests cannot retry indefinitely");CheckSignature(request);
            if(RepeatedExpiration || (ExpiredKey && Bindings==1))return Reply(401,"{\"code\":10000,\"message\":\"dummy-secret-response\"}");
            if(WrongTime && Bindings==1){clockOffset=3600;return Reply(401,Json.Encode(new {code=10003,timestamp=Timestamp.ToString(CultureInfo.InvariantCulture)}));}
            return Reply(200,"{\"code\":0,\"data\":{\"list\":[{\"appCode\":\"endfield\",\"bindingList\":[{\"uid\":\"dummy-game-uid\",\"roles\":[{\"roleId\":\"dummy-role\",\"serverId\":\"2\",\"nickname\":\"Fixture\"}]}]}]}}");
          case "/api/v1/game/endfield/card/detail":
            CheckSignature(request);
            Require(request.RequestUri.Query=="?roleId=dummy-role&serverId=2&userId=dummy-skport-user","Card query uses the SKPORT user ID");
            Require(request.Headers.GetValues("sk-game-role").Single()=="3_dummy-role_2","Card query uses the chosen role");
            return Reply(200,Json.Encode(new {code=0,timestamp=Timestamp.ToString(CultureInfo.InvariantCulture),data=new {detail=new {dungeon=new {curStamina="100",maxStamina="240",maxTs=(Timestamp+60000).ToString(CultureInfo.InvariantCulture)},@base=new {name="Fixture"}}}}));
          default:throw new Exception("Unexpected endpoint: "+path);
        }
      }
    }
    static async Task Success(SessionFixture fixture) {
      using(var handler=new Handler{Respond=fixture.Respond})using(var http=new HttpClient(handler)) {
        var client=new SkportClient(http);var sanity=await client.Fetch(DummyToken,null).ConfigureAwait(false);
        Require(sanity.Current==100 && sanity.Maximum==240,"Authenticated Sanity data is parsed");
        await client.Roles(DummyToken).ConfigureAwait(false);
        Require(fixture.Grants==1 && fixture.Exchanges==1,"An established session is reused");
      }
    }
    static async Task<SkportException> Failure(int status,string payload) {
      using(var handler=new Handler{Respond=request=>Task.FromResult(Reply(status,payload))})using(var http=new HttpClient(handler)) {
        try{await new SkportClient(http).Roles(DummyToken).ConfigureAwait(false);}
        catch(SkportException ex){return ex;}
        throw new Exception("A rejected response was accepted");
      }
    }
    static async Task RunOffline(List<string> report) {
      var missing=new SessionFixture{MissingSalt=true,MissingUserId=true};await Success(missing).ConfigureAwait(false);
      Require(missing.Refreshes==1,"Missing session key is obtained from refresh");report.Add("PASS: Login succeeds when credential exchange provides cred only; refresh and profile lookup complete the session");
      var direct=new SessionFixture();await Success(direct).ConfigureAwait(false);Require(direct.Refreshes==0,"A supplied valid key is reused");report.Add("PASS: Existing credential exchange format still works without an unnecessary refresh");
      var expiry=new SessionFixture{ExpiredKey=true};await Success(expiry).ConfigureAwait(false);Require(expiry.Refreshes==1,"An expired signing key is refreshed once");report.Add("PASS: Expired signing key refreshes automatically and retries the rejected request");
      var skew=new SessionFixture{WrongTime=true};await Success(skew).ConfigureAwait(false);Require(skew.Refreshes==0,"Clock correction does not replace valid credentials");report.Add("PASS: Server timestamp corrects clock skew before one retry");
      var repeated=new SessionFixture{RepeatedExpiration=true};
      using(var handler=new Handler{Respond=repeated.Respond})using(var http=new HttpClient(handler)) {
        SkportException error=null;try{await new SkportClient(http).Roles(DummyToken).ConfigureAwait(false);}catch(SkportException ex){error=ex;}
        Require(error!=null && error.Stage==SkportStage.Characters && error.ApiCode==10000 && repeated.Refreshes==1 && repeated.Bindings==2,"Repeated expiry stops with the original failing stage");
      }
      report.Add("PASS: Repeated session rejection stops after one refresh and reports the failing step");
      var grantError=await Failure(401,"{\"status\":3,\"msg\":\""+DummyToken+"\"}").ConfigureAwait(false);
      Require(grantError.Stage==SkportStage.Login && grantError.HttpStatus==401 && grantError.ApiCode==3,"Gryphline status is preserved");
      Require(!grantError.Message.Contains(DummyToken) && !SkportFailure.Diagnostic(grantError).Contains(DummyToken),"Messages and diagnostic data exclude the supplied token and response text");
      report.Add("PASS: Token rejection retains HTTP/status codes and never exposes token or server response text");
      var apiError=await Failure(200,"{\"code\":777,\"status\":0,\"data\":{\"code\":\"should-not-be-used\"}}").ConfigureAwait(false);
      Require(apiError.ApiCode==777,"An API error is not hidden by a different zero-valued field");report.Add("PASS: API code errors are rejected even on HTTP 200");
      var unavailable=await Failure(500,"{\"code\":10001,\"message\":\"dummy-secret-response\"}").ConfigureAwait(false);
      Require(unavailable.Message.Contains("temporarily unavailable") && !unavailable.Message.Contains("token"),"Service failures are not blamed on the token");report.Add("PASS: Service outages show a separate actionable error");
      var malformed=await Failure(200,"<html>dummy-secret-response</html>").ConfigureAwait(false);
      Require(malformed.Problem==SkportProblem.Response && !malformed.Message.Contains("dummy-secret-response"),"Malformed responses are reported safely");report.Add("PASS: Malformed service responses report the step without exposing response contents");
      using(var handler=new Handler{Respond=request=>{var task=new TaskCompletionSource<HttpResponseMessage>();task.SetException(new HttpRequestException("private-token="+DummyToken));return task.Task;}})using(var http=new HttpClient(handler)) {
        SkportException error=null;try{await new SkportClient(http).Roles(DummyToken).ConfigureAwait(false);}catch(SkportException ex){error=ex;}
        Require(error!=null && error.Problem==SkportProblem.Network && !error.Message.Contains(DummyToken),"Network failure is distinct and safe");
      }
      report.Add("PASS: Network errors are distinct from login errors and do not expose exception messages");
      var changed=new SessionFixture();using(var handler=new Handler{Respond=changed.Respond})using(var http=new HttpClient(handler)) {
        var client=new SkportClient(http);await client.Roles(DummyToken).ConfigureAwait(false);await client.Roles("dummy-second-account").ConfigureAwait(false);
        Require(changed.Grants==2 && changed.Exchanges==2,"Changing token cannot reuse another account's session");
      }
      report.Add("PASS: Changing the account token creates a new session");
      var saving=new IOException("private-token="+DummyToken);Require(SkportFailure.Describe(saving).Contains("save") && !SkportFailure.Diagnostic(saving).Contains(DummyToken),"Saving errors remain separate from login errors");
      report.Add("PASS: Saving failures are distinct and diagnostics contain only stage, numeric codes, type, and time");
    }
    static async Task RunProbe(List<string> report) {
      using(var http=Widget.NewHttp()) {
        SkportException error=null;try{await new SkportClient(http).Roles(DummyToken).ConfigureAwait(false);}catch(SkportException ex){error=ex;}
        Require(error!=null && error.Stage==SkportStage.Login && error.HttpStatus==401 && error.ApiCode==3,"Live dummy login must reach Gryphline and be rejected as an expired/invalid login");
        report.Add("PASS: The Windows executable reaches the live Gryphline login API and correctly reads HTTP 401 / status 3 for a deliberately invalid dummy token");
      }
    }
    public static void Run(string reportPath,bool liveProbe) {
      var report=new List<string>();
      try {
        if(liveProbe)RunProbe(report).GetAwaiter().GetResult();else RunOffline(report).GetAwaiter().GetResult();
        report.Add("ALL CHECKS PASSED; dummy data only, no user token, clipboard, browser session, or user profile read.");
      }catch(Exception error){report.Add("FAILED: "+error.Message);Environment.ExitCode=1;}
      File.WriteAllLines(reportPath,report,Encoding.UTF8);
    }
  }
}
