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

    /// <summary>
    /// true なら押しっぱなしモード。
    /// 引数 1 (true) で押し、0 (false) で離す。
    /// </summary>
    public bool IsHold { get; set; }
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

    /// <summary>
    /// 初期の割り当て。
    /// ゲーム既定の D/F/J/K は WASD 操作と被るため、ゲーム側のキー設定で
    /// カッを R/U に変更してもらう前提で R/F/J/U にしている。
    /// </summary>
    public static List<KeyMapping> CreateDefaultMappings() => new()
    {
        new KeyMapping { Name = "ドン(右)", Address = "/taiko/don/right", Key = Keys.J, WithSpace = true },
        new KeyMapping { Name = "ドン(左)", Address = "/taiko/don/left", Key = Keys.F, WithSpace = true },
        new KeyMapping { Name = "カッ(右)", Address = "/taiko/ka/right", Key = Keys.U },
        new KeyMapping { Name = "カッ(左)", Address = "/taiko/ka/left", Key = Keys.R },
        new KeyMapping { Name = "曲選択 ↑", Address = "/taiko/menu/up", Key = Keys.Up, IsHold = true },
        new KeyMapping { Name = "曲選択 ↓", Address = "/taiko/menu/down", Key = Keys.Down, IsHold = true },
        new KeyMapping { Name = "曲選択 →", Address = "/taiko/menu/right", Key = Keys.Right, IsHold = true },
        new KeyMapping { Name = "曲選択 ←", Address = "/taiko/menu/left", Key = Keys.Left, IsHold = true },
        new KeyMapping { Name = "戻る/オプション", Address = "/taiko/menu/back", Key = Keys.Back },
        new KeyMapping { Name = "一時停止", Address = "/taiko/menu/pause", Key = Keys.Tab },
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
