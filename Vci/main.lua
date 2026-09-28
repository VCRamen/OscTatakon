-- OscTatakon 用 VCI
-- バチ (StickRoot1 / StickRoot2) が太鼓の判定コライダーに入ったら、
-- OSC でローカルの OscTatakon に通知してキー入力させる。
--
--   StickRoot1: ドン → J (/taiko/don/right), カッ → K (/taiko/ka/right)
--   StickRoot2: ドン → F (/taiko/don/left),  カッ → D (/taiko/ka/left)
--
-- OSC はローカル (127.0.0.1) にのみ送信され、送信先ポートは
-- バーチャルキャストのタイトル画面 > 詳細設定 > VCI の「OSC 送信ポート」(既定 18100)。

local DON_COLLIDER_NAME = "DonCollider"
local KA_COLLIDER_NAME = "KaCollider"

-- バチの SubItem 名 → 判定種別ごとの OSC アドレス
local STICK_SETTINGS = {
    StickRoot1 = {
        don = "/taiko/don/right", -- J
        ka = "/taiko/ka/right",   -- K
    },
    StickRoot2 = {
        don = "/taiko/don/left",  -- F
        ka = "/taiko/ka/left",    -- D
    },
}

-- 同じバチの多重ヒット (複数の KaCollider を跨いだ時など) を抑制する時間 (秒)
local HIT_COOLDOWN_SEC = 0.06

local stickTransforms = {}
local lastHitTimes = {}

for stickName, _ in pairs(STICK_SETTINGS) do
    stickTransforms[stickName] = vci.assets.GetTransform(stickName)
end

-- コライダー名から判定種別 ("don" / "ka") を返す。該当しなければ nil
local function GetHitType(colliderName)
    if colliderName == nil then
        return nil
    end
    if string.find(colliderName, DON_COLLIDER_NAME, 1, true) ~= nil then
        return "don"
    end
    if string.find(colliderName, KA_COLLIDER_NAME, 1, true) ~= nil then
        return "ka"
    end
    return nil
end

-- 自分が持っているバチかどうか (持っている人の環境からだけ送信する)
local function IsMyStick(stickName)
    local stickTransform = stickTransforms[stickName]
    return stickTransform ~= nil and stickTransform.IsMine
end

local function GetNowSec()
    return vci.me.Time.TotalSeconds
end

local function SendHit(stickName, hitType)
    local address = STICK_SETTINGS[stickName][hitType]
    if address == nil then
        return
    end

    local now = GetNowSec()
    local lastTime = lastHitTimes[stickName]
    if lastTime ~= nil and now - lastTime < HIT_COOLDOWN_SEC then
        return
    end
    lastHitTimes[stickName] = now

    vci.osc.SendInt32(address, 1)
end

-- item: トリガー判定が発生した SubItem 名, hit: 当たった相手のコライダー名
function onTriggerEnter(item, hit)
    -- バチ側が item でも hit でも拾えるように両方向チェックする
    local stickName = nil
    local colliderName = nil
    if STICK_SETTINGS[item] ~= nil then
        stickName = item
        colliderName = hit
    elseif STICK_SETTINGS[hit] ~= nil then
        stickName = hit
        colliderName = item
    else
        return
    end

    if not IsMyStick(stickName) then
        return
    end

    local hitType = GetHitType(colliderName)
    if hitType == nil then
        return
    end

    SendHit(stickName, hitType)
end
