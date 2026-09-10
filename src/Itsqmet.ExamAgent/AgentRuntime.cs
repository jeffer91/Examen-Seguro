using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Itsqmet.ExamAgent;

public sealed class AgentRuntime : IDisposable
{
    private readonly AgentConfig _config;
    private readonly string _root;
    private readonly string _statePath;
    private readonly string _offlinePath;
    private readonly HttpClient _http;
    private readonly BrowserHistoryMonitor _history;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _lastFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _recentNavigation = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _navigationLock = new();
    private SessionState? _session;
    private List<RuleDto> _rules = [];
    private string? _lastProcess;
    private HttpListener? _listener;
    private bool _registered;
    private bool _serviceReportedStopped;

    public AgentRuntime(AgentConfig config, string root)
    {
        _config = config;
        _root = root;
        _statePath = Path.Combine(root, "current-session.json");
        _offlinePath = Path.Combine(root, "offline-events.ndjson");
        _history = new BrowserHistoryMonitor(root);
        _http = new HttpClient { BaseAddress = new Uri(config.ServerUrl), Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Add("X-ITSQMET-Key", config.AgentKey);
    }

    public async Task RunAsync()
    {
        _registered = await RegisterDevice();
        await RestoreSession();
        _ = Task.Run(() => BrowserBridge(_cts.Token));
        await Task.WhenAll(
            Task.Run(() => PollAssignments(_cts.Token)),
            Task.Run(() => MonitorForeground(_cts.Token)),
            Task.Run(() => MonitorFolders(_cts.Token)),
            Task.Run(() => MonitorBrowserHistory(_cts.Token)),
            Task.Run(() => MonitorService(_cts.Token)),
            Task.Run(() => Heartbeat(_cts.Token)),
            Task.Run(() => ReplayOffline(_cts.Token))
        );
    }

    private async Task<bool> RegisterDevice()
    {
        var body = new
        {
            deviceCode = _config.DeviceCode,
            hostname = Environment.MachineName,
            windowsVersion = Environment.OSVersion.VersionString,
            agentVersion = "0.2.1"
        };
        try
        {
            using var r = await _http.PostAsJsonAsync("api/agent/register", body);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task RestoreSession()
    {
        if (!File.Exists(_statePath)) return;
        try
        {
            _session = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(_statePath), _json);
            if (_session is not null) await LoadRules(_session.ExamId);
        }
        catch { _session = null; }
    }

    private async Task PollAssignments(CancellationToken ct)
    {
        var misses = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_registered) _registered = await RegisterDevice();
                if (_registered)
                {
                    using var r = await _http.GetAsync($"api/agent/assignment/{Uri.EscapeDataString(_config.DeviceCode)}", ct);
                    if (r.StatusCode == HttpStatusCode.NoContent)
                    {
                        if (_session is not null && ++misses >= 2) await EndLocalSession();
                    }
                    else if (r.IsSuccessStatusCode)
                    {
                        misses = 0;
                        var a = await r.Content.ReadFromJsonAsync<AssignmentDto>(_json, ct);
                        if (a is not null && _session is null && a.Status is "armed" or "active")
                            await StartSession(a, ct);
                    }
                    else if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        _registered = false;
                    }
                }
            }
            catch { }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, _config.PollSeconds)), ct);
        }
    }

    private async Task StartSession(AssignmentDto a, CancellationToken ct)
    {
        try
        {
            using var r = await _http.PostAsJsonAsync("api/agent/session/start", new { assignmentId = a.AssignmentId, deviceCode = _config.DeviceCode }, ct);
            r.EnsureSuccessStatusCode();
            var started = await r.Content.ReadFromJsonAsync<StartSessionResponse>(_json, ct);
            if (started is null) return;

            _session = new SessionState(a.AssignmentId, a.ExamId, started.SessionId, a.StudentCode, a.StudentName, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(_statePath, JsonSerializer.Serialize(_session, _json), ct);
            _lastProcess = null;
            _lastFolders.Clear();
            lock (_navigationLock) _recentNavigation.Clear();
            await LoadRules(a.ExamId);
            await SendEvent(new EventPayload
            {
                EventType = "session_started",
                Severity = "info",
                Metadata = new() { { "hostname", Environment.MachineName }, { "agentVersion", "0.2.1" } }
            }, false, ct);
        }
        catch { }
    }

    private async Task EndLocalSession()
    {
        _session = null;
        _rules = [];
        _lastProcess = null;
        _lastFolders.Clear();
        lock (_navigationLock) _recentNavigation.Clear();
        try { if (File.Exists(_statePath)) File.Delete(_statePath); } catch { }
        await Task.CompletedTask;
    }

    private async Task LoadRules(Guid examId)
    {
        try
        {
            _rules = await _http.GetFromJsonAsync<List<RuleDto>>($"api/rules?examId={examId}", _json) ?? [];
        }
        catch { _rules = []; }
    }

    private async Task MonitorForeground(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_session is not null)
            {
                var p = WindowsMonitor.ForegroundProcess();
                if (!string.IsNullOrWhiteSpace(p) && !string.Equals(p, _lastProcess, StringComparison.OrdinalIgnoreCase))
                {
                    var previous = _lastProcess;
                    _lastProcess = p;
                    await SendEvent(new EventPayload
                    {
                        EventType = "foreground_changed",
                        Severity = "info",
                        ProcessName = p,
                        Metadata = previous is null ? null : new() { { "previousProcess", previous } }
                    }, false, ct);

                    foreach (var rule in _rules.Where(x => x.Enabled && x.Kind == "process" && Match(p, x.Pattern)))
                    {
                        await SendEvent(new EventPayload
                        {
                            EventType = "process_incident",
                            Severity = rule.Severity,
                            ProcessName = p,
                            Metadata = new() { { "rule", rule.Label } }
                        }, rule.CaptureOnMatch, ct);
                    }

                    if (previous is not null)
                    {
                        foreach (var rule in _rules.Where(x => x.Enabled && x.Kind == "focus" && (x.Pattern == "*" || Match(p, x.Pattern))))
                        {
                            await SendEvent(new EventPayload
                            {
                                EventType = "focus_changed",
                                Severity = rule.Severity,
                                ProcessName = p,
                                Metadata = new() { { "rule", rule.Label }, { "previousProcess", previous } }
                            }, rule.CaptureOnMatch, ct);
                        }
                    }
                }
            }
            await Task.Delay(900, ct);
        }
    }

    private async Task MonitorFolders(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_session is not null)
            {
                var folders = WindowsMonitor.ExplorerFolders();
                foreach (var path in folders.Where(path => !_lastFolders.Contains(path)))
                {
                    await SendEvent(new EventPayload { EventType = "folder_navigation", Severity = "info", FolderPath = path }, false, ct);
                    foreach (var rule in _rules.Where(x => x.Enabled && x.Kind == "folder" && (x.Pattern == "*" || Match(path, x.Pattern))))
                    {
                        await SendEvent(new EventPayload
                        {
                            EventType = "folder_incident",
                            Severity = rule.Severity,
                            FolderPath = path,
                            Metadata = new() { { "rule", rule.Label } }
                        }, rule.CaptureOnMatch, ct);
                    }
                }
                _lastFolders.Clear();
                foreach (var folder in folders) _lastFolders.Add(folder);
            }
            await Task.Delay(1200, ct);
        }
    }

    private async Task MonitorBrowserHistory(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var session = _session;
            if (session is not null)
            {
                try
                {
                    foreach (var evt in _history.ReadNew(session.StartedAt))
                        await HandleBrowser(evt, ct, "history");
                }
                catch { }
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    private async Task MonitorService(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_session is not null)
            {
                var running = await IsServiceRunning(ct);
                if (!running && !_serviceReportedStopped)
                {
                    _serviceReportedStopped = true;
                    await SendEvent(new EventPayload
                    {
                        EventType = "service_stopping",
                        Severity = "critical",
                        ProcessName = "ITSQMETExamService",
                        Metadata = new() { { "source", "exam-agent" } }
                    }, true, ct);
                }
                else if (running)
                {
                    _serviceReportedStopped = false;
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private static async Task<bool> IsServiceRunning(CancellationToken ct)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("sc.exe", "query ITSQMETExamService")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0 && output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private async Task Heartbeat(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_session is not null)
            {
                try
                {
                    using var r = await _http.PostAsJsonAsync($"api/agent/session/{_session.SessionId}/heartbeat", new { status = "online" }, ct);
                    if (r.StatusCode == HttpStatusCode.NotFound) await EndLocalSession();
                }
                catch { }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task BrowserBridge(CancellationToken ct)
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:43119/");
            _listener.Start();
            while (!ct.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var origin = context.Request.Headers["Origin"];
                        if (!IsAllowedBrowserOrigin(origin))
                        {
                            context.Response.StatusCode = 403;
                            context.Response.Close();
                            return;
                        }
                        if (context.Request.HttpMethod == "OPTIONS")
                        {
                            AddCors(context.Response, origin!);
                            context.Response.StatusCode = 204;
                            context.Response.Close();
                            return;
                        }
                        if (context.Request.Url?.AbsolutePath != "/browser-event" || context.Request.HttpMethod != "POST")
                        {
                            context.Response.StatusCode = 404;
                            context.Response.Close();
                            return;
                        }
                        if (!FixedEquals(context.Request.Headers["X-ITSQMET-Bridge"], _config.BrowserBridgeKey))
                        {
                            context.Response.StatusCode = 403;
                            context.Response.Close();
                            return;
                        }
                        if (context.Request.ContentLength64 > 16384)
                        {
                            context.Response.StatusCode = 413;
                            context.Response.Close();
                            return;
                        }
                        using var sr = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                        var evt = JsonSerializer.Deserialize<BrowserEvent>(await sr.ReadToEndAsync(), _json);
                        AddCors(context.Response, origin!);
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                        if (evt is not null) await HandleBrowser(evt, ct, "extension");
                    }
                    catch { try { context.Response.Close(); } catch { } }
                }, ct);
            }
        }
        catch { }
    }

    private static bool IsAllowedBrowserOrigin(string? origin) =>
        !string.IsNullOrWhiteSpace(origin) &&
        (origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
         origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase));

    private static bool FixedEquals(string? supplied, string expected)
    {
        if (string.IsNullOrWhiteSpace(supplied) || string.IsNullOrWhiteSpace(expected)) return false;
        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static void AddCors(HttpListenerResponse r, string origin)
    {
        r.Headers["Access-Control-Allow-Origin"] = origin;
        r.Headers["Vary"] = "Origin";
        r.Headers["Access-Control-Allow-Headers"] = "content-type,x-itsqmet-bridge";
        r.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
    }

    private async Task HandleBrowser(BrowserEvent evt, CancellationToken ct, string source)
    {
        if (_session is null) return;
        if (!Uri.TryCreate(evt.Url, UriKind.Absolute, out var u) || !(u.Scheme is "http" or "https")) return;
        var safeUrl = $"{u.Scheme}://{u.Host}{u.AbsolutePath}";
        var navKey = evt.Browser + "|" + safeUrl;
        lock (_navigationLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (_recentNavigation.TryGetValue(navKey, out var previous) && now - previous < TimeSpan.FromSeconds(5)) return;
            _recentNavigation[navKey] = now;
            foreach (var old in _recentNavigation.Where(x => now - x.Value > TimeSpan.FromMinutes(2)).Select(x => x.Key).ToArray())
                _recentNavigation.Remove(old);
        }

        await SendEvent(new EventPayload
        {
            EventType = "browser_navigation",
            Severity = "info",
            Domain = u.Host,
            Url = safeUrl,
            Metadata = new() { { "browser", evt.Browser }, { "source", source } }
        }, false, ct);

        foreach (var rule in _rules.Where(x => x.Enabled && x.Kind == "domain" && DomainMatch(u.Host, x.Pattern)))
        {
            await SendEvent(new EventPayload
            {
                EventType = "navigation_incident",
                Severity = rule.Severity,
                Domain = u.Host,
                Url = safeUrl,
                Metadata = new() { { "browser", evt.Browser }, { "source", source }, { "rule", rule.Label } }
            }, rule.CaptureOnMatch, ct);
        }
    }

    private async Task SendEvent(EventPayload payload, bool screenshot, CancellationToken ct)
    {
        var session = _session;
        if (session is null) return;
        if (screenshot)
        {
            var s = WindowsMonitor.CaptureJpeg();
            if (s is not null)
            {
                payload.ScreenshotBase64 = s.Value.Base64;
                payload.ScreenshotContentType = "image/jpeg";
                payload.ScreenshotWidth = s.Value.Width;
                payload.ScreenshotHeight = s.Value.Height;
            }
        }
        if (!await PostEvent(session.SessionId, payload, ct)) await Queue(session.SessionId, payload);
    }

    private async Task<bool> PostEvent(Guid sessionId, EventPayload payload, CancellationToken ct)
    {
        try
        {
            using var r = await _http.PostAsJsonAsync($"api/agent/session/{sessionId}/event", payload, _json, ct);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task Queue(Guid sessionId, EventPayload payload)
    {
        await _queueLock.WaitAsync();
        try
        {
            var envelope = new OfflineEnvelope(sessionId, payload);
            await File.AppendAllTextAsync(_offlinePath, JsonSerializer.Serialize(envelope, _json) + Environment.NewLine);
        }
        catch { }
        finally { _queueLock.Release(); }
    }

    private async Task ReplayOffline(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(8), ct);
            if (!File.Exists(_offlinePath)) continue;

            await _queueLock.WaitAsync(ct);
            try
            {
                var lines = await File.ReadAllLinesAsync(_offlinePath, ct);
                if (lines.Length == 0) continue;
                var pending = new List<string>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    OfflineEnvelope? envelope = null;
                    try { envelope = JsonSerializer.Deserialize<OfflineEnvelope>(line, _json); } catch { }
                    if (envelope is null || !await PostEvent(envelope.SessionId, envelope.Payload, ct)) pending.Add(line);
                }
                if (pending.Count == 0) File.Delete(_offlinePath);
                else await File.WriteAllLinesAsync(_offlinePath, pending, ct);
            }
            catch { }
            finally { _queueLock.Release(); }
        }
    }

    private static bool Match(string value, string pattern) => value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    private static bool DomainMatch(string host, string pattern) => host.Equals(pattern, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + pattern, StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener?.Close();
        _http.Dispose();
        _queueLock.Dispose();
        _cts.Dispose();
    }
}
