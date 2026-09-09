using Microsoft.Data.Sqlite;

namespace Itsqmet.ExamAgent;

internal sealed class BrowserHistoryMonitor
{
    private readonly string _tempRoot;
    private readonly Dictionary<string, long> _lastVisit = new(StringComparer.OrdinalIgnoreCase);

    public BrowserHistoryMonitor(string root)
    {
        _tempRoot = Path.Combine(root, "history-cache");
        Directory.CreateDirectory(_tempRoot);
    }

    public IReadOnlyList<BrowserEvent> ReadNew(DateTimeOffset sessionStartedAt)
    {
        var events = new List<BrowserEvent>();
        ReadChromium("chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "User Data"), sessionStartedAt, events);
        ReadChromium("edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data"), sessionStartedAt, events);
        ReadChromium("brave", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware", "Brave-Browser", "User Data"), sessionStartedAt, events);
        ReadFirefox(sessionStartedAt, events);
        return events;
    }

    private void ReadChromium(string browser, string userData, DateTimeOffset startedAt, List<BrowserEvent> output)
    {
        if (!Directory.Exists(userData)) return;
        IEnumerable<string> profiles;
        try
        {
            profiles = Directory.EnumerateDirectories(userData)
                .Where(x => Path.GetFileName(x).Equals("Default", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase));
        }
        catch { return; }

        foreach (var profile in profiles)
        {
            var db = Path.Combine(profile, "History");
            if (!File.Exists(db)) continue;
            var key = browser + ":" + profile;
            var minVisit = _lastVisit.TryGetValue(key, out var previous)
                ? previous
                : Math.Max(0, startedAt.ToFileTime() / 10);
            var maxSeen = minVisit;

            var copy = CopySqlite(db, key);
            if (copy is null) continue;
            try
            {
                using var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT u.url,v.visit_time FROM visits v JOIN urls u ON u.id=v.url WHERE v.visit_time > $min ORDER BY v.visit_time LIMIT 500";
                cmd.Parameters.AddWithValue("$min", minVisit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var url = reader.GetString(0);
                    var visit = reader.GetInt64(1);
                    if (visit > maxSeen) maxSeen = visit;
                    if (IsWebUrl(url)) output.Add(new BrowserEvent(url, null, browser));
                }
                _lastVisit[key] = maxSeen;
            }
            catch { }
            finally { DeleteCopy(copy); }
        }
    }

    private void ReadFirefox(DateTimeOffset startedAt, List<BrowserEvent> output)
    {
        var profilesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profilesRoot)) return;
        IEnumerable<string> profiles;
        try { profiles = Directory.EnumerateDirectories(profilesRoot); } catch { return; }

        foreach (var profile in profiles)
        {
            var db = Path.Combine(profile, "places.sqlite");
            if (!File.Exists(db)) continue;
            var key = "firefox:" + profile;
            var minVisit = _lastVisit.TryGetValue(key, out var previous)
                ? previous
                : startedAt.ToUnixTimeMilliseconds() * 1000;
            var maxSeen = minVisit;

            var copy = CopySqlite(db, key);
            if (copy is null) continue;
            try
            {
                using var conn = new SqliteConnection($"Data Source={copy};Mode=ReadOnly");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT p.url,h.visit_date FROM moz_historyvisits h JOIN moz_places p ON p.id=h.place_id WHERE h.visit_date > $min ORDER BY h.visit_date LIMIT 500";
                cmd.Parameters.AddWithValue("$min", minVisit);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var url = reader.GetString(0);
                    var visit = reader.GetInt64(1);
                    if (visit > maxSeen) maxSeen = visit;
                    if (IsWebUrl(url)) output.Add(new BrowserEvent(url, null, "firefox"));
                }
                _lastVisit[key] = maxSeen;
            }
            catch { }
            finally { DeleteCopy(copy); }
        }
    }

    private string? CopySqlite(string source, string key)
    {
        try
        {
            var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))).Substring(0, 16);
            var dir = Path.Combine(_tempRoot, safe);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, Path.GetFileName(source));
            File.Copy(source, target, true);
            TryCopy(source + "-wal", target + "-wal");
            TryCopy(source + "-shm", target + "-shm");
            return target;
        }
        catch { return null; }
    }

    private static void TryCopy(string source, string target)
    {
        try { if (File.Exists(source)) File.Copy(source, target, true); } catch { }
    }

    private static void DeleteCopy(string target)
    {
        try
        {
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
    }

    private static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https";
}
