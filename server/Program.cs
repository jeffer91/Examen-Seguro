using System.Security.Cryptography;
using System.Text.Json;
using Itsqmet.ExamServer;
using Microsoft.AspNetCore.SignalR;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("DATABASE_URL no está configurada.");
var adminKey = Environment.GetEnvironmentVariable("ADMIN_API_KEY") ?? "dev-admin-change-me";
var viewerKey = Environment.GetEnvironmentVariable("VIEWER_API_KEY") ?? "dev-viewer-change-me";
var agentKey = Environment.GetEnvironmentVariable("AGENT_API_KEY") ?? "dev-agent-change-me";

app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/health"))
    {
        await next();
        return;
    }

    string? key = ctx.Request.Headers["X-ITSQMET-Key"].FirstOrDefault();
    if (ctx.Request.Path.StartsWithSegments("/hub/monitor"))
        key ??= ctx.Request.Query["key"].FirstOrDefault();

    var role = key == adminKey ? "admin"
        : key == viewerKey ? "veedor"
        : key == agentKey ? "agent"
        : null;

    if (role is null)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsync("No autorizado");
        return;
    }

    ctx.Items["role"] = role;
    await next();
});

static bool IsRole(HttpContext ctx, params string[] roles) => roles.Contains(ctx.Items["role"]?.ToString());
static IResult Forbidden() => Results.StatusCode(StatusCodes.Status403Forbidden);
static DateTime? NullableDateTime(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
static Guid? NullableGuid(NpgsqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetGuid(i);

async Task<NpgsqlConnection> OpenDb()
{
    var conn = new NpgsqlConnection(connectionString);
    await conn.OpenAsync();
    return conn;
}

app.MapGet("/api/health", () => Results.Ok(new
{
    ok = true,
    service = "ITSQMET Examen Seguro",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/exams", async (HttpContext ctx) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT id,name,career,starts_at,ends_at,status,created_at FROM exams ORDER BY created_at DESC", db);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        rows.Add(new
        {
            id = r.GetGuid(0),
            name = r.GetString(1),
            career = r.IsDBNull(2) ? null : r.GetString(2),
            startsAt = NullableDateTime(r, 3),
            endsAt = NullableDateTime(r, 4),
            status = r.GetString(5),
            createdAt = r.GetDateTime(6)
        });
    }
    return Results.Ok(rows);
});

app.MapPost("/api/admin/exams", async (HttpContext ctx, CreateExamRequest req) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("INSERT INTO exams(name,career,starts_at,ends_at) VALUES($1,$2,$3,$4) RETURNING id", db);
    cmd.Parameters.AddWithValue(req.Name);
    cmd.Parameters.AddWithValue((object?)req.Career ?? DBNull.Value);
    cmd.Parameters.AddWithValue((object?)req.StartsAt ?? DBNull.Value);
    cmd.Parameters.AddWithValue((object?)req.EndsAt ?? DBNull.Value);
    return Results.Ok(new { id = (Guid)(await cmd.ExecuteScalarAsync())! });
});

app.MapPost("/api/admin/exams/{id:guid}/status/{status}", async (HttpContext ctx, Guid id, string status) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    if (status is not ("draft" or "active" or "closed")) return Results.BadRequest();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("UPDATE exams SET status=$1 WHERE id=$2", db);
    cmd.Parameters.AddWithValue(status);
    cmd.Parameters.AddWithValue(id);
    await cmd.ExecuteNonQueryAsync();
    return Results.NoContent();
});

app.MapGet("/api/students", async (HttpContext ctx) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT id,student_code,full_name,career FROM students ORDER BY full_name", db);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
        rows.Add(new { id = r.GetGuid(0), studentCode = r.GetString(1), fullName = r.GetString(2), career = r.IsDBNull(3) ? null : r.GetString(3) });
    return Results.Ok(rows);
});

app.MapPost("/api/admin/students", async (HttpContext ctx, CreateStudentRequest req) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("INSERT INTO students(student_code,full_name,career) VALUES($1,$2,$3) ON CONFLICT(student_code) DO UPDATE SET full_name=EXCLUDED.full_name,career=EXCLUDED.career RETURNING id", db);
    cmd.Parameters.AddWithValue(req.StudentCode);
    cmd.Parameters.AddWithValue(req.FullName);
    cmd.Parameters.AddWithValue((object?)req.Career ?? DBNull.Value);
    return Results.Ok(new { id = (Guid)(await cmd.ExecuteScalarAsync())! });
});

