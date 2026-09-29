--[[ 组合状态栏（精简版）。（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  - 把血/饱食/理智徽章上原本隐藏的精确数字显示出来（引擎一直在更新，只是 Hide 了）
  - 屏幕上方三行信息（没有数据的项自动隐藏）：
      1 第 N 天 · 季节(剩几天) · 温度
      2 时段剩余 · 月相 · 猎犬倒计时 · 淘气值 · 天气 · 洞穴噩梦周期
      3 潮湿 · 饥饿/理智变化速度
  always-on，无开关。
]]

local G = GLOBAL

-- 1) 徽章数字
AddClassPostConstruct("widgets/statusdisplays", function(self)
    for _, k in ipairs({ "heart", "stomach", "brain" }) do
        if self[k] and self[k].num then self[k].num:Show() end
    end
end)

-- 2) 顶部三行信息
local SEASON_CN = { summer = "夏天", winter = "冬天", spring = "春天", autumn = "秋天",
                    mild = "温和", wet = "潮湿", green = "繁盛", dry = "干旱", lush = "葱郁" }
local PHASE_CN = { day = "白天", dusk = "黄昏", night = "夜晚" }
local MOON_CN = { new = "新月", quarter = "上弦", half = "半月", threequarter = "盈凸", full = "满月" }
local NIGHTMARE_CN = { calm = "平静", warn = "预警", nightmare = "噩梦", dawn = "黎明" }

local function mmss(sec)
    sec = math.max(0, math.floor((sec or 0) + 0.5))
    return string.format("%d:%02d", math.floor(sec / 60), sec % 60)
end

-- 每一项都单独 pcall：某个 DLC 世界里缺组件/接口，只是这一项不显示，不影响其它。
local function try(fn, ...)
    local ok, r = G.pcall(fn, ...)
    if ok then return r end
end

