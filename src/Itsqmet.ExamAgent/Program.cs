using System.Text.Json;
using Itsqmet.ExamAgent;

var mutex = new Mutex(true, "Global\\ITSQMET_ExamAgent", out var first);
if (!first) return;

var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ITSQMET", "ExamenSeguro");
Directory.CreateDirectory(root);
var configPath = Path.Combine(root, "client.json");
if (!File.Exists(configPath)) return;

var config = JsonSerializer.Deserialize<AgentConfig>(await File.ReadAllTextAsync(configPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (config is null || string.IsNullOrWhiteSpace(config.AgentKey) || string.IsNullOrWhiteSpace(config.DeviceCode)) return;
if (!config.ServerUrl.EndsWith('/')) config.ServerUrl += "/";

using var app = new AgentRuntime(config, root);
await app.RunAsync();
GC.KeepAlive(mutex);
