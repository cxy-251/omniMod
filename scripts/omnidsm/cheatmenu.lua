--[[ cheats 的游戏内可视化菜单。手柄和键鼠都能用。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  打开：按 Start / Esc（暂停）→ 暂停菜单里选「作弊菜单」。
  操作：方向键上下 或 鼠标悬停 选行；A 或 鼠标左键 切换/循环数值；B 或 Esc 返回。

  依赖 cheats 模块（FEATURES 里排在前面），读 GLOBAL.OMNIDSM.state，
  改动走已有的 GLOBAL.omni_* 函数。

  ★ 关键坑：游戏运行时全局（TheFrontEnd / SetPause / TITLEFONT ...）在 mod 加载时
    还不存在，**不能在文件作用域 local 缓存**，必须在用到时才 G.xxx 取。
    只有 widgets 这些早就加载好的 require 结果可以在文件作用域缓存。
]]

local G = GLOBAL

local Screen = G.require("widgets/screen")
local Widget = G.require("widgets/widget")
local Text   = G.require("widgets/text")
local Image  = G.require("widgets/image")
local Menu   = G.require("widgets/menu")

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
local function st() return (G.OMNIDSM and G.OMNIDSM.state) or {} end

local CheatMenu = Class(Screen, function(self)
    Screen._ctor(self, "OmniCheatMenu")
    self.was_paused = G.IsPaused and G.IsPaused()
    G.SetPause(true, "omnicheatmenu")

    self.black = self:AddChild(Image("images/global.xml", "square.tex"))
    self.black:SetVRegPoint(G.ANCHOR_MIDDLE); self.black:SetHRegPoint(G.ANCHOR_MIDDLE)
    self.black:SetVAnchor(G.ANCHOR_MIDDLE);   self.black:SetHAnchor(G.ANCHOR_MIDDLE)
    self.black:SetScaleMode(G.SCALEMODE_FILLSCREEN)
    self.black:SetTint(0, 0, 0, 0.75)

    self.root = self:AddChild(Widget("ROOT"))
    self.root:SetVAnchor(G.ANCHOR_MIDDLE)
    self.root:SetHAnchor(G.ANCHOR_MIDDLE)
    self.root:SetScaleMode(G.SCALEMODE_PROPORTIONAL)

    self.bg = self.root:AddChild(Image("images/globalpanels.xml", "small_dialog.tex"))
    self.bg:SetVRegPoint(G.ANCHOR_MIDDLE); self.bg:SetHRegPoint(G.ANCHOR_MIDDLE)
    self.bg:SetScale(2.2, 1.95, 1)

    self.title = self.root:AddChild(Text(G.TITLEFONT, 40))
    self.title:SetPosition(0, 185, 0)
    self.title:SetString("作弊菜单")

    self.hint = self.root:AddChild(Text(G.BUTTONFONT, 19))
    self.hint:SetPosition(0, -195, 0)
    self.hint:SetColour(0.8, 0.8, 0.8, 1)
    self.hint:SetString("方向键/鼠标 选择    A/左键 切换    B/Esc 返回")

    -- 标签短，两列排。
    local LROWS = {
        { label = function() return "地图  " .. onoff(st().map) end,
          act = function() G.omni_map(not st().map) end },
        { label = function() return "速度 x" .. tostring(st().speed) end,
          act = function() G.omni_speed(cycle(SPEED_PRESETS, st().speed)) end },
        { label = function() return "免建造  " .. onoff(st().tech) end,
          act = function() G.omni_tech(not st().tech) end },
        { label = function() return "秒采伐  " .. onoff(st().work) end,
          act = function() G.omni_work(not st().work) end },
        { label = function() return "锁血  " .. onoff(st().hp) end,
          act = function() G.omni_hp(not st().hp) end },
        { label = function() return "伤害 x" .. tostring(st().dmg) end,
          act = function() G.omni_dmg(cycle(DMG_PRESETS, st().dmg)) end },
        { label = function() return "光照  " .. onoff(st().light) end,
          act = function() G.omni_light(not st().light) end },
    }
    local RROWS = {
        { label = function() return "血量  " .. onoff(st().hpbar) end,
          act = function() G.omni_hpbar(not st().hpbar) end },
        { label = function() return "防崩  " .. onoff(st().janitor) end,
          act = function() G.omni_janitor(not st().janitor) end },
        { label = function() return "回理智" end, act = function() G.omni_sanity() end },
        { label = function() return "全默认" end, act = function() G.omni_on() end },
        { label = function() return "全关闭" end, act = function() G.omni_off() end },
        { label = function() return "返回"   end, act = function() self:Close() end },
    }
    self.cols = { { rows = LROWS }, { rows = RROWS } }

    for ci, col in ipairs(self.cols) do
        local items = {}
        for i, r in ipairs(col.rows) do
            items[i] = { text = r.label(), cb = function() r.act(); self:Refresh() end }
        end
        col.menu = self.root:AddChild(Menu(items, -40, false))
        col.menu:SetPosition(ci == 1 and -150 or 150, 130, 0)
        col.menu:SetTextSize(28)
    end

    -- 左右列之间的手柄焦点连线
    local L, R = self.cols[1].menu.items, self.cols[2].menu.items
    for i, it in ipairs(L) do
        it:SetFocusChangeDir(G.MOVE_RIGHT, R[math.min(i, #R)])
    end
    for i, it in ipairs(R) do
        it:SetFocusChangeDir(G.MOVE_LEFT, L[math.min(i, #L)])
    end

    self.default_focus = self.cols[1].menu
    self.cols[1].menu:SetFocus(1)

    if G.TheInputProxy then G.TheInputProxy:SetCursorVisible(true) end
end)

function CheatMenu:Refresh()
    for _, col in ipairs(self.cols) do
        for i, r in ipairs(col.rows) do
            col.menu:EditItem(i, r.label())
        end
    end
end

function CheatMenu:Close()
    G.TheFrontEnd:PopScreen(self)
    if not self.was_paused then G.SetPause(false) end
end

function CheatMenu:OnControl(control, down)
    if CheatMenu._base.OnControl(self, control, down) then return true end
    if not down and (control == G.CONTROL_CANCEL or control == G.CONTROL_PAUSE) then
        self:Close()
        return true
    end
end

-- 往暂停菜单里塞一项「作弊菜单」（鼠标可点，手柄可选）
AddClassPostConstruct("screens/pausescreen", function(ps)
    if not ps.menu or not ps.menu.AddItem then return end
    ps.menu:AddItem("作弊菜单", function()
        G.TheFrontEnd:PushScreen(CheatMenu())
    end)
    local n = ps.menu:GetNumberOfItems()
    ps.menu:SetPosition(-(160 * (n - 1)) / 2, -65, 0)
end)

print("[omnidsm/cheatmenu] 已加载（暂停菜单 → 作弊菜单）")
