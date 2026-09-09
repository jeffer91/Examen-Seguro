using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Itsqmet.ExamService;

public sealed class Watchdog(ILogger<Watchdog> logger) : BackgroundService
{
    private DateTimeOffset? _lastReportedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ITSQMET", "ExamenSeguro");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var statePath = Path.Combine(root, "current-session.json");
                var configPath = Path.Combine(root, "client.json");
                if (File.Exists(statePath) && File.Exists(configPath))
                {
                    var running = Process.GetProcessesByName("Itsqmet.ExamAgent").Length > 0;
                    if (!running)
                    {
                        if (_lastReportedAt is null || DateTimeOffset.UtcNow - _lastReportedAt > TimeSpan.FromSeconds(30))
                        {
                            await ReportStopped(configPath, statePath, stoppingToken);
                            _lastReportedAt = DateTimeOffset.UtcNow;
                        }
                        await TryRestartAgent(stoppingToken);
                    }
                    else
                    {
                        _lastReportedAt = null;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo comprobar el agente");
            }
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ReportStopped(string configPath, string statePath, CancellationToken ct)
    {
        try
        {
            var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var cfg = JsonSerializer.Deserialize<ClientConfig>(await File.ReadAllTextAsync(configPath, ct), json);
            var state = JsonSerializer.Deserialize<CurrentSession>(await File.ReadAllTextAsync(statePath, ct), json);
            if (cfg is null || state is null) return;

            using var http = new HttpClient
            {
                BaseAddress = new Uri(cfg.ServerUrl.EndsWith('/') ? cfg.ServerUrl : cfg.ServerUrl + "/"),
                Timeout = TimeSpan.FromSeconds(10)
            };
            http.DefaultRequestHeaders.Add("X-ITSQMET-Key", cfg.AgentKey);
            using var response = await http.PostAsJsonAsync($"api/agent/session/{state.SessionId}/event", new
            {
                occurredAt = DateTimeOffset.UtcNow,
                eventType = "agent_stopping",
                severity = "critical",
                processName = "Itsqmet.ExamAgent.exe",
                metadata = new { source = "windows-service", restartAttempted = true }
            }, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("El servidor respondió {StatusCode} al reportar el cierre del agente", response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo reportar el cierre del agente");
        }
    }

    private async Task TryRestartAgent(CancellationToken ct)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("schtasks.exe", "/Run /TN \"ITSQMET Exam Agent\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
                logger.LogWarning("No se pudo solicitar el reinicio del agente. Código {ExitCode}", p.ExitCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falló el intento de reinicio del agente");
        }
    }

    private sealed class ClientConfig
    {
        public string ServerUrl { get; set; } = "";
        public string AgentKey { get; set; } = "";
    }

    private sealed class CurrentSession
    {
        public Guid SessionId { get; set; }
    }
}
