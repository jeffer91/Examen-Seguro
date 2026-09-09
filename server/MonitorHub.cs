using Microsoft.AspNetCore.SignalR;

namespace Itsqmet.ExamServer;

public sealed class MonitorHub : Hub
{
    public Task JoinExam(string examId) => Groups.AddToGroupAsync(Context.ConnectionId, $"exam:{examId}");
    public Task LeaveExam(string examId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"exam:{examId}");
}