app.MapGet("/api/devices", async (HttpContext ctx) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT id,device_code,hostname,windows_version,agent_version,last_seen_at,active FROM devices ORDER BY hostname", db);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        rows.Add(new
        {
            id = r.GetGuid(0),
            deviceCode = r.GetString(1),
            hostname = r.IsDBNull(2) ? null : r.GetString(2),
            windowsVersion = r.IsDBNull(3) ? null : r.GetString(3),
            agentVersion = r.IsDBNull(4) ? null : r.GetString(4),
            lastSeenAt = NullableDateTime(r, 5),
            active = r.GetBoolean(6)
        });
    }
    return Results.Ok(rows);
});

app.MapPost("/api/agent/register", async (HttpContext ctx, RegisterDeviceRequest req) =>
{
    if (!IsRole(ctx, "agent")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("INSERT INTO devices(device_code,hostname,windows_version,agent_version,last_seen_at) VALUES($1,$2,$3,$4,now()) ON CONFLICT(device_code) DO UPDATE SET hostname=EXCLUDED.hostname,windows_version=EXCLUDED.windows_version,agent_version=EXCLUDED.agent_version,last_seen_at=now() RETURNING id", db);
    cmd.Parameters.AddWithValue(req.DeviceCode);
    cmd.Parameters.AddWithValue(req.Hostname);
    cmd.Parameters.AddWithValue((object?)req.WindowsVersion ?? DBNull.Value);
    cmd.Parameters.AddWithValue((object?)req.AgentVersion ?? DBNull.Value);
    return Results.Ok(new { id = (Guid)(await cmd.ExecuteScalarAsync())! });
});

app.MapPost("/api/admin/assignments", async (HttpContext ctx, CreateAssignmentRequest req) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("INSERT INTO exam_assignments(exam_id,student_id,device_id,consent_recorded) VALUES($1,$2,$3,$4) ON CONFLICT(exam_id,student_id) DO UPDATE SET device_id=EXCLUDED.device_id,consent_recorded=EXCLUDED.consent_recorded,status='ready' RETURNING id", db);
    cmd.Parameters.AddWithValue(req.ExamId);
    cmd.Parameters.AddWithValue(req.StudentId);
    cmd.Parameters.AddWithValue(req.DeviceId);
    cmd.Parameters.AddWithValue(req.ConsentRecorded);
    return Results.Ok(new { id = (Guid)(await cmd.ExecuteScalarAsync())! });
});

app.MapPost("/api/admin/assignments/{id:guid}/arm", async (HttpContext ctx, Guid id) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("UPDATE exam_assignments SET status='armed' WHERE id=$1 AND consent_recorded=true", db);
    cmd.Parameters.AddWithValue(id);
    var n = await cmd.ExecuteNonQueryAsync();
    return n == 1 ? Results.NoContent() : Results.BadRequest(new { message = "Debe registrarse el consentimiento informado antes de iniciar supervisión." });
});

app.MapPost("/api/admin/assignments/{id:guid}/finish", async (HttpContext ctx, Guid id, IHubContext<MonitorHub> hub) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    await using var db = await OpenDb();
    await using var tx = await db.BeginTransactionAsync();
    await using var q = new NpgsqlCommand("SELECT exam_id,student_id,device_id FROM exam_assignments WHERE id=$1 FOR UPDATE", db, tx);
    q.Parameters.AddWithValue(id);
    await using var r = await q.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.NotFound();
    var examId = r.GetGuid(0);
    var studentId = r.GetGuid(1);
    var deviceId = r.GetGuid(2);
    await r.DisposeAsync();

    await using var a = new NpgsqlCommand("UPDATE exam_assignments SET status='finished' WHERE id=$1", db, tx);
    a.Parameters.AddWithValue(id);
    await a.ExecuteNonQueryAsync();
    await using var s = new NpgsqlCommand("UPDATE exam_sessions SET status='finished',finished_at=now() WHERE exam_id=$1 AND student_id=$2 AND device_id=$3 AND status='active'", db, tx);
    s.Parameters.AddWithValue(examId);
    s.Parameters.AddWithValue(studentId);
    s.Parameters.AddWithValue(deviceId);
    await s.ExecuteNonQueryAsync();
    await tx.CommitAsync();
    await hub.Clients.Group($"exam:{examId}").SendAsync("sessionFinished", new { assignmentId = id, examId, studentId, deviceId });
    return Results.NoContent();
});

