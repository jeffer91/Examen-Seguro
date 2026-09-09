using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Itsqmet.ExamAgent;

public sealed class AgentRuntime : IDisposable
{
    private readonly AgentConfig _config;
    private readonly string _root;
    private readonly string _statePath;
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private SessionState? _session;
    private List<RuleDto> _rules = [];
    private string? _lastProcess;
    private HttpListener? _listener;
    private CancellationTokenSource _cts = new();

    public AgentRuntime(AgentConfig config, string root)
    {
        _config = config; _root = root; _statePath = Path.Combine(root, "current-session.json");
        _http = new HttpClient { BaseAddress = new Uri(config.ServerUrl), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("X-ITSQMET-Key", config.AgentKey);
    }

    public async Task RunAsync()
    {
        await RegisterDevice();
        await RestoreSession();
        _ = Task.Run(() => BrowserBridge(_cts.Token));
        var poll = Task.Run(() => PollAssignments(_cts.Token));
        var monitor = Task.Run(() => MonitorForeground(_cts.Token));
        var heartbeat = Task.Run(() => Heartbeat(_cts.Token));
        await Task.WhenAll(poll, monitor, heartbeat);
    }

    private async Task RegisterDevice()
    {
        var body = new { deviceCode=_config.DeviceCode, hostname=Environment.MachineName, windowsVersion=Environment.OSVersion.VersionString, agentVersion="0.1.0" };
        try { using var r=await _http.PostAsJsonAsync("api/agent/register", body); r.EnsureSuccessStatusCode(); } catch { }
    }

    private async Task RestoreSession()
    {
        if (!File.Exists(_statePath)) return;
        try { _session = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(_statePath), _json); if (_session is not null) await LoadRules(_session.ExamId); } catch { _session = null; }
    }

