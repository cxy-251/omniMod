--[[ 内存显示（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  只显示，不干预：屏幕左上角显示 Lua 内存 MB + 已玩分钟数，超过阈值提示
  "建议存盘重进"，纯参考，方便你自己判断要不要手动存盘重开。

  ★ 以前这里还做过"每 30 秒强制 collectgarbage" + "每 3 分钟清远处垃圾实体"，
  已经去掉了——内存管理、垃圾实体回收本来就是游戏引擎自己的活，没必要 mod
  再插一手重复做一遍，尤其是强制高频度整体 GC 这种跟游戏自己内存节奏不同步的
  操作，怀疑是这几次莫名其妙的引擎崩溃（钓鱼、噩梦裂隙那两次）的诱因之一。

  开关：state.janitor（cheats 模块里），默认开。控制台 omni_janitor(true/false)。
]]

local G = GLOBAL

local function on()
    return not (G.OMNIDSM and G.OMNIDSM.state) or G.OMNIDSM.state.janitor ~= false
end

local MEM_WARN_MB = 200
local hud

local function ensure_hud()
    local p = G.GetPlayer and G.GetPlayer()
    if not p or not p.HUD or not p.HUD.controls then return end
    if hud and hud.inst and hud.inst:IsValid() then return end
    local Text = G.require("widgets/text")
    hud = p.HUD.controls:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT or G.DEFAULTFONT, 20))
    hud:SetHAnchor(G.ANCHOR_LEFT)
    hud:SetVAnchor(G.ANCHOR_TOP)
    hud:SetRegionSize(320, 40)
    hud:SetHAlign(G.ANCHOR_LEFT)
    hud:SetPosition(175, -26, 0)
end

local t_start = nil

local function tick()
    if t_start == nil then t_start = (G.GetTime and G.GetTime()) or 0 end
    if not on() then
        if hud then hud:Hide() end
        return
    end
    ensure_hud()
    if hud then
        local mb = collectgarbage("count") / 1024
        local mins = math.floor((((G.GetTime and G.GetTime()) or 0) - t_start) / 60)
        hud:Show()
        if mb > MEM_WARN_MB then
            hud:SetColour(1, 0.45, 0.45, 0.9)
            hud:SetString(string.format("Lua %.0f MB · %d 分 · 建议存盘重进", mb, mins))
        else
            hud:SetColour(1, 1, 1, 0.7)
            hud:SetString(string.format("Lua %.0f MB · %d 分", mb, mins))
        end
    end
end

AddSimPostInit(function()
    local w = G.GetWorld and G.GetWorld()
    if not (w and w.DoPeriodicTask) then return end
    w:DoPeriodicTask(5, tick)
    w:DoTaskInTime(3, tick)
end)

print("[omnidsm/janitor] 已加载（只显示左上角内存/时长，不再强制 GC / 清垃圾）")
