--[[ 「你正看着/选中的东西」信息条：生物血量/攻击 + 食物 饥/血/智/鲜。
     （modimport 加载，跑在 mod 环境）

  联机版 widgets/hoverer 是鼠标专用（手柄下整个隐藏），所以做一个 HUD 固定信息条。
  目标来源（按优先级）：
    1. 物品栏里手柄/鼠标选中或悬停的格子（背包食物）
    2. 手柄准星目标 / 攻击目标（生物）
    3. 鼠标世界目标
    4. 兜底：面前 5 格内最近的「可吃」实体（手柄看地上食物时 controller_target 常抓不到）
  always-on。生物血量受 OMNIDST.state.hpbar 开关；食物数值常显。
]]

local G = GLOBAL

local function hpbar_on()
    return not (G.OMNIDST and G.OMNIDST.state) or G.OMNIDST.state.hpbar ~= false
end
local function r1(x) return math.floor((x or 0) * 10 + 0.5) / 10 end

local function usable(t, allow_held)
    if not (t and t:IsValid() and t.components) then return false end
    if t == G.ThePlayer then return false end
    if not allow_held and (t:HasTag("INLIMBO") or (t.components.inventoryitem and t.components.inventoryitem:IsHeld())) then
        return false
    end
    return true
end

local function get_target()
    local p = G.ThePlayer
    if not (p and p:IsValid()) then return nil end

    -- 1) 世界目标优先：手柄准星 / 攻击目标（生物、地上物），滤掉手持/装备
    local pc = p.components and p.components.playercontroller
    if pc then
        for _, t in ipairs({ pc.controller_target, pc.controller_attack_target }) do
            if usable(t, false) then return t end
        end
    end

    -- 2) 鼠标世界目标
    if G.TheInput and G.TheInput.GetWorldEntityUnderMouse then
        local t = G.TheInput:GetWorldEntityUnderMouse()
        if usable(t, false) then return t end
    end

    -- 3) 物品栏里「当前聚焦/悬停」的格子（键鼠悬停 或 手柄把光标移到某格上）
    local inv = p.HUD and p.HUD.controls and p.HUD.controls.inv
    if inv then
        local it
        if inv.hovertile and inv.hovertile.item then
            it = inv.hovertile.item                                  -- 鼠标悬停
        elseif inv.active_slot and inv.active_slot.focus            -- 手柄光标确实停在这格上
               and inv.active_slot.tile and inv.active_slot.tile.item then
            it = inv.active_slot.tile.item
        end
        if usable(it, true) then return it end
    end

    -- 5) 兜底：面前最近的可吃实体（只找食物，不找生物）
    if p.Transform and G.TheSim then
        local x, y, z = p.Transform:GetWorldPosition()
        local best, bestd
        for _, e in ipairs(G.TheSim:FindEntities(x, y, z, 5, nil, { "INLIMBO", "FX", "DECOR", "NOCLICK", "player" })) do
            if e ~= p and e.components and e.components.edible and e.Transform then
                local ex, _, ez = e.Transform:GetWorldPosition()
                local d = (ex - x) * (ex - x) + (ez - z) * (ez - z)
                if not bestd or d < bestd then best, bestd = e, d end
            end
        end
        if best then return best end
    end
    return nil
end

local function build_text(t)
    if not (t and t.components) then return nil end
    local p = G.ThePlayer
    local parts, has_info = {}, false

    local name = (t.GetDisplayName and t:GetDisplayName()) or t.name
    if type(name) == "string" then
        name = name:gsub("\n.*$", "")
        parts[#parts + 1] = name
    end

    if hpbar_on() and t.components.health then
        local hc = t.components.health
        local cur = hc.currenthealth or (hc.GetCurrent and hc:GetCurrent())
        local mx  = hc.maxhealth or (hc.GetMaxWithPenalty and hc:GetMaxWithPenalty())
        if cur and mx and mx > 0 then
            local atk = t.components.combat and t.components.combat.defaultdamage or 0
            parts[#parts + 1] = string.format("[%d/%d]%s", math.floor(cur + 0.5), math.floor(mx + 0.5),
                atk > 0 and ("  攻" .. math.floor(atk)) or "")
            has_info = true
        end
    end

    local e = t.components.edible
    if e then
        local hu = (p and e.GetHunger and r1(e:GetHunger(p))) or r1(e.hungervalue)
        local he = (p and e.GetHealth and r1(e:GetHealth(p))) or r1(e.healthvalue)
        local sa = (p and e.GetSanity and r1(e:GetSanity(p))) or r1(e.sanityvalue)
        local seg = {}
        seg[#seg + 1] = "饥" .. (hu >= 0 and "+" or "") .. hu
        seg[#seg + 1] = "血" .. (he >= 0 and "+" or "") .. he
        seg[#seg + 1] = "智" .. (sa >= 0 and "+" or "") .. sa
        if t.components.perishable then
            seg[#seg + 1] = "鲜" .. math.floor(t.components.perishable:GetPercent() * 100 + 0.5) .. "%"
        end
        parts[#parts + 1] = table.concat(seg, " ")
        has_info = true
    end

    if not has_info then return nil end
    return table.concat(parts, "   ")
end

-- 跟 status.lua 完全一样的挂法：AddClassPostConstruct 到 widgets/controls，
-- 直接往 controls 上加 Image + Text，用 line.inst:DoPeriodicTask 刷新。
local _dbg = 0
AddClassPostConstruct("widgets/controls", function(self)
    local Text  = G.require("widgets/text")
    local Image = G.require("widgets/image")

    -- 放屏幕顶部、状态栏那行下面（跟 status.lua 一样锚 TOP，实测能显示）
    local bg = self:AddChild(Image("images/global.xml", "square.tex"))
    bg:SetVAnchor(G.ANCHOR_TOP)
    bg:SetHAnchor(G.ANCHOR_MIDDLE)
    bg:SetPosition(0, -62, 0)
    bg:SetTint(0, 0, 0, 0.62)
    bg:SetClickable(false)
    bg:MoveToFront()

    local line = self:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT or G.DEFAULTFONT, 22))
    line:SetVAnchor(G.ANCHOR_TOP)
    line:SetHAnchor(G.ANCHOR_MIDDLE)
    line:SetPosition(0, -62, 0)
    line:SetColour(1, 1, 1, 1)
    line:MoveToFront()
    self._omni_targetinfo = line

    local t0 = (G.GetTime and G.GetTime()) or 0
    line.inst:DoPeriodicTask(0.1, function()
        if not line.inst:IsValid() then return end
        local tgt = get_target()
        local ok, s = G.pcall(function() return build_text(tgt) end)
        _dbg = _dbg + 1
        if _dbg <= 20 then
            print(("[omnidst/targetinfo] tick %d: tgt=%s  txt=%s"):format(
                _dbg, tostring(tgt and tgt.prefab), tostring(ok and s)))
        end
        -- 开局 12 秒：即使没目标也显示占位，方便确认位置/渲染
        if (not ok or s == nil or s == "") then
            if ((G.GetTime and G.GetTime()) or 0) - t0 < 12 then
                s = "〔目标信息条 · 指向生物/食物〕"
            else
                line:Hide(); bg:Hide()
                return
            end
        end
        line:SetString(s)
        local w, h = line:GetRegionSize()
        bg:SetSize((w or 60) + 28, (h or 22) + 14)
        line:Show(); bg:Show()
    end)
end)

print("[omnidst/targetinfo] 目标信息条已加载")