app.MapGet("/api/admin/assignments", async (HttpContext ctx) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT a.id,a.exam_id,e.name,a.student_id,s.student_code,s.full_name,a.device_id,d.device_code,d.hostname,a.status,a.consent_recorded FROM exam_assignments a JOIN exams e ON e.id=a.exam_id JOIN students s ON s.id=a.student_id JOIN devices d ON d.id=a.device_id ORDER BY e.created_at DESC,s.full_name", db);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
        rows.Add(new { id=r.GetGuid(0), examId=r.GetGuid(1), examName=r.GetString(2), studentId=r.GetGuid(3), studentCode=r.GetString(4), fullName=r.GetString(5), deviceId=r.GetGuid(6), deviceCode=r.GetString(7), hostname=r.IsDBNull(8)?null:r.GetString(8), status=r.GetString(9), consentRecorded=r.GetBoolean(10) });
    return Results.Ok(rows);
});

app.MapGet("/api/agent/assignment/{deviceCode}", async (HttpContext ctx, string deviceCode) =>
{
    if (!IsRole(ctx, "agent")) return Forbidden();
    await using var db = await OpenDb();
    await using var touch = new NpgsqlCommand("UPDATE devices SET last_seen_at=now() WHERE device_code=$1", db);
    touch.Parameters.AddWithValue(deviceCode);
    await touch.ExecuteNonQueryAsync();

    await using var cmd = new NpgsqlCommand("SELECT a.id,a.exam_id,e.name,s.student_code,s.full_name,a.status FROM exam_assignments a JOIN devices d ON d.id=a.device_id JOIN exams e ON e.id=a.exam_id JOIN students s ON s.id=a.student_id WHERE d.device_code=$1 AND a.status IN ('armed','active') ORDER BY a.created_at DESC LIMIT 1", db);
    cmd.Parameters.AddWithValue(deviceCode);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.NoContent();
    return Results.Ok(new { assignmentId=r.GetGuid(0), examId=r.GetGuid(1), examName=r.GetString(2), studentCode=r.GetString(3), studentName=r.GetString(4), status=r.GetString(5) });
});

app.MapPost("/api/agent/session/start", async (HttpContext ctx, StartSessionRequest req, IHubContext<MonitorHub> hub) =>
{
    if (!IsRole(ctx, "agent")) return Forbidden();
    await using var db = await OpenDb();
    await using var tx = await db.BeginTransactionAsync();

    await using var q = new NpgsqlCommand("SELECT a.exam_id,a.student_id,a.device_id,a.consent_recorded FROM exam_assignments a JOIN devices d ON d.id=a.device_id WHERE a.id=$1 AND d.device_code=$2 AND a.status IN ('armed','active') FOR UPDATE", db, tx);
    q.Parameters.AddWithValue(req.AssignmentId);
    q.Parameters.AddWithValue(req.DeviceCode);
    await using var r = await q.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.BadRequest();
    var examId = r.GetGuid(0);
    var studentId = r.GetGuid(1);
    var deviceId = r.GetGuid(2);
    var consent = r.GetBoolean(3);
    await r.DisposeAsync();
    if (!consent) return Results.BadRequest(new { message = "Supervisión no autorizada para esta sesión." });

    await using var existing = new NpgsqlCommand("SELECT id FROM exam_sessions WHERE exam_id=$1 AND student_id=$2 AND device_id=$3 AND status='active' ORDER BY started_at DESC LIMIT 1", db, tx);
    existing.Parameters.AddWithValue(examId);
    existing.Parameters.AddWithValue(studentId);
    existing.Parameters.AddWithValue(deviceId);
    var existingId = await existing.ExecuteScalarAsync();
    Guid sessionId;
    if (existingId is Guid found)
    {
        sessionId = found;
    }
    else
    {
        await using var c = new NpgsqlCommand("INSERT INTO exam_sessions(exam_id,student_id,device_id,status,consent_acknowledged_at,started_at,last_heartbeat_at) VALUES($1,$2,$3,'active',now(),now(),now()) RETURNING id", db, tx);
        c.Parameters.AddWithValue(examId);
        c.Parameters.AddWithValue(studentId);
        c.Parameters.AddWithValue(deviceId);
        sessionId = (Guid)(await c.ExecuteScalarAsync())!;
    }

    await using var u = new NpgsqlCommand("UPDATE exam_assignments SET status='active' WHERE id=$1", db, tx);
    u.Parameters.AddWithValue(req.AssignmentId);
    await u.ExecuteNonQueryAsync();
    await tx.CommitAsync();
    await hub.Clients.Group($"exam:{examId}").SendAsync("sessionStarted", new { sessionId, examId, studentId, deviceId });
    return Results.Ok(new { sessionId, examId });
});

