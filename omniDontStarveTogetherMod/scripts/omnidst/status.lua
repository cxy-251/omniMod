--[[ 组合状态栏（联机版）。（modimport 加载，跑在 mod 环境）

  - 徽章上的精确数字常显（联机版自带 ShowStatusNumbers，只是平时藏着）
  - 屏幕上方三行信息（没有数据的项自动隐藏）：
      1 第 N 天 · 季节(剩 N 天) · 温度
      2 时段剩余 · 月相 · 猎犬倒计时 · 淘气值 · 天气 ｜ 洞穴里换成噩梦周期
      3 潮湿 · 饥饿/理智每分钟变化
  猎犬/淘气/饥饿理智速度只存在于服务器进程（带洞穴时客户端读不到），由服务器每 2 秒
  通过 mod RPC 推给客户端；不带洞穴时客户端就是服务器，直接读。
  always-on，无开关。
]]

local G = GLOBAL

local SEASON_CN = { autumn = "秋天", winter = "冬天", spring = "春天", summer = "夏天" }
local PHASE_CN = { day = "白天", dusk = "黄昏", night = "夜晚" }
local MOON_CN = { new = "新月", quarter = "上弦", half = "半月", threequarter = "盈凸", full = "满月" }
local NIGHTMARE_CN = { calm = "平静", warn = "预警", wild = "噩梦", dawn = "黎明" }

local function mmss(sec)
    sec = math.max(0, math.floor((sec or 0) + 0.5))
    return string.format("%d:%02d", math.floor(sec / 60), sec % 60)
end
local function try(fn, ...)
    local ok, r = G.pcall(fn, ...)
    if ok then return r end
end

