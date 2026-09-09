namespace Itsqmet.ExamServer;

public record CreateExamRequest(string Name, string? Career, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt);
public record CreateStudentRequest(string StudentCode, string FullName, string? Career);
public record RegisterDeviceRequest(string DeviceCode, string Hostname, string? WindowsVersion, string? AgentVersion);
public record CreateAssignmentRequest(Guid ExamId, Guid StudentId, Guid DeviceId, bool ConsentRecorded);
public record CreateRuleRequest(Guid? ExamId, string Kind, string Pattern, string Label, string Severity, bool CaptureOnMatch);
public record StartSessionRequest(Guid AssignmentId, string DeviceCode);
public record HeartbeatRequest(string Status = "online");
public record EventRequest(
    DateTimeOffset OccurredAt,
    string EventType,
    string Severity,
    string? ProcessName,
    string? Domain,
    string? Url,
    string? FolderPath,
    int? DurationMs,
    Dictionary<string, object>? Metadata,
    string? ScreenshotBase64,
    string? ScreenshotContentType,
    int? ScreenshotWidth,
    int? ScreenshotHeight
);
