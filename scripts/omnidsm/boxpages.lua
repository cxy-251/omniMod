--[[ 多箱翻页 + 开箱暂停。（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  - 打开任意「随身箱子」→ 箱子界面下方出现  ◀  i / n  ▶ ，翻遍所有箱子。
    翻页 = 关当前箱子 + 打开列表里上/下一个。
  - 打开随身箱子时 SetPause(true,"inv")（跟手柄开背包一样的暂停 —— 世界停，
    但从箱子里拿东西照常），关闭时 SetPause(false)。
  always-on。
]]

local G = GLOBAL

local function my_boxes(doer)
    local inv = doer and doer.components and doer.components.inventory
    if not inv then return {} end
    local list, seen = {}, {}
    local function add(v)
        if v and v.prefab == "omni_box" and v.components and v.components.container and not seen[v] then
            seen[v] = true; list[#list + 1] = v
        end
    end
    for _, v in pairs(inv.itemslots or {}) do add(v) end
    add(inv.activeitem)
    if inv.overflow and inv.overflow.components and inv.overflow.components.container then
        for _, v in pairs(inv.overflow.components.container.slots or {}) do add(v) end
    end
    return list
end

local paused_by_box = false
local function box_pause()
    if not paused_by_box then G.SetPause(true, "inv"); paused_by_box = true end
end
local function box_unpause()
    if paused_by_box then G.SetPause(false); paused_by_box = false end
end

AddClassPostConstruct("widgets/containerwidget", function(self)
    local ImageButton = G.require("widgets/imagebutton")
    local Text        = G.require("widgets/text")

    local function build_pager(container, doer)
        if self._omni_pager then self._omni_pager:Kill(); self._omni_pager = nil end
        local boxes = my_boxes(doer)
        if #boxes < 2 then return end
        local idx = 1
        for i, b in ipairs(boxes) do if b == container then idx = i break end end

        local W = G.require("widgets/widget")
        local pager = self:AddChild(W("omni_pager"))
        self._omni_pager = pager
        pager:SetPosition(0, -500, 0)   -- 箱子界面里、网格下方（会跟着 0.6 缩放）

        local function go(delta)
            local nb = boxes[((idx - 1 + delta) % #boxes) + 1]
            if nb and nb ~= container then
                container.components.container:Close()
                nb.components.container:Open(doer)
            end
        end

        local l = pager:AddChild(ImageButton("images/ui.xml", "spin_arrow.tex",
            "spin_arrow_over.tex", "spin_arrow_disabled.tex", "spin_arrow_down.tex"))
        l:SetScale(-1.6, 1.6, 1); l:SetPosition(-120, 0, 0)
        l:SetOnClick(function() go(-1) end)

        local r = pager:AddChild(ImageButton("images/ui.xml", "spin_arrow.tex",
            "spin_arrow_over.tex", "spin_arrow_disabled.tex", "spin_arrow_down.tex"))
        r:SetScale(1.6, 1.6, 1); r:SetPosition(120, 0, 0)
        r:SetOnClick(function() go(1) end)

        local lbl = pager:AddChild(Text(G.TITLEFONT, 45))
        lbl:SetColour(1, 1, 1, 1)
        lbl:SetPosition(0, 0, 0)
        lbl:SetString(idx .. " / " .. #boxes)
    end

    local _Open = self.Open
    self.Open = function(self, container, doer)
        _Open(self, container, doer)
        if container and container.prefab == "omni_box" and doer then
            box_pause()
            build_pager(container, doer)
        elseif self._omni_pager then
            self._omni_pager:Kill(); self._omni_pager = nil
        end
    end

    local _Close = self.Close
    self.Close = function(self, ...)
        if self._omni_pager then self._omni_pager:Kill(); self._omni_pager = nil end
        box_unpause()
        return _Close(self, ...)
    end
end)

print("[omnidsm/boxpages] 多箱翻页 + 开箱暂停 已加载")