local function line1(p, clock, sm)
    local parts = {}
    if clock and clock.numcycles then parts[#parts + 1] = "第 " .. (clock.numcycles + 1) .. " 天" end
    if sm then
        local s = sm.current_season or (sm.GetSeason and try(sm.GetSeason, sm))
        if s then
            local str = SEASON_CN[s] or tostring(s)
            local left = sm.GetDaysLeftInSeason and try(sm.GetDaysLeftInSeason, sm)
            if type(left) == "number" and left < 1000 then str = str .. "(剩 " .. math.ceil(left) .. " 天)" end
            parts[#parts + 1] = str
        end
    end
    local tp = p.components.temperature
    if tp and tp.GetCurrent then parts[#parts + 1] = math.floor(tp:GetCurrent() + 0.5) .. "°" end
    return table.concat(parts, "   ")
end

local function moon_text(clock)
    if not (clock and clock.GetMoonPhase) then return end
    local w = G.GetWorld and G.GetWorld()
    if w and w.IsCave and w:IsCave() then return end
    local ph = try(clock.GetMoonPhase, clock)
    if not ph then return end
    local str = MOON_CN[ph] or ph
    if ph ~= "full" and clock.numcycles then
        for d = 1, 16 do
            if math.floor((clock.numcycles + d) / 2) % 8 == 4 then str = str .. "(" .. d .. " 天后满月)" break end
        end
    end
    return str
end

local function hound_text(w)
    local h = w and w.components and w.components.hounded
    if not h or h.spawnmode == "never" then return end
    if (h.timetoattack or 0) > 0 then
        return (h.warning and "猎犬来了! " or "猎犬 ") .. mmss(h.timetoattack) .. (h.houndstorelease and h.houndstorelease > 0 and (" x" .. h.houndstorelease) or "")
    elseif (h.houndstorelease or 0) > 0 then
        return "猎犬来袭 余" .. h.houndstorelease .. "只"
    end
end

local function naughty_text(p)
    local k = p.components.kramped
    if not k then return end
    local a, t = k.actions or 0, k.threshold
    if a <= 0 then return "淘气 0" end
    local str = "淘气 " .. a .. (t and ("/" .. t) or "")
    if t and t - a <= 5 then str = str .. "!" end
    return str
end

local function weather_text(sm)
    if not sm then return end
    local out = {}
    if sm.IsRaining and try(sm.IsRaining, sm) then out[#out + 1] = "下雨" end
    local snow = sm.GetSnowPercent and try(sm.GetSnowPercent, sm)
    if type(snow) == "number" and snow > 0.05 then out[#out + 1] = "积雪 " .. math.floor(snow * 100) .. "%" end
    if #out > 0 then return table.concat(out, " ") end
end

local function line2(p, clock, sm, w)
    local parts = {}
    local function add(x) if x then parts[#parts + 1] = x end end
    local nm = w and w.components and w.components.nightmareclock
    if nm and nm.phase and w.IsCave and w:IsCave() then
        add("噩梦周期 " .. (NIGHTMARE_CN[nm.phase] or nm.phase) .. " " .. mmss(nm.GetTimeLeftInEra and nm:GetTimeLeftInEra()))
    elseif clock and clock.GetPhase then
        local ph = try(clock.GetPhase, clock)
        local always = clock.CurrentPhaseIsAlways and try(clock.CurrentPhaseIsAlways, clock)
        if ph and not always then
            add((PHASE_CN[ph] or tostring(ph)) .. " " .. mmss(clock.GetTimeLeftInEra and clock:GetTimeLeftInEra()))
        end
    end
    add(moon_text(clock))
    add(hound_text(w))
    add(naughty_text(p))
    add(weather_text(sm))
    return table.concat(parts, "   ")
end

local function line3(p)
    local parts = {}
    local m = p.components.moisture
    if m and m.GetMoisturePercent then
        local pc = try(m.GetMoisturePercent, m)
        if type(pc) == "number" and pc > 0.01 then parts[#parts + 1] = "潮湿 " .. math.floor(pc * 100 + 0.5) .. "%" end
    end
    local hg = p.components.hunger
    if hg and hg.hungerrate and hg.burnrate then
        parts[#parts + 1] = string.format("饥饿 -%.1f/分", hg.hungerrate * hg.burnrate * 60)
    end
    local sn = p.components.sanity
    if sn and sn.GetRate then
        local r = try(sn.GetRate, sn)
        if type(r) == "number" then parts[#parts + 1] = string.format("理智 %+.1f/分", r * 60) end
    end
    return table.concat(parts, "   ")
end

AddSimPostInit(function()
    local p = G.GetPlayer and G.GetPlayer()
    if not (p and p.HUD and p.HUD.controls) then return end
    local Text = G.require("widgets/text")
    if p.HUD.controls._omni_status and p.HUD.controls._omni_status.inst:IsValid() then return end

    local function mk(y, alpha)
        local t = p.HUD.controls:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT, 22))
        t:SetVAnchor(G.ANCHOR_TOP)
        t:SetHAnchor(G.ANCHOR_MIDDLE)
        t:SetPosition(0, y, 0)
        t:SetColour(1, 1, 1, alpha)
        return t
    end
    local t1, t2, t3 = mk(-26, 0.8), mk(-50, 0.8), mk(-74, 0.7)
    p.HUD.controls._omni_status = t1

    p:DoPeriodicTask(1, function()
        if not t1.inst:IsValid() then return end
        local clock = G.GetClock and G.GetClock()
        local sm = G.GetSeasonManager and G.GetSeasonManager()
        local w = G.GetWorld and G.GetWorld()
        t1:SetString(try(line1, p, clock, sm) or "")
        t2:SetString(try(line2, p, clock, sm, w) or "")
        t3:SetString(try(line3, p) or "")
    end)
end)

print("[omnidsm/status] 组合状态栏已加载（三行：时间/季节/月相/猎犬/淘气/天气/洞穴/玩家）")
