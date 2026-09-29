# OscTatakon

OSC メッセージを受信して、キーボード入力 (キーの押下 → 離す) に変換する Windows 用フォームアプリです。
Steam 版「太鼓の達人」を、バーチャルキャストの VCI から OSC 経由で叩けるようにすることを目的としています。

```
[VCI (main.lua)] --OSC/UDP 18100--> [OscTatakon] --SendInput--> [太鼓の達人 (前面ウィンドウ)]
```

## 必要なもの

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (ビルド用。実行だけなら .NET 8 Desktop Runtime)

## ビルド / 実行

```powershell
cd OscTatakon
dotnet run -c Release
```

単体 exe にする場合:

```powershell
dotnet publish OscTatakon -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Visual Studio 2022 で `OscTatakon/OscTatakon.csproj` を開いてもビルドできます。

## 使い方

1. OscTatakon を起動し「受信開始」を押す (既定ポート 18100 = バーチャルキャストの OSC 送信ポート既定値)。
2. 太鼓の達人を起動し、演奏画面のウィンドウをクリックして **前面 (アクティブ)** にしておく。
3. VCI から OSC が届くと、割り当てたキーが押されます。

### 既定の割り当て

ゲーム既定の D/F/J/K は WASD 操作と被るため、**ゲーム側のキー設定でカッを R / U に変更してください**。

| 名前 | OSC アドレス | キー | +Space | 押しっぱなし |
|---|---|---|---|---|
| ドン(右) | `/taiko/don/right` | J | ON | |
| ドン(左) | `/taiko/don/left` | F | ON | |
| カッ(右) | `/taiko/ka/right` | U | | |
| カッ(左) | `/taiko/ka/left` | R | | |
| 曲選択 ↑ | `/taiko/menu/up` | ↑ | | ON |
| 曲選択 ↓ | `/taiko/menu/down` | ↓ | | ON |
| 曲選択 → | `/taiko/menu/right` | → | | ON |
| 曲選択 ← | `/taiko/menu/left` | ← | | ON |
| 戻る/オプション | `/taiko/menu/back` | BackSpace | | |

- 表で自由に追加・変更できます (設定は exe と同じフォルダの `settings.json` に保存)。
- 「押しっぱなし」にチェックを入れた行は、引数 1 (true) で押して 0 (false) で離すまでキーを押し続けます。解放が 10 秒届かない場合は自動で離します。
- 「割り当てを初期値に戻す」で上の表の状態に戻せます。
- 「+Space」にチェックを入れた行は、そのキーと同時に Space キーも押します (ドンの行に付けると、ドン入力で Space も押されます)。
- 引数なし、または先頭引数が 0 / false 以外のメッセージを「1 回叩いた」とみなします。
- 未割り当てのアドレスもログに出るので、VCI 側のアドレス確認に使えます。

### 動作確認

- 表の「3秒後」ボタン / 「3秒後に全キーをテスト」ボタンを押してから 3 秒以内にゲームのウィンドウをクリックすると、そのキーが入力されます。
- VCI なしで OSC 経由の確認をする場合は PowerShell で:
  ```powershell
  .\Tools\SendOscTest.ps1 -Address /taiko/don/left -Count 10 -IntervalMs 150
  ```

## 入力が効かない時のチェックポイント

これまで入力がうまく受け付けられなかった原因として多いものと、このアプリでの対策です。

| 原因 | 対策 |
|---|---|
| 押下と解放を同時に送っている。ゲームはフレーム毎 (約 16.7ms) にキー状態を見るので、その間に押して離すと「押されていない」扱いになる | 押下後 **押下時間 (既定 30ms)** 保持してから離す。同じキーの連打は **間隔 (既定 20ms)** を空ける。効かなければ押下時間を 40〜50ms に上げてみる |
| 仮想キーコードだけ送っている。DirectInput / Raw Input 系のゲームはスキャンコードを見る | 既定で `SendInput` + `KEYEVENTF_SCANCODE` (実キーボードと同じスキャンコード) を送る。方式は切り替え可能 |
| `INPUT` 構造体のサイズ違いで `SendInput` が黙って失敗している | union を正しく定義し、失敗時はエラーコードをログに出す |
| ゲームが前面ウィンドウでない。`SendInput` は前面ウィンドウにしか届かない | ゲームをクリックして前面に。または「入力前に対象ウィンドウを前面化」をオンにしてプロセスを選ぶ |
| ゲームが管理者権限で動いている (UIPI により低い権限からの入力は破棄される) | OscTatakon も「管理者として実行」する (タイトルに `[管理者]` と表示されます) |
| IME が ON で文字入力として吸われている | IME をオフ (半角英数) にする |

## VCI 側

`Vci/main.lua` がサンプルです。太鼓の面の SubItem にバチが当たったら `vci.osc.SendInt32(アドレス, 1)` で送信します。

- OSC 送信はローカル (127.0.0.1) のみ。送信ポートはバーチャルキャストのタイトル画面 > 詳細設定 > VCI で設定 (既定 18100)。
- 衝突のコールバックは全員の環境で呼ばれるため、`vci.assets.IsMine` で所有者の環境からのみ送信しています。
