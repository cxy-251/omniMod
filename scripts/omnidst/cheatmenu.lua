--[[ cheats 的游戏内可视化菜单（联机版）。手柄 + 键鼠都能用。
     （modimport 加载，跑在 mod 环境）

  打开：按 Esc / Start（暂停）→ 暂停菜单里选「作弊菜单」。
  操作：方向键 选行；A / 鼠标左键 切换或循环数值；B / Esc 返回。

  用 TextButton 自己排两列，读 GLOBAL.OMNIDST.state，改动走 GLOBAL.omni_* 函数。
  ★ 运行时全局在 mod 加载时还不存在 —— 用到时才 G.xxx 取。
]]

local G = GLOBAL

local Screen     = G.require("widgets/screen")
local Widget     = G.require("widgets/widget")
local Text       = G.require("widgets/text")
local Image      = G.require("widgets/image")
local TextButton = G.require("widgets/textbutton")

local SPEED_PRESETS = { 1, 1.5, 2, 3, 5, 8 }
local DMG_PRESETS   = { 1, 2, 3, 5, 10, 25 }
local ROW_H = 44

local function cycle(list, cur)
    for i, v in ipairs(list) do
        if math.abs(v - (cur or 0)) < 1e-6 then return list[i % #list + 1] end
    end
    return list[1]
end
local function onoff(b) return b and "开" or "关" end
local function st() return (G.OMNIDST and G.OMNIDST.state) or {} end

local CheatMenu = Class(Screen, function(self)
    Screen._ctor(self, "OmniCheatMenu")

    self.black = self:AddChild(Image("images/global.xml", "square.tex"))
    self.black:SetVRegPoint(G.ANCHOR_MIDDLE); self.black:SetHRegPoint(G.ANCHOR_MIDDLE)
    self.black:SetVAnchor(G.ANCHOR_MIDDLE);   self.black:SetHAnchor(G.ANCHOR_MIDDLE)
    self.black:SetScaleMode(G.SCALEMODE_FILLSCREEN)
    self.black:SetTint(0, 0, 0, 0.75)

    self.root = self:AddChild(Widget("ROOT"))
    self.root:SetVAnchor(G.ANCHOR_MIDDLE)
    self.root:SetHAnchor(G.ANCHOR_MIDDLE)
    self.root:SetScaleMode(G.SCALEMODE_PROPORTIONAL)

    self.bg = self.root:AddChild(Image("images/global_redux.xml", "dialog_wide.tex"))
    if not self.bg.texture then
        self.bg:SetTexture("images/globalpanels.xml", "panel.tex")
    end
    self.bg:SetVRegPoint(G.ANCHOR_MIDDLE); self.bg:SetHRegPoint(G.ANCHOR_MIDDLE)
    self.bg:SetScale(1.5, 1.7, 1)

    self.title = self.root:AddChild(Text(G.TITLEFONT or G.NEWFONT, 42))
    self.title:SetPosition(0, 190, 0)
    self.title:SetString("作弊菜单")

    self.hint = self.root:AddChild(Text(G.BUTTONFONT or G.NEWFONT, 19))
    self.hint:SetPosition(0, -200, 0)
    self.hint:SetColour(0.85, 0.85, 0.85, 1)
    self.hint:SetString("方向键 选择    A / 左键 切换    B / Esc 返回")

    local LROWS = {
        { label = function() return "地图全开：" .. onoff(st().map) end,
          act = function() G.omni_map(not st().map) end },
        { label = function() return "行走速度：x" .. tostring(st().speed) end,
          act = function() G.omni_speed(cycle(SPEED_PRESETS, st().speed)) end },
        { label = function() return "免费建造：" .. onoff(st().tech) end,
          act = function() G.omni_tech(not st().tech) end },
        { label = function() return "秒采伐挖矿：" .. onoff(st().work) end,
          act = function() G.omni_work(not st().work) end },
        { label = function() return "锁血不死：" .. onoff(st().hp) end,
          act = function() G.omni_hp(not st().hp) end },
        { label = function() return "伤害倍率：x" .. tostring(st().dmg) end,
          act = function() G.omni_dmg(cycle(DMG_PRESETS, st().dmg)) end },
        { label = function() return "身上光照：" .. onoff(st().light) end,
          act = function() G.omni_light(not st().light) end },
    }
    local RROWS = {
        { label = function() return "生物血量：" .. onoff(st().hpbar) end,
          act = function() G.omni_hpbar(not st().hpbar) end },
        { label = function() return "防崩管家：" .. onoff(st().janitor) end,
          act = function() G.omni_janitor(not st().janitor) end },
        { label = function() return "技能树全开" end, act = function() if G.omni_skills then G.omni_skills() end end },
        { label = function() return "回复理智" end,  act = function() G.omni_sanity() end },
        { label = function() return "回复生命" end,  act = function() G.omni_health() end },
        { label = function() return "回复饱食" end,  act = function() G.omni_hunger() end },
        { label = function() return "重载 Mod (c_reset)" end,
          act = function() self:Close(); if G.c_reset then G.c_reset() end end },
        { label = function() return "返回" end,      act = function() self:Close() end },
    }
    self.cols = { { rows = LROWS, x = -190 }, { rows = RROWS, x = 190 } }

    for _, col in ipairs(self.cols) do
        col.btns = {}
        for i, r in ipairs(col.rows) do
            local b = self.root:AddChild(TextButton(""))
            b:SetFont(G.BUTTONFONT or G.NEWFONT)
            b:SetTextSize(30)
            b:SetColour(1, 1, 1, 1)
            b:SetOverColour(1, 0.85, 0.3, 1)
            b:SetText(r.label())
            b:SetPosition(col.x, 150 - (i - 1) * ROW_H, 0)
            b:SetOnClick(function() r.act(); self:Refresh() end)
            col.btns[i] = b
        end
    end

    local L, R = self.cols[1].btns, self.cols[2].btns
    for _, col in ipairs(self.cols) do
        for i, b in ipairs(col.btns) do
            if col.btns[i - 1] then b:SetFocusChangeDir(G.MOVE_UP, col.btns[i - 1]) end
            if col.btns[i + 1] then b:SetFocusChangeDir(G.MOVE_DOWN, col.btns[i + 1]) end
        end
    end
    for i, b in ipairs(L) do b:SetFocusChangeDir(G.MOVE_RIGHT, R[math.min(i, #R)]) end
    for i, b in ipairs(R) do b:SetFocusChangeDir(G.MOVE_LEFT,  L[math.min(i, #L)]) end

    self.default_focus = L[1]
end)

function CheatMenu:Refresh()
    for _, col in ipairs(self.cols) do
        for i, r in ipairs(col.rows) do
            col.btns[i]:SetText(r.label())
        end
    end
end

function CheatMenu:Close()
    G.TheFrontEnd:PopScreen(self)
end

function CheatMenu:OnControl(control, down)
    if CheatMenu._base.OnControl(self, control, down) then return true end
    if not down and (control == G.CONTROL_CANCEL or control == G.CONTROL_PAUSE) then
        self:Close()
        return true
    end
end

-- 往暂停菜单里塞一项「作弊菜单」
AddClassPostConstruct("screens/redux/pausescreen", function(self)
    if not (self.menu and self.menu.AddItem) then return end
    self.menu:AddItem("作弊菜单", function()
        G.TheFrontEnd:PushScreen(CheatMenu())
    end, nil, nil, 30)
end)

print("[omnidst/cheatmenu] 已加载（暂停菜单 → 作弊菜单）")
