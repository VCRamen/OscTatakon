using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OscTatakon;

/// <summary>受信した OSC メッセージ。</summary>
public sealed record OscMessage(string Address, IReadOnlyList<object?> Args);

/// <summary>
/// UDP で OSC メッセージ/バンドルを受信する最小実装。
/// 外部ライブラリには依存しない。
/// </summary>
public sealed class OscReceiver : IDisposable
{
    private UdpClient? udpClient;
    private CancellationTokenSource? cancelSource;

    /// <summary>メッセージ受信時に呼ばれる (受信スレッド上)。</summary>
    public event Action<OscMessage>? MessageReceived;

    /// <summary>受信エラー時に呼ばれる (受信スレッド上)。</summary>
    public event Action<string>? ErrorOccurred;

    public bool IsRunning => udpClient != null;

    public void Start(int port)
    {
        Stop();
        var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        udpClient = client;
        cancelSource = new CancellationTokenSource();
        var token = cancelSource.Token;
        var thread = new Thread(() => ReceiveLoop(client, token))
        {
            IsBackground = true,
            Name = "OscReceiver",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    public void Stop()
    {
        cancelSource?.Cancel();
        udpClient?.Close();
        udpClient = null;
        cancelSource = null;
    }

    public void Dispose() => Stop();

    private void ReceiveLoop(UdpClient client, CancellationToken token)
    {
        var remote = new IPEndPoint(IPAddress.Any, 0);
        while (!token.IsCancellationRequested)
        {
            byte[] data;
            try
            {
                data = client.Receive(ref remote);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException ex)
            {
                if (token.IsCancellationRequested) return;
                // Windows では送信先が居ない ICMP で ConnectionReset が来ることがあるので無視して継続
                if (ex.SocketErrorCode == SocketError.ConnectionReset) continue;
                ErrorOccurred?.Invoke($"受信エラー: {ex.Message}");
                return;
            }

            try
            {
                foreach (var message in OscParser.Parse(data))
                {
                    MessageReceived?.Invoke(message);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"OSC パースエラー: {ex.Message}");
            }
        }
    }
}

/// <summary>OSC 1.0 のパーサ (メッセージ/バンドル対応)。</summary>
public static class OscParser
{
    private const string BUNDLE_HEADER = "#bundle";

    public static List<OscMessage> Parse(byte[] data)
    {
        var result = new List<OscMessage>();
        ParseInto(data, 0, data.Length, result);
        return result;
    }

    private static void ParseInto(byte[] data, int offset, int length, List<OscMessage> result)
    {
        if (length <= 0) return;
        var end = offset + length;

        if (data[offset] == (byte)'#')
        {
            var pos = offset;
            var header = ReadString(data, ref pos, end);
            if (header != BUNDLE_HEADER) return;
            pos += 8; // timetag (即時実行するので無視)
            while (pos + 4 <= end)
            {
                var size = ReadInt32(data, ref pos);
                if (size <= 0 || pos + size > end) break;
                ParseInto(data, pos, size, result);
                pos += size;
            }
            return;
        }

        var p = offset;
        var address = ReadString(data, ref p, end);
        var args = new List<object?>();
        if (p < end && data[p] == (byte)',')
        {
            var typeTags = ReadString(data, ref p, end);
            for (var i = 1; i < typeTags.Length; i++)
            {
                switch (typeTags[i])
                {
                    case 'i': args.Add(ReadInt32(data, ref p)); break;
                    case 'f': args.Add(ReadFloat32(data, ref p)); break;
                    case 'h': args.Add(ReadInt64(data, ref p)); break;
                    case 'd': args.Add(ReadFloat64(data, ref p)); break;
                    case 't': args.Add(ReadInt64(data, ref p)); break;
                    case 's':
                    case 'S': args.Add(ReadString(data, ref p, end)); break;
                    case 'b': args.Add(ReadBlob(data, ref p)); break;
                    case 'c': args.Add((char)ReadInt32(data, ref p)); break;
                    case 'r':
                    case 'm': args.Add(ReadInt32(data, ref p)); break;
                    case 'T': args.Add(true); break;
                    case 'F': args.Add(false); break;
                    case 'N': args.Add(null); break;
                    case 'I': args.Add(double.PositiveInfinity); break;
                    case '[':
                    case ']': break;
                    default: return; // 未知の型: 以降は読めないのでメッセージごと破棄
                }
            }
        }
        result.Add(new OscMessage(address, args));
    }

    private static string ReadString(byte[] data, ref int pos, int end)
    {
        var start = pos;
        while (pos < end && data[pos] != 0) pos++;
        var text = Encoding.UTF8.GetString(data, start, pos - start);
        pos++; // 終端 null
        pos = Align4(pos);
        return text;
    }

    private static byte[] ReadBlob(byte[] data, ref int pos)
    {
        var size = ReadInt32(data, ref pos);
        var blob = new byte[size];
        Array.Copy(data, pos, blob, 0, size);
        pos = Align4(pos + size);
        return blob;
    }

    private static int ReadInt32(byte[] data, ref int pos)
    {
        var value = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
        pos += 4;
        return value;
    }

    private static long ReadInt64(byte[] data, ref int pos)
    {
        long high = (uint)ReadInt32(data, ref pos);
        long low = (uint)ReadInt32(data, ref pos);
        return (high << 32) | low;
    }

    private static float ReadFloat32(byte[] data, ref int pos) =>
        BitConverter.Int32BitsToSingle(ReadInt32(data, ref pos));

    private static double ReadFloat64(byte[] data, ref int pos) =>
        BitConverter.Int64BitsToDouble(ReadInt64(data, ref pos));

    private static int Align4(int pos) => (pos + 3) & ~3;
}