app.MapPost("/api/agent/session/{sessionId:guid}/heartbeat", async (HttpContext ctx, Guid sessionId, HeartbeatRequest req, IHubContext<MonitorHub> hub) =>
{
    if (!IsRole(ctx, "agent")) return Forbidden();
    await using var db = await OpenDb();
    await using var update = new NpgsqlCommand("UPDATE exam_sessions SET last_heartbeat_at=now(),status='active' WHERE id=$1 AND status='active' RETURNING exam_id", db);
    update.Parameters.AddWithValue(sessionId);
    var examValue = await update.ExecuteScalarAsync();
    if (examValue is not Guid examId) return Results.NotFound();

    await using var hb = new NpgsqlCommand("INSERT INTO heartbeats(session_id,agent_status) VALUES($1,$2)", db);
    hb.Parameters.AddWithValue(sessionId);
    hb.Parameters.AddWithValue(req.Status);
    await hb.ExecuteNonQueryAsync();
    await hub.Clients.Group($"exam:{examId}").SendAsync("heartbeat", new { sessionId, status=req.Status, at=DateTimeOffset.UtcNow });
    return Results.NoContent();
});

app.MapPost("/api/agent/session/{sessionId:guid}/event", async (HttpContext ctx, Guid sessionId, EventRequest req, IHubContext<MonitorHub> hub) =>
{
    if (!IsRole(ctx, "agent")) return Forbidden();
    await using var db = await OpenDb();
    await using var tx = await db.BeginTransactionAsync();

    await using var e = new NpgsqlCommand("INSERT INTO events(session_id,occurred_at,event_type,severity,process_name,domain,url,folder_path,duration_ms,metadata) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10::jsonb) RETURNING id", db, tx);
    e.Parameters.AddWithValue(sessionId);
    e.Parameters.AddWithValue(req.OccurredAt);
    e.Parameters.AddWithValue(req.EventType);
    e.Parameters.AddWithValue(req.Severity);
    e.Parameters.AddWithValue((object?)req.ProcessName ?? DBNull.Value);
    e.Parameters.AddWithValue((object?)req.Domain ?? DBNull.Value);
    e.Parameters.AddWithValue((object?)req.Url ?? DBNull.Value);
    e.Parameters.AddWithValue((object?)req.FolderPath ?? DBNull.Value);
    e.Parameters.AddWithValue((object?)req.DurationMs ?? DBNull.Value);
    e.Parameters.AddWithValue(JsonSerializer.Serialize(req.Metadata ?? new Dictionary<string, object>()));
    var eventId = (Guid)(await e.ExecuteScalarAsync())!;

    if (!string.IsNullOrWhiteSpace(req.ScreenshotBase64))
    {
        var bytes = Convert.FromBase64String(req.ScreenshotBase64);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await using var s = new NpgsqlCommand("INSERT INTO screenshots(event_id,storage_key,sha256,width,height,image_bytes,content_type) VALUES($1,$2,$3,$4,$5,$6,$7)", db, tx);
        s.Parameters.AddWithValue(eventId);
        s.Parameters.AddWithValue($"db:{eventId}");
        s.Parameters.AddWithValue(hash);
        s.Parameters.AddWithValue((object?)req.ScreenshotWidth ?? DBNull.Value);
        s.Parameters.AddWithValue((object?)req.ScreenshotHeight ?? DBNull.Value);
        s.Parameters.AddWithValue(bytes);
        s.Parameters.AddWithValue(req.ScreenshotContentType ?? "image/jpeg");
        await s.ExecuteNonQueryAsync();
    }

    await using var q = new NpgsqlCommand("SELECT exam_id FROM exam_sessions WHERE id=$1", db, tx);
    q.Parameters.AddWithValue(sessionId);
    var examId = (Guid)(await q.ExecuteScalarAsync())!;
    await tx.CommitAsync();

    var payload = new { eventId, sessionId, examId, req.OccurredAt, req.EventType, req.Severity, req.ProcessName, req.Domain, req.Url, hasScreenshot=!string.IsNullOrWhiteSpace(req.ScreenshotBase64) };
    await hub.Clients.Group($"exam:{examId}").SendAsync("incident", payload);
    return Results.Ok(payload);
});

