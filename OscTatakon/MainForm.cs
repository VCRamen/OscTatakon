using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Principal;

namespace OscTatakon;

public sealed class MainForm : Form
{
    private const int MAX_LOG_LINES = 300;
    private const int TEST_DELAY_MS = 3000;
    private const int TEST_INTERVAL_MS = 300;

    private static readonly Keys[] SELECTABLE_KEYS = BuildSelectableKeys();

    private readonly AppSettings settings;
    private readonly OscReceiver receiver = new();
    private readonly KeyInjector injector = new();
    private readonly ConcurrentQueue<string> logQueue = new();
    private readonly System.Windows.Forms.Timer uiTimer = new() { Interval = 100 };

    private volatile Dictionary<string, KeyMapping> addressMap = new(StringComparer.OrdinalIgnoreCase);
    private long receivedCount;
    private long hitCount;
    private volatile bool isReceiveLogEnabled = true;
    private int targetRefreshTick;

    private readonly NumericUpDown portInput = new() { Minimum = 1, Maximum = 65535, Width = 80 };
    private readonly Button startButton = new() { Text = "受信開始", AutoSize = true };
    private readonly CheckBox autoStartCheck = new() { Text = "起動時に受信開始", AutoSize = true };
    private readonly Label statusLabel = new() { Text = "停止中", AutoSize = true, ForeColor = Color.Gray };

