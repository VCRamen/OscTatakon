# OscTatakon の動作確認用: OSC メッセージ (int32 引数 1 個) を UDP で送る
# 例) .\SendOscTest.ps1 -Address /taiko/don/left -Count 10 -IntervalMs 100 -DelaySec 3
param(
    [string]$Address = "/taiko/don/left",
    [int]$Value = 1,
    [int]$Port = 18100,
    [int]$Count = 1,
    [int]$IntervalMs = 200,
    [int]$DelaySec = 3
)

function ConvertTo-OscString([string]$text) {
    $raw = [System.Text.Encoding]::UTF8.GetBytes($text)
    $size = [int]([Math]::Floor($raw.Length / 4) + 1) * 4
    $bytes = New-Object byte[] $size
    [Array]::Copy($raw, $bytes, $raw.Length)
    return ,$bytes
}

$valueBytes = [BitConverter]::GetBytes([int]$Value)
[Array]::Reverse($valueBytes)
[byte[]]$packet = (ConvertTo-OscString $Address) + (ConvertTo-OscString ",i") + $valueBytes

$udp = New-Object System.Net.Sockets.UdpClient
try {
    Write-Host "$DelaySec 秒後に送信します。ゲームのウィンドウを前面にしてください。"
    Start-Sleep -Seconds $DelaySec
    for ($i = 0; $i -lt $Count; $i++) {
        [void]$udp.Send($packet, $packet.Length, "127.0.0.1", $Port)
        Start-Sleep -Milliseconds $IntervalMs
    }
    Write-Host "送信完了: $Address x $Count"
}
finally {
    $udp.Close()
}
