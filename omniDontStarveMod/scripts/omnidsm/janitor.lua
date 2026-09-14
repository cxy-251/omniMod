--[[ 防崩管家。（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  单机饥荒是 32 位引擎，有内存墙，长时间跑图容易卡死闪退。这个：
  - 每 30 秒 collectgarbage —— 主要收益，绝对安全
  - 每 3 分钟清一遍远处的纯垃圾（FX 残留 / ash / 远处彻底腐烂的东西）—— 很保守，不碰
    掉落资源、不碰建筑、不碰生物
  - 屏幕左上角显示 Lua 内存 MB + 已玩分钟数，超过阈值提示"建议存盘重进"

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
    collectgarbage("collect")
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

local SWEEP_PREFABS = { ash = true }
local function sweep()
    if not on() then return end
    local p = G.GetPlayer and G.GetPlayer()
    if not (p and p.Transform and G.TheSim) then return end
    local px, py, pz = p.Transform:GetWorldPosition()
    local removed = 0
    -- 全图扫，只删远处（>50）的纯垃圾
    for _, e in ipairs(G.TheSim:FindEntities(px, py, pz, 2000)) do
        if e ~= p and e:IsValid() and e.entity and e.Transform then
            local ex, _, ez = e.Transform:GetWorldPosition()
            local dsq = (ex - px) * (ex - px) + (ez - pz) * (ez - pz)
            if dsq > 50 * 50 then
                local kill = false
                if e.persists == false and e:HasTag("FX") then
                    kill = true
                elseif SWEEP_PREFABS[e.prefab] then
                    kill = true
                elseif e.components and e.components.perishable and e.components.perishable:GetPercent() <= 0
                       and e.components.inventoryitem and e.components.inventoryitem.owner == nil
                       and not e:HasTag("irreplaceable") then
                    kill = true   -- 远处彻底烂掉、没人要的东西
                end
                if kill then e:Remove(); removed = removed + 1 end
            end
        end
    end
    if removed > 0 then print("[omnidsm/janitor] 清理远处垃圾 " .. removed .. " 个") end
end

AddSimPostInit(function()
    local w = G.GetWorld and G.GetWorld()
    if not (w and w.DoPeriodicTask) then return end
    w:DoPeriodicTask(30, tick)
    w:DoPeriodicTask(180, sweep)
    w:DoTaskInTime(3, tick)
end)

print("[omnidsm/janitor] 已加载（30s GC / 3min 清垃圾 / 左上角内存显示）")
