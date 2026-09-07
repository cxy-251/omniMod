--[[ 组合状态栏（联机版）。（modimport 加载，跑在 mod 环境）

  - 徽章上的精确数字常显（联机版自带 ShowStatusNumbers，只是平时藏着）
  - 屏幕上方加一行：第 N 天 · 季节(剩 N 天) · 温度
  always-on，无开关。
]]

local G = GLOBAL

local SEASON_CN = { autumn = "秋天", winter = "冬天", spring = "春天", summer = "夏天" }

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

    -- 顶部一行：天数 / 季节 / 温度
    local Text = G.require("widgets/text")
    local line = self:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT, 22))
    line:SetVAnchor(G.ANCHOR_TOP)
    line:SetHAnchor(G.ANCHOR_MIDDLE)
    line:SetPosition(0, -28, 0)
    line:SetColour(1, 1, 1, 0.85)
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
    end)
end)

print("[omnidst/status] 组合状态栏已加载")
