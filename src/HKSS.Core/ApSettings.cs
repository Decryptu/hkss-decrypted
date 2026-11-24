using System;
using System.IO;
using Newtonsoft.Json;

namespace HKSS.Core;

/// <summary>
/// Archipelago room connection settings shared across both games in a combo run.
/// Stored in <c>ap.json</c> inside the run directory so the launcher and plugins
/// can read/update room settings independently of gameplay progress (<c>state.json</c>).
/// </summary>
public sealed class ApSettings
{
    public const string DefaultHost = "localhost";
    public const int DefaultPort = 38281;

    [JsonProperty("host")]
    public string Host { get; set; } = DefaultHost;

    [JsonProperty("port")]
    public int Port { get; set; } = DefaultPort;

    [JsonProperty("slot")]
    public string Slot { get; set; } = string.Empty;

    [JsonProperty("password")]
    public string Password { get; set; } = string.Empty;

    [JsonProperty("auto_connect")]
    public bool AutoConnect { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port > 0 &&
        Port <= 65535 &&
        !string.IsNullOrWhiteSpace(Slot);

    public string FormatAddress() => $"{Host}:{Port}";

    /// <summary>
    /// Parses a host or host:port string. Preserves scheme if present (e.g. wss://archipelago.gg:38281).
    /// </summary>
    public static bool TryParseAddress(string? input, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string trimmed = input.Trim();
        int schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        int searchStart = schemeEnd >= 0 ? schemeEnd + 3 : 0;
        int colon = trimmed.LastIndexOf(':');
        if (colon > searchStart)
        {
            string portPart = trimmed.Substring(colon + 1);
            if (!int.TryParse(portPart, out int parsedPort) || parsedPort <= 0 || parsedPort > 65535)
                return false;
            host = trimmed.Substring(0, colon).Trim();
            port = parsedPort;
            return !string.IsNullOrWhiteSpace(host);
        }

        host = trimmed;
        return !string.IsNullOrWhiteSpace(host);
    }

    public ApSettings Clone() => new()
    {
        Host = Host,
        Port = Port,
        Slot = Slot,
        Password = Password,
        AutoConnect = AutoConnect,
    };
}

/// <summary>Atomic reader/writer for <c>ap.json</c> inside a combo run folder.</summary>
public sealed class ApSettingsStore
{
    public const string SettingsFileName = "ap.json";

    public const string HostEnvVar = "HKSS_AP_HOST";
    public const string PortEnvVar = "HKSS_AP_PORT";
    public const string SlotEnvVar = "HKSS_AP_SLOT";
    public const string PasswordEnvVar = "HKSS_AP_PASSWORD";
    public const string AutoConnectEnvVar = "HKSS_AP_AUTOCONNECT";

    private readonly string _path;

    public ApSettingsStore(RunContext run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        _path = Path.Combine(run.RunDir, SettingsFileName);
    }

    public ApSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new ApSettings();
            return JsonConvert.DeserializeObject<ApSettings>(File.ReadAllText(_path)) ?? new ApSettings();
        }
        catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
        {
            return new ApSettings();
        }
    }

    public ApSettings LoadWithEnvOverrides(Func<string, string?> getEnv)
    {
        if (getEnv == null) throw new ArgumentNullException(nameof(getEnv));
        ApSettings settings = Load();

        string? hostEnv = getEnv(HostEnvVar);
        if (!string.IsNullOrWhiteSpace(hostEnv))
        {
            if (ApSettings.TryParseAddress(hostEnv, out string parsedHost, out int parsedPort))
            {
                settings.Host = parsedHost;
                if (hostEnv.IndexOf(':', hostEnv.IndexOf("://", StringComparison.Ordinal) is int s && s >= 0 ? s + 3 : 0) > 0)
                    settings.Port = parsedPort;
            }
            else
            {
                settings.Host = hostEnv.Trim();
            }
        }

        string? portEnv = getEnv(PortEnvVar);
        if (!string.IsNullOrWhiteSpace(portEnv) && int.TryParse(portEnv.Trim(), out int port) && port > 0 && port <= 65535)
            settings.Port = port;

        string? slotEnv = getEnv(SlotEnvVar);
        if (!string.IsNullOrWhiteSpace(slotEnv))
            settings.Slot = slotEnv.Trim();

        string? passEnv = getEnv(PasswordEnvVar);
        if (passEnv != null)
            settings.Password = passEnv;

        string? autoEnv = getEnv(AutoConnectEnvVar);
        if (!string.IsNullOrWhiteSpace(autoEnv) && bool.TryParse(autoEnv.Trim(), out bool autoConnect))
            settings.AutoConnect = autoConnect;

        return settings;
    }

    public void Save(ApSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        string temp = _path + ".new";
        File.WriteAllText(temp, json);
        if (File.Exists(_path)) File.Replace(temp, _path, null);
        else File.Move(temp, _path);
    }
}
