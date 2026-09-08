--[[ 「你正看着的东西」信息条：生物血量/攻击 + 食物 饥/血/智/鲜。
     （modimport 加载，跑在 mod 环境）

  ★ 联机版 widgets/hoverer 是鼠标专用的（手柄下整个隐藏）。Steam Deck 手柄玩
    必须读 playercontroller.controller_target。这里做一个 HUD 固定信息条：
    屏幕底部偏上、物品栏上方，深色底 + 白字，手柄和鼠标都管用。always-on。

  生物血量受 OMNIDST.state.hpbar 开关；食物数值常显。
]]

local G = GLOBAL

local function hpbar_on()
    return not (G.OMNIDST and G.OMNIDST.state) or G.OMNIDST.state.hpbar ~= false
end

local function r1(x) return math.floor((x or 0) * 10 + 0.5) / 10 end

local function build_text(t)
    if not (t and t.components) then return nil end
    local p = G.ThePlayer
    local parts = {}

    -- 名字
    local name = (t.GetDisplayName and t:GetDisplayName()) or t.name
    if type(name) == "string" then
        name = name:gsub("\n.*$", "")   -- 去掉 GetDisplayName 可能带的第二行说明
        parts[#parts + 1] = name
    end

    -- 生物血量 / 攻击
    if hpbar_on() and t.components.health then
        local hc = t.components.health
        local cur = hc.currenthealth or (hc.GetCurrent and hc:GetCurrent())
        local mx  = hc.maxhealth or (hc.GetMaxWithPenalty and hc:GetMaxWithPenalty())
        if cur and mx and mx > 0 then
            local atk = t.components.combat and t.components.combat.defaultdamage or 0
            parts[#parts + 1] = string.format("[%d/%d]%s",
                math.floor(cur + 0.5), math.floor(mx + 0.5),
                atk > 0 and ("  攻" .. math.floor(atk)) or "")
        end
    end

    -- 食物数值
    local e = t.components.edible
    if e then
        local hu = (p and e.GetHunger and r1(e:GetHunger(p))) or r1(e.hungervalue)
        local he = (p and e.GetHealth and r1(e:GetHealth(p))) or r1(e.healthvalue)
        local sa = (p and e.GetSanity and r1(e:GetSanity(p))) or r1(e.sanityvalue)
        local seg = {}
        if hu ~= 0 then seg[#seg + 1] = "饥" .. (hu > 0 and "+" or "") .. hu end
        if he ~= 0 then seg[#seg + 1] = "血" .. (he > 0 and "+" or "") .. he end
        if sa ~= 0 then seg[#seg + 1] = "智" .. (sa > 0 and "+" or "") .. sa end
        if t.components.perishable then
            seg[#seg + 1] = "鲜" .. math.floor(t.components.perishable:GetPercent() * 100 + 0.5) .. "%"
        end
        if #seg > 0 then parts[#parts + 1] = table.concat(seg, " ") end
    end

    -- 只有名字、没别的信息 -> 不显示（免得挡视野）
    if #parts <= 1 then return nil end
    return table.concat(parts, "   ")
end

local function get_target()
    local p = G.ThePlayer
    local pc = p and p.components and p.components.playercontroller
    if pc then
        local t = pc.controller_target or pc.controller_attack_target
        if t and t:IsValid() then return t end
    end
    if G.TheInput and G.TheInput.GetWorldEntityUnderMouse then
        local t = G.TheInput:GetWorldEntityUnderMouse()
        if t and t:IsValid() then return t end
    end
    return nil
end

local bar_root
local function make_bar()
    if bar_root and bar_root.inst and bar_root.inst:IsValid() then return end
    local p = G.ThePlayer
    if not (p and p.HUD and p.HUD.controls) then return end

    local Text  = G.require("widgets/text")
    local Image = G.require("widgets/image")
    local Widget = G.require("widgets/widget")

    local root = p.HUD.controls:AddChild(Widget("omni_targetinfo"))
    bar_root = root
    root:SetVAnchor(G.ANCHOR_BOTTOM)
    root:SetHAnchor(G.ANCHOR_MIDDLE)
    root:SetPosition(0, 205, 0)

    local bg = root:AddChild(Image("images/global.xml", "square.tex"))
    bg:SetTint(0, 0, 0, 0.6)
    bg:SetClickable(false)

    local txt = root:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT or G.DEFAULTFONT, 22))
    txt:SetColour(1, 1, 1, 1)

    p.HUD.inst:DoPeriodicTask(0, function()
        if not (root.inst and root.inst:IsValid()) then return end
        local s = build_text(get_target())
        if s == nil or s == "" then
            root:Hide()
            return
        end
        txt:SetString(s)
        local w, h = txt:GetRegionSize()
        bg:SetSize((w or 40) + 24, (h or 22) + 12)
        root:Show()
    end)
end

AddPlayerPostInit(function(p)
    p:DoTaskInTime(1, make_bar)
    p:DoTaskInTime(4, make_bar)
end)

print("[omnidst/targetinfo] 目标信息条已加载")