app.MapGet("/api/live/{examId:guid}", async (HttpContext ctx, Guid examId) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT es.id,s.student_code,s.full_name,d.hostname,es.status,es.started_at,es.last_heartbeat_at,(SELECT count(*) FROM events ev WHERE ev.session_id=es.id AND ev.severity IN ('high','critical')) AS alerts FROM exam_sessions es JOIN students s ON s.id=es.student_id JOIN devices d ON d.id=es.device_id WHERE es.exam_id=$1 ORDER BY s.full_name", db);
    cmd.Parameters.AddWithValue(examId);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        rows.Add(new
        {
            sessionId = r.GetGuid(0),
            studentCode = r.GetString(1),
            fullName = r.GetString(2),
            hostname = r.IsDBNull(3) ? null : r.GetString(3),
            status = r.GetString(4),
            startedAt = NullableDateTime(r, 5),
            lastHeartbeatAt = NullableDateTime(r, 6),
            alerts = r.GetInt64(7)
        });
    }
    return Results.Ok(rows);
});

app.MapGet("/api/events/{examId:guid}", async (HttpContext ctx, Guid examId) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT ev.id,ev.session_id,s.student_code,s.full_name,ev.occurred_at,ev.event_type,ev.severity,ev.process_name,ev.domain,ev.url,(sc.id IS NOT NULL) has_screenshot FROM events ev JOIN exam_sessions es ON es.id=ev.session_id JOIN students s ON s.id=es.student_id LEFT JOIN screenshots sc ON sc.event_id=ev.id WHERE es.exam_id=$1 ORDER BY ev.occurred_at DESC LIMIT 500", db);
    cmd.Parameters.AddWithValue(examId);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        rows.Add(new
        {
            id = r.GetGuid(0),
            sessionId = r.GetGuid(1),
            studentCode = r.GetString(2),
            fullName = r.GetString(3),
            occurredAt = r.GetDateTime(4),
            eventType = r.GetString(5),
            severity = r.GetString(6),
            processName = r.IsDBNull(7) ? null : r.GetString(7),
            domain = r.IsDBNull(8) ? null : r.GetString(8),
            url = r.IsDBNull(9) ? null : r.GetString(9),
            hasScreenshot = r.GetBoolean(10)
        });
    }
    return Results.Ok(rows);
});

app.MapGet("/api/screenshots/{eventId:guid}", async (HttpContext ctx, Guid eventId) =>
{
    if (!IsRole(ctx, "admin", "veedor")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT image_bytes,content_type FROM screenshots WHERE event_id=$1", db);
    cmd.Parameters.AddWithValue(eventId);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync() || r.IsDBNull(0)) return Results.NotFound();
    return Results.File((byte[])r[0], r.GetString(1));
});

app.MapGet("/api/rules", async (HttpContext ctx, Guid? examId) =>
{
    if (!IsRole(ctx, "admin", "veedor", "agent")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("SELECT id,exam_id,kind,pattern,label,severity,enabled,capture_on_match FROM monitoring_rules WHERE enabled=true AND (exam_id IS NULL OR exam_id=$1) ORDER BY exam_id NULLS FIRST,label", db);
    cmd.Parameters.AddWithValue((object?)examId ?? DBNull.Value);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        rows.Add(new
        {
            id = r.GetGuid(0),
            examId = NullableGuid(r, 1),
            kind = r.GetString(2),
            pattern = r.GetString(3),
            label = r.GetString(4),
            severity = r.GetString(5),
            enabled = r.GetBoolean(6),
            captureOnMatch = r.GetBoolean(7)
        });
    }
    return Results.Ok(rows);
});

app.MapPost("/api/admin/rules", async (HttpContext ctx, CreateRuleRequest req) =>
{
    if (!IsRole(ctx, "admin")) return Forbidden();
    await using var db = await OpenDb();
    await using var cmd = new NpgsqlCommand("INSERT INTO monitoring_rules(exam_id,kind,pattern,label,severity,capture_on_match) VALUES($1,$2,$3,$4,$5,$6) RETURNING id", db);
    cmd.Parameters.AddWithValue((object?)req.ExamId ?? DBNull.Value);
    cmd.Parameters.AddWithValue(req.Kind);
    cmd.Parameters.AddWithValue(req.Pattern);
    cmd.Parameters.AddWithValue(req.Label);
    cmd.Parameters.AddWithValue(req.Severity);
    cmd.Parameters.AddWithValue(req.CaptureOnMatch);
    return Results.Ok(new { id = (Guid)(await cmd.ExecuteScalarAsync())! });
});

app.MapHub<MonitorHub>("/hub/monitor");
app.Run();
