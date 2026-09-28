using System.Diagnostics;
using System.Runtime.InteropServices;
using static OscTatakon.NativeMethods;

namespace OscTatakon;

/// <summary>キー入力の送り方。</summary>
public enum InputMethod
{
    /// <summary>SendInput + スキャンコードのみ (DirectInput / RawInput 系のゲーム向け。既定)</summary>
    SendInputScanCode,
    /// <summary>SendInput + 仮想キーコード + スキャンコード</summary>
    SendInputVirtualKey,
    /// <summary>旧 API の keybd_event</summary>
    KeybdEvent,
}

/// <summary>
/// 「押す → 一定時間保持 → 離す」を専用スレッドでスケジューリングしてキー入力を送る。
///
/// ゲームはフレーム単位 (60fps なら約 16.7ms 毎) でキー状態をポーリングすることが多く、
/// 押下と解放を同時に送るとフレームの間に埋もれて入力として認識されない。
/// そのため押下時間 (HoldMs) と、同じキーを連続で叩く時の解放時間 (GapMs) を確保する。
/// </summary>
public sealed class KeyInjector : IDisposable
{
    /// <summary>これ以上遅れる入力は捨てる (連打が溜まり続けるのを防ぐ)。</summary>
    private const double MAX_QUEUE_DELAY_MS = 250.0;

    private sealed record ScheduledAction(double TimeMs, Keys Key, bool IsDown, long Sequence);

    private readonly object lockObject = new();
    private readonly List<ScheduledAction> actions = new();
    private readonly Dictionary<Keys, double> keyFreeTimeMs = new();
    private readonly AutoResetEvent wakeEvent = new(false);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Thread worker;
    private volatile bool isRunning = true;
    private long sequence;

    public InputMethod Method { get; set; } = InputMethod.SendInputScanCode;
    public int HoldMs { get; set; } = 30;
    public int GapMs { get; set; } = 20;

    /// <summary>入力前にアクティブにするウィンドウ (0 なら何もしない)。</summary>
    public IntPtr TargetWindow { get; set; } = IntPtr.Zero;

    /// <summary>送信結果などのログ (ワーカースレッド上で呼ばれる)。</summary>
    public event Action<string>? Log;

    public KeyInjector()
    {
        timeBeginPeriod(1); // Sleep / Wait の分解能を 1ms にする
        worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "KeyInjector",
            Priority = ThreadPriority.Highest,
        };
        worker.Start();
    }

    /// <summary>キーを 1 回叩く (押して HoldMs 後に離す)。</summary>
    public void Hit(Keys key)
    {
        lock (lockObject)
        {
            var now = clock.Elapsed.TotalMilliseconds;
            var downTime = now;
            if (keyFreeTimeMs.TryGetValue(key, out var freeTime) && freeTime + GapMs > now)
            {
                downTime = freeTime + GapMs;
            }
            if (downTime - now > MAX_QUEUE_DELAY_MS)
            {
                return;
            }
            var upTime = downTime + Math.Max(1, HoldMs);
            keyFreeTimeMs[key] = upTime;
            actions.Add(new ScheduledAction(downTime, key, true, sequence++));
            actions.Add(new ScheduledAction(upTime, key, false, sequence++));
        }
        wakeEvent.Set();
    }

    private void WorkerLoop()
    {
        while (isRunning)
        {
            ScheduledAction? next = null;
            var waitMs = Timeout.Infinite;
            lock (lockObject)
            {
                if (actions.Count > 0)
                {
                    next = actions.MinBy(a => (a.TimeMs, a.Sequence));
                    var remain = next!.TimeMs - clock.Elapsed.TotalMilliseconds;
                    if (remain <= 0.5)
                    {
                        actions.Remove(next);
                    }
                    else
                    {
                        waitMs = (int)Math.Ceiling(remain);
                        next = null;
                    }
                }
            }

            if (next == null)
            {
                wakeEvent.WaitOne(waitMs);
                continue;
            }

            try
            {
                if (next.IsDown) ActivateTargetWindowIfNeeded();
                SendKey(next.Key, next.IsDown);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"送信エラー: {ex.Message}");
            }
        }
    }

    /// <summary>指定の方式でキーの押下/解放を 1 回送る。</summary>
    public void SendKey(Keys key, bool isDown)
    {
        var vk = (uint)key & 0xFF;
        var scanEx = MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX);
        var scan = (ushort)(scanEx & 0xFF);
        var isExtended = (scanEx & 0xFF00) == 0xE000 || (scanEx & 0xFF00) == 0xE100;

        switch (Method)
        {
            case InputMethod.SendInputScanCode:
            case InputMethod.SendInputVirtualKey:
            {
                var flags = 0u;
                if (!isDown) flags |= KEYEVENTF_KEYUP;
                if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;
                var useScanOnly = Method == InputMethod.SendInputScanCode;
                if (useScanOnly) flags |= KEYEVENTF_SCANCODE;

                var inputs = new[]
                {
                    new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        u = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = useScanOnly ? (ushort)0 : (ushort)vk,
                                wScan = scan,
                                dwFlags = flags,
                                time = 0,
                                dwExtraInfo = IntPtr.Zero,
                            },
                        },
                    },
                };
                var sent = SendInput(1, inputs, Marshal.SizeOf<INPUT>());
                if (sent != 1)
                {
                    var error = Marshal.GetLastWin32Error();
                    Log?.Invoke($"SendInput 失敗 (key={key}, error={error})。" +
                                "ゲームが管理者権限で動いている場合は本アプリも管理者として実行してください。");
                }
                break;
            }
            case InputMethod.KeybdEvent:
            {
                var flags = 0u;
                if (!isDown) flags |= KEYEVENTF_KEYUP;
                if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;
                keybd_event((byte)vk, (byte)scan, flags, UIntPtr.Zero);
                break;
            }
        }
    }

    private void ActivateTargetWindowIfNeeded()
    {
        var target = TargetWindow;
        if (target == IntPtr.Zero) return;
        var foreground = GetForegroundWindow();
        if (foreground == target) return;

        // 他プロセスのウィンドウを前面化するため、前面スレッドに一時的にアタッチする
        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
                       AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            if (IsIconic(target)) ShowWindow(target, SW_RESTORE);
            BringWindowToTop(target);
            SetForegroundWindow(target);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    /// <summary>押しっぱなしのキーを全て離す。</summary>
    public void ReleaseAll()
    {
        List<Keys> keys;
        lock (lockObject)
        {
            keys = actions.Where(a => !a.IsDown).Select(a => a.Key).Distinct().ToList();
            actions.Clear();
            keyFreeTimeMs.Clear();
        }
        foreach (var key in keys)
        {
            SendKey(key, false);
        }
    }

    public void Dispose()
    {
        isRunning = false;
        wakeEvent.Set();
        worker.Join(500);
        ReleaseAll();
        timeEndPeriod(1);
        wakeEvent.Dispose();
    }
}
