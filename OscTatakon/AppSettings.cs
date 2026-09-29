using System.Text.Encodings.Web;
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

    /// <summary>
    /// 既定の割り当ての版数。既定の割り当てを変えたら上げる。
    /// 保存済みの版数がこれより古い場合、割り当ては新しい既定値に置き換える。
    /// </summary>
    public const int CURRENT_MAPPINGS_VERSION = 3;

    private const string FILE_NAME = "settings.json";

    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
        Converters = { new JsonStringEnumConverter() },
    };

    public int Port { get; set; } = DEFAULT_PORT;
    public InputMethod Method { get; set; } = InputMethod.SendInputScanCode;
    public int HoldMs { get; set; } = 30;
    public int GapMs { get; set; } = 20;
    public bool ActivateTarget { get; set; }
    public string TargetProcessName { get; set; } = "";
    public bool AutoStart { get; set; }

    /// <summary>保存時の割り当ての版数 (古い settings.json には無いので 0 になる)。</summary>
    public int MappingsVersion { get; set; }

    public List<KeyMapping> Mappings { get; set; } = CreateDefaultMappings();

    /// <summary>読み込み時に古い割り当てを既定値に置き換えたかどうか (保存しない)。</summary>
    [JsonIgnore]
    public bool WasMappingsUpgraded { get; private set; }

    /// <summary>
    /// 初期の割り当て。
    /// ゲーム既定の D/F/J/K は WASD 操作と被るため、ゲーム側のキー設定で
    /// カッを R/U に変更してもらう前提で R/F/J/U にしている。
    /// 曲選択は矢印キーだと効きにくかったため WASD にしている。
    /// </summary>
    public static List<KeyMapping> CreateDefaultMappings() => new()
    {
        new KeyMapping { Name = "ドン(右)", Address = "/taiko/don/right", Key = Keys.J, WithSpace = true },
        new KeyMapping { Name = "ドン(左)", Address = "/taiko/don/left", Key = Keys.F, WithSpace = true },
        new KeyMapping { Name = "カッ(右)", Address = "/taiko/ka/right", Key = Keys.U },
        new KeyMapping { Name = "カッ(左)", Address = "/taiko/ka/left", Key = Keys.R },
        new KeyMapping { Name = "曲選択 ↑", Address = "/taiko/menu/up", Key = Keys.W, IsHold = true },
        new KeyMapping { Name = "曲選択 ↓", Address = "/taiko/menu/down", Key = Keys.S, IsHold = true },
        new KeyMapping { Name = "曲選択 →", Address = "/taiko/menu/right", Key = Keys.D, IsHold = true },
        new KeyMapping { Name = "曲選択 ←", Address = "/taiko/menu/left", Key = Keys.A, IsHold = true },
        new KeyMapping { Name = "戻る/オプション", Address = "/taiko/menu/back", Key = Keys.Back },
        new KeyMapping { Name = "一時停止", Address = "/taiko/menu/pause", Key = Keys.Tab },
        new KeyMapping { Name = "ライブラリ選択 Q", Address = "/taiko/menu/library/q", Key = Keys.Q },
        new KeyMapping { Name = "ライブラリ選択 E", Address = "/taiko/menu/library/e", Key = Keys.E },
    };

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JSON_OPTIONS);
                if (settings != null)
                {
                    if (settings.MappingsVersion < CURRENT_MAPPINGS_VERSION)
                    {
                        settings.Mappings = CreateDefaultMappings();
                        settings.MappingsVersion = CURRENT_MAPPINGS_VERSION;
                        settings.WasMappingsUpgraded = true;
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // 壊れた設定は無視して既定値で起動する
        }
        return new AppSettings { MappingsVersion = CURRENT_MAPPINGS_VERSION };
    }

    /// <summary>保存する。失敗した場合はエラーメッセージを返す (成功時は null)。</summary>
    public string? Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JSON_OPTIONS));
            return null;
        }
        catch (Exception ex)
        {
            // 書き込み不可の場所に置かれていても動作は継続する
            return ex.Message;
        }
    }
}
