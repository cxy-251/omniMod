--[[ 组合状态栏（精简版）。（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  - 把血/饱食/理智徽章上原本隐藏的精确数字显示出来（引擎一直在更新，只是 Hide 了）
  - 屏幕上方加一行：第 N 天 · 季节 · 温度
  always-on，无开关。
]]

local G = GLOBAL

-- 1) 徽章数字
AddClassPostConstruct("widgets/statusdisplays", function(self)
    for _, k in ipairs({ "heart", "stomach", "brain" }) do
        if self[k] and self[k].num then self[k].num:Show() end
    end
end)

-- 2) 顶部一行：天数 / 季节 / 温度
AddSimPostInit(function()
    local p = G.GetPlayer and G.GetPlayer()
    if not (p and p.HUD and p.HUD.controls) then return end
    local Text = G.require("widgets/text")
    if p.HUD.controls._omni_status and p.HUD.controls._omni_status.inst:IsValid() then return end

    local t = p.HUD.controls:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT, 22))
    t:SetVAnchor(G.ANCHOR_TOP)
    t:SetHAnchor(G.ANCHOR_MIDDLE)
    t:SetPosition(0, -26, 0)
    t:SetColour(1, 1, 1, 0.8)
    p.HUD.controls._omni_status = t

    local SEASON_CN = { summer = "夏天", winter = "冬天", spring = "春天", autumn = "秋天",
                        mild = "温和", wet = "潮湿", green = "繁盛", dry = "干旱", lush = "葱郁" }

    p:DoPeriodicTask(1, function()
        if not t.inst:IsValid() then return end
        local parts = {}
        local clock = G.GetClock and G.GetClock()
        if clock and clock.numcycles then parts[#parts + 1] = "第 " .. (clock.numcycles + 1) .. " 天" end
        local sm = G.GetSeasonManager and G.GetSeasonManager()
        if sm then
            local s = sm.current_season or (sm.GetSeason and sm:GetSeason())
            if s then parts[#parts + 1] = SEASON_CN[s] or tostring(s) end
        end
        if p.components.temperature and p.components.temperature.GetCurrent then
            parts[#parts + 1] = math.floor(p.components.temperature:GetCurrent() + 0.5) .. "°"
        end
        t:SetString(table.concat(parts, "   "))
    end)
end)

print("[omnidsm/status] 组合状态栏已加载")