    private async Task PollAssignments(CancellationToken ct)
    {
        var misses=0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var r=await _http.GetAsync($"api/agent/assignment/{Uri.EscapeDataString(_config.DeviceCode)}",ct);
                if (r.StatusCode==HttpStatusCode.NoContent)
                {
                    if(_session is not null && ++misses>=2) await EndLocalSession();
                }
                else if(r.IsSuccessStatusCode)
                {
                    misses=0; var a=await r.Content.ReadFromJsonAsync<AssignmentDto>(_json,ct);
                    if(a is not null && _session is null && a.Status=="armed") await StartSession(a,ct);
                }
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(2,_config.PollSeconds)),ct);
        }
    }

    private async Task StartSession(AssignmentDto a, CancellationToken ct)
    {
        try
        {
            using var r=await _http.PostAsJsonAsync("api/agent/session/start",new{assignmentId=a.AssignmentId,deviceCode=_config.DeviceCode},ct);r.EnsureSuccessStatusCode();var started=await r.Content.ReadFromJsonAsync<StartSessionResponse>(_json,ct);if(started is null)return;
            _session=new SessionState(a.AssignmentId,a.ExamId,started.SessionId,a.StudentCode,a.StudentName,DateTimeOffset.UtcNow);await File.WriteAllTextAsync(_statePath,JsonSerializer.Serialize(_session,_json),ct);await LoadRules(a.ExamId);
            await SendEvent(new EventPayload{EventType="session_started",Severity="info",Metadata=new(){{"hostname",Environment.MachineName}}},false,ct);
        }
        catch { }
    }

    private async Task EndLocalSession()
    {
        _session=null;_rules=[];_lastProcess=null;try{if(File.Exists(_statePath))File.Delete(_statePath);}catch{}await Task.CompletedTask;
    }

    private async Task LoadRules(Guid examId)
    {
        try { _rules = await _http.GetFromJsonAsync<List<RuleDto>>($"api/rules?examId={examId}",_json) ?? []; } catch { _rules=[]; }
    }

    private async Task MonitorForeground(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            if(_session is not null)
            {
                var p=WindowsMonitor.ForegroundProcess();
                if(!string.IsNullOrWhiteSpace(p)&&!string.Equals(p,_lastProcess,StringComparison.OrdinalIgnoreCase))
                {
                    _lastProcess=p;
                    await SendEvent(new EventPayload{EventType="foreground_changed",Severity="info",ProcessName=p},false,ct);
                    var matches=_rules.Where(x=>x.Enabled&&x.Kind=="process"&&Match(p,x.Pattern)).ToList();
                    foreach(var rule in matches) await SendEvent(new EventPayload{EventType="process_incident",Severity=rule.Severity,ProcessName=p,Metadata=new(){{"rule",rule.Label}}},rule.CaptureOnMatch,ct);
                }
            }
            await Task.Delay(900,ct);
        }
    }

    private async Task Heartbeat(CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            if(_session is not null)
            {
                try { using var r=await _http.PostAsJsonAsync($"api/agent/session/{_session.SessionId}/heartbeat",new{status="online"},ct); } catch { }
            }
            await Task.Delay(TimeSpan.FromSeconds(5),ct);
        }
    }

    private async Task BrowserBridge(CancellationToken ct)
    {
        try
        {
            _listener=new HttpListener();_listener.Prefixes.Add("http://127.0.0.1:43119/");_listener.Start();
            while(!ct.IsCancellationRequested)
            {
                var context=await _listener.GetContextAsync();
                _=Task.Run(async()=>
                {
                    try
                    {
                        if(context.Request.HttpMethod=="OPTIONS"){AddCors(context.Response);context.Response.StatusCode=204;context.Response.Close();return;}
                        if(context.Request.Url?.AbsolutePath!="/browser-event"||context.Request.HttpMethod!="POST"){context.Response.StatusCode=404;context.Response.Close();return;}
                        using var sr=new StreamReader(context.Request.InputStream,Encoding.UTF8);var evt=JsonSerializer.Deserialize<BrowserEvent>(await sr.ReadToEndAsync(),_json);AddCors(context.Response);context.Response.StatusCode=204;context.Response.Close();if(evt is not null)await HandleBrowser(evt,ct);
                    }catch{try{context.Response.Close();}catch{}}
                },ct);
            }
        }
        catch { }
    }

    private static void AddCors(HttpListenerResponse r){r.Headers["Access-Control-Allow-Origin"]="*";r.Headers["Access-Control-Allow-Headers"]="content-type";r.Headers["Access-Control-Allow-Methods"]="POST, OPTIONS";}

    private async Task HandleBrowser(BrowserEvent evt,CancellationToken ct)
    {
        if(_session is null)return;
        if(!Uri.TryCreate(evt.Url,UriKind.Absolute,out var u)||!(u.Scheme is "http" or "https"))return;
        var safeUrl=$"{u.Scheme}://{u.Host}{u.AbsolutePath}";
        await SendEvent(new EventPayload{EventType="browser_navigation",Severity="info",Domain=u.Host,Url=safeUrl,Metadata=new(){{"browser",evt.Browser}}},false,ct);
        var rules=_rules.Where(x=>x.Enabled&&x.Kind=="domain"&&DomainMatch(u.Host,x.Pattern)).ToList();
        foreach(var rule in rules) await SendEvent(new EventPayload{EventType="navigation_incident",Severity=rule.Severity,Domain=u.Host,Url=safeUrl,Metadata=new(){{"browser",evt.Browser},{"rule",rule.Label}}},rule.CaptureOnMatch,ct);
    }

    private async Task SendEvent(EventPayload payload,bool screenshot,CancellationToken ct)
    {
        if(_session is null)return;
        if(screenshot)
        {
            var s=WindowsMonitor.CaptureJpeg();if(s is not null){payload.ScreenshotBase64=s.Value.Base64;payload.ScreenshotContentType="image/jpeg";payload.ScreenshotWidth=s.Value.Width;payload.ScreenshotHeight=s.Value.Height;}
        }
        try{using var r=await _http.PostAsJsonAsync($"api/agent/session/{_session.SessionId}/event",payload,_json,ct);}catch{await Queue(payload);}
    }

    private async Task Queue(EventPayload p)
    {
        try{var q=Path.Combine(_root,"offline-events.ndjson");await File.AppendAllTextAsync(q,JsonSerializer.Serialize(new{sessionId=_session?.SessionId,payload=p},_json)+Environment.NewLine);}catch{}
    }

    private static bool Match(string value,string pattern)=>value.Contains(pattern,StringComparison.OrdinalIgnoreCase);
    private static bool DomainMatch(string host,string pattern)=>host.Equals(pattern,StringComparison.OrdinalIgnoreCase)||host.EndsWith("."+pattern,StringComparison.OrdinalIgnoreCase);
    public void Dispose(){_cts.Cancel();try{_listener?.Stop();}catch{} _listener?.Close();_http.Dispose();_cts.Dispose();}
}
