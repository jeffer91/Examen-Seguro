using Microsoft.AspNetCore.SignalR;

namespace Itsqmet.ExamServer;

public sealed class MonitorHub : Hub
{
    private bool CanView()
    {
        var role = Context.GetHttpContext()?.Items["role"]?.ToString();
        return role is "admin" or "veedor";
    }

    public Task JoinExam(string examId)
    {
        if (!CanView()) throw new HubException("No autorizado para supervisión en vivo.");
        return Groups.AddToGroupAsync(Context.ConnectionId, $"exam:{examId}");
    }

    public Task LeaveExam(string examId)
    {
        if (!CanView()) throw new HubException("No autorizado para supervisión en vivo.");
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, $"exam:{examId}");
    }
}
