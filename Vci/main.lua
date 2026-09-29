-- OscTatakon 用 VCI
-- バチ (StickRoot1 / StickRoot2) が太鼓の判定コライダーに入ったら、
-- OSC でローカルの OscTatakon に通知してキー入力させる。
--
--   StickRoot1: ドン → J (/taiko/don/right), カッ → U (/taiko/ka/right)
--   StickRoot2: ドン → F (/taiko/don/left),  カッ → R (/taiko/ka/left)
--   曲選択ボタン (どちらのバチでも):
--     KaUp → ↑, KaDown → ↓, KaRight → →, KaLeft → ←, BSOptionButton → BackSpace
--     TabOptionButton → Tab (一時停止)
--     上下左右はバチが触れている間 (onTriggerEnter 〜 onTriggerExit) 押しっぱなし
--     (触れた時に 1、離れた時に 0 を送る)
--   (キーの割り当ては OscTatakon 側の設定)
--
-- OSC はローカル (127.0.0.1) にのみ送信され、送信先ポートは
-- バーチャルキャストのタイトル画面 > 詳細設定 > VCI の「OSC 送信ポート」(既定 18100)。

local DON_COLLIDER_NAME = "DonCollider"
local KA_COLLIDER_NAME = "KaCollider"

-- バチの SubItem 名 → 判定種別ごとの OSC アドレス
local STICK_SETTINGS = {
    StickTip1 = {
        don = "/taiko/don/right", -- J
        ka = "/taiko/ka/right",   -- U
    },
    StickTip2 = {
        don = "/taiko/don/left",  -- F
        ka = "/taiko/ka/left",    -- R
    },
}

-- 曲選択ボタンのコライダー名 → OSC アドレス
-- isHold = true のボタンは、触れている間押しっぱなしにする
local BUTTON_SETTINGS = {
    { colliderName = "KaUp", address = "/taiko/menu/up", isHold = true },          -- ↑
    { colliderName = "KaDown", address = "/taiko/menu/down", isHold = true },      -- ↓
    { colliderName = "KaRight", address = "/taiko/menu/right", isHold = true },    -- →
    { colliderName = "KaLeft", address = "/taiko/menu/left", isHold = true },      -- ←
    { colliderName = "BSOptionButton", address = "/taiko/menu/back", isHold = false }, -- BackSpace
    { colliderName = "TabOptionButton", address = "/taiko/menu/pause", isHold = false }, -- Tab (一時停止)
}

-- 同じバチの多重ヒット (複数の KaCollider を跨いだ時など) を抑制する時間 (秒)
local HIT_COOLDOWN_SEC = 0.06

-- 同じボタンの多重ヒットを抑制する時間 (秒)。短いと 2 回入力されることがある
-- (押しっぱなしのボタンには使わない)
local BUTTON_COOLDOWN_SEC = 0.3

local stickTransforms = {}
local lastHitTimes = {}
local lastButtonTimes = {}

-- 押しっぱなしボタンごとの「今触れているコライダーの数」
-- (両方のバチ、または複数のコライダーが同時に触れても 1 回の押下として扱う)
local holdTouchCounts = {}

local kaSnapObj = vci.assets.GetTransform("SuapKa")
local taikoObj = vci.assets.GetTransform("TataconRoot")

local snaps = {
    {vci.assets.GetTransform("StickTip1"), vci.assets.GetTransform("StickRoot1") },
    {vci.assets.GetTransform("StickTip2"), vci.assets.GetTransform("StickRoot2") }
}

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

-- コライダー名から曲選択ボタンの設定を返す。該当しなければ nil
local function GetButtonSetting(colliderName)
    if colliderName == nil then
        return nil
    end
    for _, buttonSetting in ipairs(BUTTON_SETTINGS) do
        if string.find(colliderName, buttonSetting.colliderName, 1, true) ~= nil then
            return buttonSetting
        end
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

local function SendButton(buttonSetting)
    if buttonSetting.isHold then
        local touchCount = holdTouchCounts[buttonSetting.colliderName] or 0
        holdTouchCounts[buttonSetting.colliderName] = touchCount + 1
        if touchCount == 0 then
            vci.osc.SendInt32(buttonSetting.address, 1)
        end
        return
    end

    local now = GetNowSec()
    local lastTime = lastButtonTimes[buttonSetting.colliderName]
    if lastTime ~= nil and now - lastTime < BUTTON_COOLDOWN_SEC then
        return
    end
    lastButtonTimes[buttonSetting.colliderName] = now

    vci.osc.SendInt32(buttonSetting.address, 1)
end

local function ReleaseButton(buttonSetting)
    if not buttonSetting.isHold then
        return
    end

    local touchCount = holdTouchCounts[buttonSetting.colliderName] or 0
    if touchCount <= 0 then
        return
    end
    holdTouchCounts[buttonSetting.colliderName] = touchCount - 1
    if touchCount == 1 then
        vci.osc.SendInt32(buttonSetting.address, 0)
    end
end

-- onTriggerEnter / onTriggerExit の引数からバチ名と相手のコライダー名を取り出す
local function ResolveStickAndCollider(item, hit)
    -- バチ側が item でも hit でも拾えるように両方向チェックする
    if STICK_SETTINGS[item] ~= nil then
        return item, hit
    end
    if STICK_SETTINGS[hit] ~= nil then
        return hit, item
    end
    return nil, nil
end

function updateAll()
    kaSnapObj.SetPosition(taikoObj.GetPosition())
    kaSnapObj.SetRotation(taikoObj.GetRotation())
    kaSnapObj.SetLocalScale(taikoObj.GetLocalScale())

    for snapID, snap in ipairs(snaps) do
        snap[1].SetPosition(snap[2].GetPosition())
        snap[1].SetRotation(snap[2].GetRotation())
    end
end

function onUse(use)
    if use == "StickRoot1" then
        vci.assets.GetTransform("StickRoot1Collider").SetActive(false)
        print("コライダー消し１")
    end
    if use == "StickRoot2" then
        vci.assets.GetTransform("StickRoot2Collider").SetActive(false)
        print("コライダー消し２")
    end

    if use == "TataconRoot" then
        --vci.assets.GetTransform("StickRoot1Collider").SetActive(true)
        --vci.assets.GetTransform("StickRoot2Collider").SetActive(true)
    end
end

-- item: トリガー判定が発生した SubItem 名, hit: 当たった相手のコライダー名
function onTriggerEnter(item, hit)
    local stickName, colliderName = ResolveStickAndCollider(item, hit)
    if stickName == nil or not IsMyStick(stickName) then
        return
    end

    local buttonSetting = GetButtonSetting(colliderName)
    if buttonSetting ~= nil then
        SendButton(buttonSetting)
        return
    end

    local hitType = GetHitType(colliderName)
    if hitType == nil then
        return
    end

    SendHit(stickName, hitType)
end

-- 押しっぱなしボタンからバチが離れたらキーを離す
function onTriggerExit(item, hit)
    local stickName, colliderName = ResolveStickAndCollider(item, hit)
    if stickName == nil or not IsMyStick(stickName) then
        return
    end

    local buttonSetting = GetButtonSetting(colliderName)
    if buttonSetting ~= nil then
        ReleaseButton(buttonSetting)
    end
end
