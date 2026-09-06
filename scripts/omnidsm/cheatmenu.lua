--[[ 把 cheats 的开关做成游戏内可视化菜单，手柄可操作（Steam Deck 不用键鼠）。

  打开方式：按 Start（暂停）→ 暂停菜单里多一项「作弊菜单」→ A 进入。
  菜单里：方向键上下选行，A 切换 / 循环数值，B 或 Start 返回。

  依赖 cheats 模块（同一个 mod 里先加载），读 GLOBAL.OMNIDSM.state，
  改动走已有的 GLOBAL.omni_* 函数。

  注意：本文件里凡是游戏全局（Class / require / 常量 / TheFrontEnd …）一律走 G.xxx，
  因为 mod 的脚本环境不一定能直接看到 _G。
]]

local M = {}

local SPEED_PRESETS = { 1, 1.5, 2, 3, 5, 8 }
local DMG_PRESETS   = { 1, 2, 3, 5, 10, 25 }

local function cycle(list, cur)
    for i, v in ipairs(list) do
        if math.abs(v - (cur or 0)) < 1e-6 then
            return list[i % #list + 1]
        end
    end
    return list[1]
end

local function onoff(b) return b and "开" or "关" end

function M.init(G)
    local Screen      = G.require("widgets/screen")
    local Widget      = G.require("widgets/widget")
    local Text        = G.require("widgets/text")
    local Image       = G.require("widgets/image")
    local Menu        = G.require("widgets/menu")

    local Class       = G.Class
    local TheFrontEnd = G.TheFrontEnd
    local SetPause    = G.SetPause
    local A_MID       = G.ANCHOR_MIDDLE
    local SM_FILL     = G.SCALEMODE_FILLSCREEN
    local SM_PROP     = G.SCALEMODE_PROPORTIONAL
    local TITLEFONT   = G.TITLEFONT
    local BUTTONFONT  = G.BUTTONFONT
    local C_CANCEL    = G.CONTROL_CANCEL
    local C_PAUSE     = G.CONTROL_PAUSE

    local function st() return (G.OMNIDSM and G.OMNIDSM.state) or {} end

    ----------------------------------------------------------------------
    local CheatMenu = Class(Screen, function(self)
        Screen._ctor(self, "OmniCheatMenu")
        self.was_paused = G.IsPaused and G.IsPaused()
        SetPause(true, "omnicheatmenu")

        self.black = self:AddChild(Image("images/global.xml", "square.tex"))
        self.black:SetVRegPoint(A_MID); self.black:SetHRegPoint(A_MID)
        self.black:SetVAnchor(A_MID);   self.black:SetHAnchor(A_MID)
        self.black:SetScaleMode(SM_FILL)
        self.black:SetTint(0, 0, 0, 0.75)

        self.root = self:AddChild(Widget("ROOT"))
        self.root:SetVAnchor(A_MID)
        self.root:SetHAnchor(A_MID)
        self.root:SetScaleMode(SM_PROP)

        self.bg = self.root:AddChild(Image("images/globalpanels.xml", "small_dialog.tex"))
        self.bg:SetVRegPoint(A_MID); self.bg:SetHRegPoint(A_MID)
        self.bg:SetScale(1.7, 1.6, 1)

        self.title = self.root:AddChild(Text(TITLEFONT, 45))
        self.title:SetPosition(0, 175, 0)
        self.title:SetString("作弊菜单")

        self.hint = self.root:AddChild(Text(BUTTONFONT, 22))
        self.hint:SetPosition(0, -205, 0)
        self.hint:SetColour(0.8, 0.8, 0.8, 1)
        self.hint:SetString("上下选择   A 切换/调整   B 返回")

        self.rows = {
            { label = function() return "地图全开：" .. onoff(st().map) end,
              act = function() G.omni_map(not st().map) end },
            { label = function() return "行走速度：x" .. tostring(st().speed) end,
              act = function() G.omni_speed(cycle(SPEED_PRESETS, st().speed)) end },
            { label = function() return "科技全解锁：" .. onoff(st().tech) end,
              act = function() G.omni_tech(not st().tech) end },
            { label = function() return "锁血（不低于10）：" .. onoff(st().hp) end,
              act = function() G.omni_hp(not st().hp) end },
            { label = function() return "伤害倍率：x" .. tostring(st().dmg) end,
              act = function() G.omni_dmg(cycle(DMG_PRESETS, st().dmg)) end },
            { label = function() return "— 全部恢复默认 —" end, act = function() G.omni_on() end },
            { label = function() return "— 全部关闭 —"     end, act = function() G.omni_off() end },
            { label = function() return "返回"             end, act = function() self:Close() end },
        }

        local items = {}
        for i, r in ipairs(self.rows) do
            items[i] = { text = r.label(), cb = function() r.act(); self:Refresh() end }
        end

        self.menu = self.root:AddChild(Menu(items, -46, false))  -- 竖排，行距 46
        self.menu:SetPosition(0, 120, 0)
        self.default_focus = self.menu
        self.menu:SetFocus(1)
    end)

    function CheatMenu:Refresh()
        for i, r in ipairs(self.rows) do
            self.menu:EditItem(i, r.label())
        end
    end

    function CheatMenu:Close()
        TheFrontEnd:PopScreen(self)
        if not self.was_paused then SetPause(false) end
    end

    function CheatMenu:OnControl(control, down)
        if CheatMenu._base.OnControl(self, control, down) then return true end
        if not down and (control == C_CANCEL or control == C_PAUSE) then
            self:Close()
            return true
        end
    end

    ----------------------------------------------------------------------
    G.AddClassPostConstruct("screens/pausescreen", function(ps)
        if not ps.menu or not ps.menu.AddItem then return end
        ps.menu:AddItem("作弊菜单", function()
            TheFrontEnd:PushScreen(CheatMenu())
        end)
        local n = ps.menu:GetNumberOfItems()
        ps.menu:SetPosition(-(160 * (n - 1)) / 2, -65, 0)  -- 按新项数重新水平居中
    end)

    print("[omnidsm/cheatmenu] 已加载（暂停菜单 → 作弊菜单）")
end

return M
