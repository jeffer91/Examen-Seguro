using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Itsqmet.ExamService;

public sealed class Watchdog(ILogger<Watchdog> logger) : BackgroundService
{
    private bool _reported;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ITSQMET","ExamenSeguro");
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var statePath=Path.Combine(root,"current-session.json");var configPath=Path.Combine(root,"client.json");
                if(File.Exists(statePath)&&File.Exists(configPath))
                {
                    var running=Process.GetProcessesByName("Itsqmet.ExamAgent").Length>0;
                    if(!running&&!_reported)
                    {
                        var cfg=JsonSerializer.Deserialize<ClientConfig>(await File.ReadAllTextAsync(configPath,stoppingToken),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                        var state=JsonSerializer.Deserialize<CurrentSession>(await File.ReadAllTextAsync(statePath,stoppingToken),new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                        if(cfg is not null&&state is not null)
                        {
                            using var http=new HttpClient{BaseAddress=new Uri(cfg.ServerUrl.EndsWith('/')?cfg.ServerUrl:cfg.ServerUrl+"/")};http.DefaultRequestHeaders.Add("X-ITSQMET-Key",cfg.AgentKey);
                            await http.PostAsJsonAsync($"api/agent/session/{state.SessionId}/event",new{occurredAt=DateTimeOffset.UtcNow,eventType="agent_stopping",severity="critical",processName="Itsqmet.ExamAgent.exe",metadata=new{source="windows-service"}},stoppingToken);
                            _reported=true;
                        }
                    }
                    if(running)_reported=false;
                }
            }
            catch(Exception ex){logger.LogWarning(ex,"No se pudo comprobar el agente");}
            await Task.Delay(TimeSpan.FromSeconds(3),stoppingToken);
        }
    }
    private sealed class ClientConfig{public string ServerUrl{get;set;}="";public string AgentKey{get;set;}="";}
    private sealed class CurrentSession{public Guid SessionId{get;set;}}
}
