using System.Text.Json.Serialization;

namespace Itsqmet.ExamAgent;

public sealed class AgentConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5080/";
    public string AgentKey { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public int PollSeconds { get; set; } = 3;
}

public sealed record AssignmentDto(Guid AssignmentId, Guid ExamId, string ExamName, string StudentCode, string StudentName, string Status);
public sealed record StartSessionResponse(Guid SessionId, Guid ExamId);
public sealed record RuleDto(Guid Id, Guid? ExamId, string Kind, string Pattern, string Label, string Severity, bool Enabled, bool CaptureOnMatch);
public sealed record BrowserEvent(string Url, string? Title, string Browser);
public sealed record SessionState(Guid AssignmentId, Guid ExamId, Guid SessionId, string StudentCode, string StudentName, DateTimeOffset StartedAt);
public sealed class EventPayload
{
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = "info";
    public string Severity { get; set; } = "info";
    public string? ProcessName { get; set; }
    public string? Domain { get; set; }
    public string? Url { get; set; }
    public string? FolderPath { get; set; }
    public int? DurationMs { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string? ScreenshotBase64 { get; set; }
    public string? ScreenshotContentType { get; set; }
    public int? ScreenshotWidth { get; set; }
    public int? ScreenshotHeight { get; set; }
}
