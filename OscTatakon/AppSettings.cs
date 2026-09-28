using System.Text.Json;
using System.Text.Json.Serialization;

namespace OscTatakon;

/// <summary>OSC アドレスとキーの対応。</summary>
public sealed class KeyMapping
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public Keys Key { get; set; } = Keys.None;

    /// <summary>true ならこのキーと同時に Space も押す。</summary>
    public bool WithSpace { get; set; }
}

/// <summary>設定 (exe と同じフォルダの settings.json に保存)。</summary>
public sealed class AppSettings
{
    /// <summary>バーチャルキャストの OSC 送信ポートの既定値。</summary>
    public const int DEFAULT_PORT = 18100;

    private const string FILE_NAME = "settings.json";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public int Port { get; set; } = DEFAULT_PORT;
    public InputMethod Method { get; set; } = InputMethod.SendInputScanCode;
    public int HoldMs { get; set; } = 30;
    public int GapMs { get; set; } = 20;
    public bool ActivateTarget { get; set; }
    public string TargetProcessName { get; set; } = "";
    public bool AutoStart { get; set; }

    public List<KeyMapping> Mappings { get; set; } = CreateDefaultMappings();

    /// <summary>太鼓の達人 (Steam 版) の初期キー配置 D/F/J/K。</summary>
    public static List<KeyMapping> CreateDefaultMappings() => new()
    {
        new KeyMapping { Name = "カッ(左)", Address = "/taiko/ka/left", Key = Keys.D },
        new KeyMapping { Name = "ドン(左)", Address = "/taiko/don/left", Key = Keys.F },
        new KeyMapping { Name = "ドン(右)", Address = "/taiko/don/right", Key = Keys.J },
        new KeyMapping { Name = "カッ(右)", Address = "/taiko/ka/right", Key = Keys.K },
    };

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JSON_OPTIONS);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // 壊れた設定は無視して既定値で起動する
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JSON_OPTIONS));
        }
        catch
        {
            // 書き込み不可の場所に置かれていても動作は継続する
        }
    }
}
