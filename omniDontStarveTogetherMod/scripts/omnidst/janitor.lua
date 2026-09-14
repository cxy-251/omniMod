--[[ 防崩管家（联机版）。（modimport 加载，跑在 mod 环境）

  联机版是 64 位引擎，没有单机版那种 32 位内存墙，但用户历来爱崩，还是加一层：
  - 每 45 秒 collectgarbage("collect") —— 稳，主要收益
  - 屏幕左上角显示 Lua 内存 MB + 已玩分钟数，超阈值标红提示存盘重进
  开关：OMNIDST.state.janitor（cheats 模块里），默认开。控制台 omni_janitor(true/false)。
]]

local G = GLOBAL

local function on()
    return not (G.OMNIDST and G.OMNIDST.state) or G.OMNIDST.state.janitor ~= false
end

local MEM_WARN_MB = 700   -- 64 位，阈值放宽
local hud, t_start

local function ensure_hud()
    local p = G.ThePlayer
    if not (p and p.HUD and p.HUD.controls) then return end
    if hud and hud.inst and hud.inst:IsValid() then return end
    local Text = G.require("widgets/text")
    hud = p.HUD.controls:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT or G.DEFAULTFONT, 20))
    hud:SetHAnchor(G.ANCHOR_LEFT)
    hud:SetVAnchor(G.ANCHOR_TOP)
    hud:SetRegionSize(340, 40)
    hud:SetHAlign(G.ANCHOR_LEFT)
    hud:SetPosition(185, -28, 0)
end

local function tick()
    if t_start == nil then t_start = (G.GetTime and G.GetTime()) or 0 end
    if not on() then if hud then hud:Hide() end return end
    collectgarbage("collect")
    ensure_hud()
    if not hud then return end
    local mb = collectgarbage("count") / 1024
    local mins = math.floor((((G.GetTime and G.GetTime()) or 0) - t_start) / 60)
    hud:Show()
    if mb > MEM_WARN_MB then
        hud:SetColour(1, 0.45, 0.45, 0.9)
        hud:SetString(string.format("Lua %.0f MB · %d 分 · 建议存盘重进", mb, mins))
    else
        hud:SetColour(1, 1, 1, 0.6)
        hud:SetString(string.format("Lua %.0f MB · %d 分", mb, mins))
    end
end

AddSimPostInit(function()
    if not (G.TheWorld and G.TheWorld.DoPeriodicTask) then return end
    G.TheWorld:DoPeriodicTask(45, tick)
    G.TheWorld:DoTaskInTime(5, tick)
end)

print("[omnidst/janitor] 已加载（45s GC + 左上角内存显示）")