------------------------------------------------------------------ 服务器侧：算只有服务器知道的数据
local function server_info(p)
    local w = G.TheWorld
    local hound, naughty, player = "", "", ""

    local h = w.components.hounded
    if h and h.GetDebugString then
        local d = try(h.GetDebugString, h) or ""
        local tag, t = d:match("^(%u+) spawns are coming in ([%d%.]+)")
        if tag and t then
            hound = (tag == "WARNING" and "猎犬来了! " or "猎犬 ") .. mmss(G.tonumber(t))
        elseif d:find("^ATTACKING") then
            hound = "猎犬来袭中"
        end
    end

    local k = w.components.kramped
    if k and k.GetDebugString then
        local d = try(k.GetDebugString, k) or ""
        local key = tostring(p)
        for line in d:gmatch("[^\n]+") do
            if line:find(key, 1, true) then
                local a, th = line:match("Actions: (%d+) / (%d+)")
                if a then
                    a, th = G.tonumber(a), G.tonumber(th)
                    naughty = "淘气 " .. a .. "/" .. th .. ((th - a <= 5) and "!" or "")
                else
                    naughty = "淘气 0"
                end
            end
        end
    end

    local parts = {}
    local m = p.components.moisture
    if m and m.GetMoisturePercent then
        local pc = try(m.GetMoisturePercent, m)
        if type(pc) == "number" and pc > 0.01 then parts[#parts + 1] = "潮湿 " .. math.floor(pc * 100 + 0.5) .. "%" end
    end
    local hg = p.components.hunger
    if hg and hg.hungerrate then
        local br = (hg.burnrate or 1) * ((hg.burnratemodifiers and hg.burnratemodifiers:Get()) or 1)
        parts[#parts + 1] = string.format("饥饿 -%.1f/分", hg.hungerrate * br * 60)
    end
    local sn = p.components.sanity
    if sn and type(sn.rate) == "number" then
        parts[#parts + 1] = string.format("理智 %+.1f/分", sn.rate * 60)
    end
    player = table.concat(parts, "   ")
    return hound .. "|" .. naughty .. "|" .. player
end

local srv = { hound = "", naughty = "", player = "" }
local function store(str)
    local a, b, c = tostring(str or ""):match("^(.-)|(.-)|(.*)$")
    if a then srv.hound, srv.naughty, srv.player = a, b, c end
end
AddClientModRPCHandler("omnidst", "statusinfo", store)

AddPlayerPostInit(function(p)
    if not (G.TheWorld and G.TheWorld.ismastersim) then return end
    p:DoPeriodicTask(2, function()
        if not p:IsValid() then return end
        local str = try(server_info, p)
        if not str then return end
        if p == G.ThePlayer then
            store(str)   -- 不带洞穴：客户端就是服务器，同一进程直接写
        elseif p.userid and p.userid ~= "" then
            G.pcall(SendModRPCToClient, GetClientModRPC("omnidst", "statusinfo"), p.userid, str)
        end
    end)
end)

------------------------------------------------------------------ 客户端侧：拼三行
local function phase_left()
    local net = G.TheWorld and G.TheWorld.net
    local clock = net and net.components and net.components.clock
    if not (clock and clock.GetTimeUntilPhase) then return end
    local best
    for _, ph in ipairs({ "day", "dusk", "night" }) do
        if ph ~= G.TheWorld.state.phase then
            local t = try(clock.GetTimeUntilPhase, clock, ph)
            if type(t) == "number" and t > 0 and (best == nil or t < best) then best = t end
        end
    end
    return best
end

local function line2(st)
    local parts = {}
    local function add(x) if x and x ~= "" then parts[#parts + 1] = x end end
    local cave = G.TheWorld:HasTag("cave")
    if cave then
        if st.nightmarephase and st.nightmarephase ~= "none" then
            add("噩梦周期 " .. (NIGHTMARE_CN[st.nightmarephase] or st.nightmarephase))
        end
    else
        if st.phase then
            local left = phase_left()
            add((PHASE_CN[st.phase] or st.phase) .. (left and (" " .. mmss(left)) or ""))
        end
        if st.moonphase then
            add((MOON_CN[st.moonphase] or st.moonphase) .. (st.moonphase ~= "full" and st.moonphase ~= "new" and (st.iswaxingmoon and "(渐盈)" or "(渐亏)") or ""))
        end
    end
    add(srv.hound)
    add(srv.naughty)
    local wx = {}
    if st.israining then wx[#wx + 1] = "下雨" end
    if st.issnowing then wx[#wx + 1] = "下雪" end
    if (st.snowlevel or 0) > 0.05 then wx[#wx + 1] = "积雪 " .. math.floor(st.snowlevel * 100) .. "%" end
    add(table.concat(wx, " "))
    return table.concat(parts, "   ")
end


AddClassPostConstruct("widgets/controls", function(self)
    -- 徽章数字常显
    if self.ShowStatusNumbers then
        self.inst:DoTaskInTime(1, function()
            if self.ShowStatusNumbers then self:ShowStatusNumbers() end
        end)
        local _Hide = self.HideStatusNumbers
        self.HideStatusNumbers = function(s, ...)
            -- 手柄物品栏开合时游戏会调 Hide，这里让它开完还是显示
            local r = _Hide and _Hide(s, ...)
            if s.ShowStatusNumbers then s.inst:DoTaskInTime(0, function() s:ShowStatusNumbers() end) end
            return r
        end
    end

    -- 顶部三行
    local Text = G.require("widgets/text")
    local function mk(y, a)
        local t = self:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT, 22))
        t:SetVAnchor(G.ANCHOR_TOP)
        t:SetHAnchor(G.ANCHOR_MIDDLE)
        t:SetPosition(0, y, 0)
        t:SetColour(1, 1, 1, a)
        return t
    end
    local line, l2, l3 = mk(-28, 0.85), mk(-52, 0.85), mk(-76, 0.75)
    self._omni_statusline = line

    line.inst:DoPeriodicTask(1, function()
        if not line.inst:IsValid() then return end
        local w = G.TheWorld
        if not (w and w.state) then return end
        local parts = {}
        parts[#parts + 1] = "第 " .. ((w.state.cycles or 0) + 1) .. " 天"
        local s = w.state.season
        if s then
            local rem = w.state.remainingdaysinseason
            parts[#parts + 1] = (SEASON_CN[s] or s) .. (rem and ("(剩" .. math.floor(rem) .. ")") or "")
        end
        local t = w.state.temperature
        if t == nil and G.ThePlayer and G.ThePlayer.components.temperature then
            t = G.ThePlayer.components.temperature:GetCurrent()
        end
        if t then parts[#parts + 1] = math.floor(t + 0.5) .. "°" end
        line:SetString(table.concat(parts, "   "))
        l2:SetString(try(line2, w.state) or "")
        l3:SetString(srv.player or "")
    end)
end)

print("[omnidst/status] 组合状态栏已加载（三行：时间/季节/月相/猎犬/淘气/天气/洞穴/玩家）")
