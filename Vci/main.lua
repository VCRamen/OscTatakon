-- OscTatakon 用 VCI サンプル
-- 太鼓の面 (SubItem) が叩かれたら、OSC でローカルの OscTatakon に通知する。
--
-- 前提となる VCI の構成 (名前は下の SUB_ITEM_TO_ADDRESS と合わせる):
--   SubItem "KaLeft" / "DonLeft" / "DonRight" / "KaRight" : 太鼓の各面 (Collider 付き)
--   叩く側 (バチ) のコライダー名は STICK_COLLIDER_NAMES に列挙
--
-- OSC はローカル (127.0.0.1) にのみ送信され、送信先ポートは
-- バーチャルキャストのタイトル画面 > 詳細設定 > VCI の「OSC 送信ポート」(既定 18100)。

-- SubItem 名 → OSC アドレス
local SUB_ITEM_TO_ADDRESS = {
    KaLeft = "/taiko/ka/left",
    DonLeft = "/taiko/don/left",
    DonRight = "/taiko/don/right",
    KaRight = "/taiko/ka/right",
}

-- 叩く側として扱うコライダー名
local STICK_COLLIDER_NAMES = {
    StickLeft = true,
    StickRight = true,
}

-- 同じ面の多重ヒットを抑制する時間 (秒)
local HIT_COOLDOWN_SEC = 0.05

local lastHitTimes = {}

local function SendHit(subItemName)
    local address = SUB_ITEM_TO_ADDRESS[subItemName]
    if address == nil then
        return
    end

    local now = os.clock()
    local lastTime = lastHitTimes[subItemName]
    if lastTime ~= nil and now - lastTime < HIT_COOLDOWN_SEC then
        return
    end
    lastHitTimes[subItemName] = now

    vci.osc.SendInt32(address, 1)
end

-- バチが面に当たった時
function onCollisionEnter(item, hit)
    -- 全員の環境でコールバックが走るので、所有者の環境からだけ送る
    if not vci.assets.IsMine then
        return
    end
    if STICK_COLLIDER_NAMES[hit] then
        SendHit(item)
    end
end

-- 面を掴んでトリガーを引いた時 (動作確認用)
function onUse(use)
    SendHit(use)
end
