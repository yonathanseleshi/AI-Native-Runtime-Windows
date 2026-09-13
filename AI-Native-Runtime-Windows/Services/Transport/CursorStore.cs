using System.Text.Json;

namespace AI_Native_Runtime_Windows.Services.Transport
{
    public sealed record EventCursor(string StreamId, long Sequence);

    /// <summary>
    /// Persists the event stream's last-delivered cursor across a relaunch
    /// (`desktop-shell-conventions.md` §4.1). A sequence number and stream
    /// id are not secrets — this deliberately does **not** go through
    /// <see cref="CredentialStore"/> (Credential Manager), mirroring MAC's
    /// choice of `UserDefaults` over Keychain for the same reason, and for
    /// the same reason this project reserves Credential Manager for actual
    /// secrets. A plain JSON file under `%LOCALAPPDATA%` (rather than
    /// `Windows.Storage.ApplicationData.Current.LocalSettings`) is used
    /// because that API requires package identity and this app must also
    /// run unpackaged (plan §6.5) — a plain file works in both modes.
    /// </summary>
    public sealed class CursorStore
    {
        private readonly string _path;

        public CursorStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AINativeRuntime");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "event-cursor.json");
        }

        public EventCursor? Load()
        {
            if (!File.Exists(_path)) return null;
            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<EventCursor>(json);
            }
            catch (JsonException)
            {
                // Corrupt/unreadable — treat as "no cursor ever recorded," never throw.
                return null;
            }
        }

        public void Save(EventCursor cursor)
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(cursor));
        }

        public void Clear()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