    private readonly DataGridView mappingGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        RowHeadersWidth = 24,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
    };

    private readonly ComboBox methodCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown holdInput = new() { Minimum = 1, Maximum = 500, Width = 60 };
    private readonly NumericUpDown gapInput = new() { Minimum = 0, Maximum = 500, Width = 60 };

    private readonly CheckBox activateCheck = new() { Text = "入力前に対象ウィンドウを前面化", AutoSize = true };
    private readonly ComboBox processCombo = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 260 };
    private readonly Button refreshButton = new() { Text = "更新", AutoSize = true };

    private readonly Button testAllButton = new() { Text = "3秒後に全キーをテスト", AutoSize = true };
    private readonly Button resetMappingButton = new() { Text = "割り当てを初期値に戻す", AutoSize = true };
    private readonly CheckBox logReceiveCheck = new() { Text = "受信内容をログに出す", AutoSize = true, Checked = true };
    private readonly Label counterLabel = new() { AutoSize = true };
    private readonly ListBox logList = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
        HorizontalScrollbar = true,
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    public MainForm()
    {
        settings = AppSettings.Load();

        Text = "OscTatakon - OSC → キー入力" + (IsAdministrator() ? " [管理者]" : "");
        ClientSize = new Size(640, 820);
        MinimumSize = new Size(560, 600);
        Font = new Font("Yu Gothic UI", 9f);

        BuildLayout();
        LoadSettingsToUi();

        receiver.MessageReceived += OnOscMessage;
        receiver.ErrorOccurred += message => logQueue.Enqueue(message);
        injector.Log += message => logQueue.Enqueue(message);

        startButton.Click += (_, _) => ToggleReceiver();
        refreshButton.Click += (_, _) => RefreshProcessList();
        testAllButton.Click += async (_, _) => await TestAllKeysAsync();
        resetMappingButton.Click += (_, _) => ResetMappings();
        methodCombo.SelectedIndexChanged += (_, _) => ApplyInputSettings();
        holdInput.ValueChanged += (_, _) => ApplyInputSettings();
        gapInput.ValueChanged += (_, _) => ApplyInputSettings();
        activateCheck.CheckedChanged += (_, _) => ApplyInputSettings();
        processCombo.TextChanged += (_, _) => ApplyInputSettings();
        logReceiveCheck.CheckedChanged += (_, _) => isReceiveLogEnabled = logReceiveCheck.Checked;
        mappingGrid.CellValueChanged += (_, _) => RebuildAddressMap();
        mappingGrid.RowsRemoved += (_, _) => RebuildAddressMap();
        mappingGrid.CellContentClick += OnGridCellContentClick;
        mappingGrid.DataError += (_, e) => e.ThrowException = false;
        mappingGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (mappingGrid.IsCurrentCellDirty && mappingGrid.CurrentCell is DataGridViewComboBoxCell or DataGridViewCheckBoxCell)
            {
                mappingGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        uiTimer.Tick += (_, _) => FlushUi();
        uiTimer.Start();

        AddLog($"設定ファイル: {Path.Combine(AppContext.BaseDirectory, "settings.json")}");
        if (settings.AutoStart) ToggleReceiver();
    }

    // ------------------------------------------------------------------
    // レイアウト
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        Controls.Add(root);

        // OSC 受信
        var oscGroup = CreateGroup("OSC 受信");
        var oscFlow = CreateFlow();
        oscFlow.Controls.AddRange(new Control[]
        {
            CreateLabel("ポート"), portInput, startButton, statusLabel, autoStartCheck,
        });
        oscGroup.Controls.Add(oscFlow);
        root.Controls.Add(oscGroup);

        // キー割り当て
        var mapGroup = CreateGroup("OSC アドレス → キー割り当て (引数が 0 / false のメッセージは無視)");
        mapGroup.Dock = DockStyle.Fill;
        mapGroup.AutoSize = false;
        mappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "名前", FillWeight = 22 });
        mappingGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Address", HeaderText = "OSC アドレス", FillWeight = 32 });
        var keyColumn = new DataGridViewComboBoxColumn
        {
            Name = "Key",
            HeaderText = "キー",
            FillWeight = 18,
            ValueType = typeof(Keys),
            FlatStyle = FlatStyle.Flat,
        };
        foreach (var key in SELECTABLE_KEYS) keyColumn.Items.Add(key);
        mappingGrid.Columns.Add(keyColumn);
        mappingGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "WithSpace",
            HeaderText = "+Space",
            ToolTipText = "オンにすると、このキーと同時に Space キーも押します",
            FillWeight = 12,
        });
        mappingGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "IsHold",
            HeaderText = "押しっぱなし",
            ToolTipText = "オンにすると、引数 1 で押して 0 で離すまで押しっぱなしにします",
            FillWeight = 16,
        });
        mappingGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Test",
            HeaderText = "",
            Text = "3秒後",
            UseColumnTextForButtonValue = true,
            FillWeight = 12,
        });
        mapGroup.Controls.Add(mappingGrid);
        root.Controls.Add(mapGroup);

        // 入力方式
        var inputGroup = CreateGroup("入力方式");
        var inputTable = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        var methodFlow = CreateFlow();
        methodCombo.Items.AddRange(new object[]
        {
            "SendInput (スキャンコード) ※推奨",
            "SendInput (仮想キー + スキャンコード)",
            "keybd_event (旧API)",
        });
        methodFlow.Controls.AddRange(new Control[] { CreateLabel("方式"), methodCombo });
        var timingFlow = CreateFlow();
        timingFlow.Controls.AddRange(new Control[]
        {
            CreateLabel("押下時間(ms)"), holdInput, CreateLabel("同じキー連打時の間隔(ms)"), gapInput,
        });
        var targetFlow = CreateFlow();
        targetFlow.Controls.AddRange(new Control[] { activateCheck, processCombo, refreshButton });
        inputTable.Controls.Add(methodFlow);
        inputTable.Controls.Add(timingFlow);
        inputTable.Controls.Add(targetFlow);
        inputGroup.Controls.Add(inputTable);
        root.Controls.Add(inputGroup);

        // テスト
        var testFlow = CreateFlow();
        testFlow.Controls.AddRange(new Control[] { testAllButton, resetMappingButton, logReceiveCheck, counterLabel });
        root.Controls.Add(testFlow);

        // ログ
        var logGroup = CreateGroup("ログ");
        logGroup.Dock = DockStyle.Fill;
        logGroup.AutoSize = false;
        logGroup.Controls.Add(logList);
        root.Controls.Add(logGroup);
    }

    private static GroupBox CreateGroup(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(6),
    };

    private static FlowLayoutPanel CreateFlow() => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
    };

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 7, 3, 3),
    };

    // ------------------------------------------------------------------
    // 設定
    // ------------------------------------------------------------------

    private void LoadSettingsToUi()
    {
        portInput.Value = Math.Clamp(settings.Port, 1, 65535);
        autoStartCheck.Checked = settings.AutoStart;
        methodCombo.SelectedIndex = (int)settings.Method;
        holdInput.Value = Math.Clamp(settings.HoldMs, 1, 500);
        gapInput.Value = Math.Clamp(settings.GapMs, 0, 500);
        activateCheck.Checked = settings.ActivateTarget;
        RefreshProcessList();
        processCombo.Text = settings.TargetProcessName;

        SetMappingsToGrid(settings.Mappings);
        ApplyInputSettings();
    }

    private void SetMappingsToGrid(IEnumerable<KeyMapping> mappings)
    {
        mappingGrid.Rows.Clear();
        foreach (var mapping in mappings)
        {
            var key = SELECTABLE_KEYS.Contains(mapping.Key) ? mapping.Key : Keys.None;
            mappingGrid.Rows.Add(mapping.Name, mapping.Address, key, mapping.WithSpace, mapping.IsHold);
        }
        RebuildAddressMap();
    }

    private void ResetMappings()
    {
        var answer = MessageBox.Show(
            this, "キー割り当てを初期値に戻しますか？", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        SetMappingsToGrid(AppSettings.CreateDefaultMappings());
        AddLog("キー割り当てを初期値に戻しました");
    }

    private void SaveUiToSettings()
    {
        settings.Port = (int)portInput.Value;
        settings.AutoStart = autoStartCheck.Checked;
        settings.Method = (InputMethod)Math.Max(0, methodCombo.SelectedIndex);
        settings.HoldMs = (int)holdInput.Value;
        settings.GapMs = (int)gapInput.Value;
        settings.ActivateTarget = activateCheck.Checked;
        settings.TargetProcessName = processCombo.Text.Trim();
        settings.Mappings = ReadMappingsFromGrid();
        settings.Save();
    }

    private List<KeyMapping> ReadMappingsFromGrid()
    {
        var mappings = new List<KeyMapping>();
        foreach (DataGridViewRow row in mappingGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var address = (row.Cells["Address"].Value as string ?? "").Trim();
            if (address.Length == 0) continue;
            mappings.Add(new KeyMapping
            {
                Name = (row.Cells["Name"].Value as string ?? "").Trim(),
                Address = address,
                Key = row.Cells["Key"].Value is Keys key ? key : Keys.None,
                WithSpace = row.Cells["WithSpace"].Value is true,
                IsHold = row.Cells["IsHold"].Value is true,
            });
        }
        return mappings;
    }

    private void RebuildAddressMap()
    {
        var map = new Dictionary<string, KeyMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in ReadMappingsFromGrid())
        {
            if (mapping.Key != Keys.None) map[mapping.Address] = mapping;
        }
        addressMap = map;
    }

    private void ApplyInputSettings()
    {
        injector.Method = (InputMethod)Math.Max(0, methodCombo.SelectedIndex);
        injector.HoldMs = (int)holdInput.Value;
        injector.GapMs = (int)gapInput.Value;
        injector.TargetWindow = activateCheck.Checked ? FindTargetWindow(processCombo.Text) : IntPtr.Zero;
    }

    // ------------------------------------------------------------------
    // 対象ウィンドウ
    // ------------------------------------------------------------------

    private void RefreshProcessList()
    {
        var current = processCombo.Text;
        processCombo.Items.Clear();
        var names = Process.GetProcesses()
            .Where(p => SafeGetMainWindowHandle(p) != IntPtr.Zero)
            .Select(p => p.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        processCombo.Items.AddRange(names);
        processCombo.Text = current;
        ApplyInputSettings();
    }

    private static IntPtr FindTargetWindow(string processName)
    {
        processName = processName.Trim();
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName = processName[..^4];
        }
        if (processName.Length == 0) return IntPtr.Zero;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            var handle = SafeGetMainWindowHandle(process);
            if (handle != IntPtr.Zero) return handle;
        }
        return IntPtr.Zero;
    }

    private static IntPtr SafeGetMainWindowHandle(Process process)
    {
        try
        {
            return process.MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    // ------------------------------------------------------------------
    // OSC 受信
    // ------------------------------------------------------------------

    private void ToggleReceiver()
    {
        if (receiver.IsRunning)
        {
            receiver.Stop();
            injector.ReleaseAll();
            startButton.Text = "受信開始";
            statusLabel.Text = "停止中";
            statusLabel.ForeColor = Color.Gray;
            portInput.Enabled = true;
            AddLog("受信停止");
            return;
        }

        var port = (int)portInput.Value;
        try
        {
            RebuildAddressMap();
            receiver.Start(port);
        }
        catch (Exception ex)
        {
            AddLog($"ポート {port} を開けませんでした: {ex.Message}");
            statusLabel.Text = "エラー";
            statusLabel.ForeColor = Color.Red;
            return;
        }
        startButton.Text = "受信停止";
        statusLabel.Text = $"受信中 (UDP {port})";
        statusLabel.ForeColor = Color.Green;
        portInput.Enabled = false;
        AddLog($"受信開始: UDP {port}");
    }

    /// <summary>受信スレッドから呼ばれる。遅延を避けるためここで直接キー入力を発行する。</summary>
    private void OnOscMessage(OscMessage message)
    {
        Interlocked.Increment(ref receivedCount);
        var map = addressMap;
        var isMapped = map.TryGetValue(message.Address, out var mapping);
        var isTrigger = isMapped && IsTriggerValue(message.Args);
        string result;

        if (!isMapped)
        {
            result = "未割り当て";
        }
        else if (mapping!.IsHold)
        {
            if (isTrigger)
            {
                PressMapping(mapping);
                Interlocked.Increment(ref hitCount);
                result = $"→ {DescribeKeys(mapping)} 押す";
            }
            else
            {
                ReleaseMapping(mapping);
                result = $"→ {DescribeKeys(mapping)} 離す";
            }
        }
        else if (isTrigger)
        {
            HitMapping(mapping);
            Interlocked.Increment(ref hitCount);
            result = $"→ {DescribeKeys(mapping)}";
        }
        else
        {
            result = "無視(0/false)";
        }

        if (isReceiveLogEnabled)
        {
            var args = string.Join(", ", message.Args.Select(a => a?.ToString() ?? "nil"));
            logQueue.Enqueue($"{message.Address} [{args}] {result}");
        }
    }

    /// <summary>割り当てのキーを叩く (+Space がオンなら Space も同時に)。</summary>
    private void HitMapping(KeyMapping mapping)
    {
        injector.Hit(mapping.Key);
        if (mapping.WithSpace && mapping.Key != Keys.Space) injector.Hit(Keys.Space);
    }

    /// <summary>押しっぱなしモードの押下 (+Space がオンなら Space も)。</summary>
    private void PressMapping(KeyMapping mapping)
    {
        injector.Press(mapping.Key);
        if (mapping.WithSpace && mapping.Key != Keys.Space) injector.Press(Keys.Space);
    }

    /// <summary>押しっぱなしモードの解放。</summary>
    private void ReleaseMapping(KeyMapping mapping)
    {
        injector.Release(mapping.Key);
        if (mapping.WithSpace && mapping.Key != Keys.Space) injector.Release(Keys.Space);
    }

    private static string DescribeKeys(KeyMapping mapping) =>
        mapping.WithSpace && mapping.Key != Keys.Space ? $"{mapping.Key} + Space" : mapping.Key.ToString();

    /// <summary>引数なし、または先頭引数が 0 / false 以外なら入力とみなす。</summary>
    private static bool IsTriggerValue(IReadOnlyList<object?> args)
    {
        if (args.Count == 0) return true;
        return args[0] switch
        {
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            float f => f != 0f,
            double d => d != 0d,
            string s => s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    // ------------------------------------------------------------------
    // テスト
    // ------------------------------------------------------------------

    private async void OnGridCellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || mappingGrid.Columns[e.ColumnIndex].Name != "Test") return;
        var row = mappingGrid.Rows[e.RowIndex];
        if (row.IsNewRow || row.Cells["Key"].Value is not Keys key || key == Keys.None) return;
        var mapping = new KeyMapping { Key = key, WithSpace = row.Cells["WithSpace"].Value is true };
        AddLog($"3 秒後に {DescribeKeys(mapping)} を入力します。ゲームのウィンドウをクリックして前面にしてください。");
        await Task.Delay(TEST_DELAY_MS);
        HitMapping(mapping);
        AddLog($"テスト入力: {DescribeKeys(mapping)}");
    }

    private async Task TestAllKeysAsync()
    {
        var mappings = ReadMappingsFromGrid().Where(m => m.Key != Keys.None).ToList();
        if (mappings.Count == 0) return;
        testAllButton.Enabled = false;
        AddLog("3 秒後に全キーを順番に入力します。ゲームのウィンドウをクリックして前面にしてください。");
        await Task.Delay(TEST_DELAY_MS);
        foreach (var mapping in mappings)
        {
            HitMapping(mapping);
            AddLog($"テスト入力: {DescribeKeys(mapping)}");
            await Task.Delay(TEST_INTERVAL_MS);
        }
        testAllButton.Enabled = true;
    }

    // ------------------------------------------------------------------
    // ログ
    // ------------------------------------------------------------------

    private void AddLog(string message) => logQueue.Enqueue(message);

    private void FlushUi()
    {
        if (!logQueue.IsEmpty)
        {
            logList.BeginUpdate();
            while (logQueue.TryDequeue(out var line))
            {
                logList.Items.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");
            }
            while (logList.Items.Count > MAX_LOG_LINES) logList.Items.RemoveAt(0);
            logList.TopIndex = logList.Items.Count - 1;
            logList.EndUpdate();
        }
        // ゲームを後から起動した場合に備えて、対象ウィンドウを 2 秒毎に探し直す
        if (activateCheck.Checked && ++targetRefreshTick >= 20)
        {
            targetRefreshTick = 0;
            injector.TargetWindow = FindTargetWindow(processCombo.Text);
        }
        counterLabel.Text = $"受信 {Interlocked.Read(ref receivedCount)} / 入力 {Interlocked.Read(ref hitCount)}";
    }

    // ------------------------------------------------------------------

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveUiToSettings();
        uiTimer.Stop();
        receiver.Dispose();
        injector.Dispose();
        base.OnFormClosing(e);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static Keys[] BuildSelectableKeys()
    {
        var keys = new List<Keys> { Keys.None };
        for (var k = Keys.A; k <= Keys.Z; k++) keys.Add(k);
        for (var k = Keys.D0; k <= Keys.D9; k++) keys.Add(k);
        for (var k = Keys.NumPad0; k <= Keys.NumPad9; k++) keys.Add(k);
        for (var k = Keys.F1; k <= Keys.F12; k++) keys.Add(k);
        keys.AddRange(new[]
        {
            Keys.Space, Keys.Enter, Keys.Escape, Keys.Tab, Keys.Back,
            Keys.Left, Keys.Up, Keys.Right, Keys.Down,
            Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
            Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey,
            Keys.OemMinus, Keys.Oemplus, Keys.Oemcomma, Keys.OemPeriod,
            Keys.OemQuestion, Keys.Oem1, Keys.Oem7, Keys.OemOpenBrackets, Keys.OemCloseBrackets,
        });
        return keys.ToArray();
    }
}
