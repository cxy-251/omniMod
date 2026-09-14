--[[ 多个随身箱子翻页。（modimport 加载，跑在 mod 环境）

  打开任意「随身箱子」→ 箱子界面下方出现  ◀  i / n  ▶ ，翻遍身上所有随身箱子。
  翻页 = 关当前箱子 + 打开下一个。always-on。
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
    local of = inv.GetOverflowContainer and inv:GetOverflowContainer()
    if of then for _, v in pairs(of.slots or {}) do add(v) end end
    return list
end

AddClassPostConstruct("widgets/containerwidget", function(self)
    local ImageButton = G.require("widgets/imagebutton")
    local Text        = G.require("widgets/text")
    local Widget      = G.require("widgets/widget")

    local function kill_pager()
        if self._omni_pager then self._omni_pager:Kill(); self._omni_pager = nil end
    end

    local function build_pager(container, doer)
        kill_pager()
        if not (container and doer) then return end
        local boxes = my_boxes(doer)
        if #boxes < 2 then return end
        local idx = 1
        for i, b in ipairs(boxes) do if b == container then idx = i break end end

        local pager = self:AddChild(Widget("omni_pager"))
        self._omni_pager = pager
        pager:SetPosition(0, -320, 0)   -- 网格下方（跟随 containerwidget 的 0.6 缩放）

        local function go(delta)
            local nb = boxes[((idx - 1 + delta) % #boxes) + 1]
            if nb and nb ~= container and nb.components.container then
                container.components.container:Close(doer)
                nb.components.container:Open(doer)
            end
        end

        local l = pager:AddChild(ImageButton("images/ui.xml", "spin_arrow.tex",
            "spin_arrow_over.tex", "spin_arrow_disabled.tex", "spin_arrow_down.tex"))
        l:SetScale(-1.7, 1.7, 1); l:SetPosition(-130, 0, 0)
        l:SetOnClick(function() go(-1) end)

        local r = pager:AddChild(ImageButton("images/ui.xml", "spin_arrow.tex",
            "spin_arrow_over.tex", "spin_arrow_disabled.tex", "spin_arrow_down.tex"))
        r:SetScale(1.7, 1.7, 1); r:SetPosition(130, 0, 0)
        r:SetOnClick(function() go(1) end)

        local lbl = pager:AddChild(Text(G.TITLEFONT or G.NEWFONT, 50))
        lbl:SetColour(1, 1, 1, 1)
        lbl:SetPosition(0, 0, 0)
        lbl:SetString(idx .. " / " .. #boxes)
    end

    local _Open = self.Open
    self.Open = function(self, container, doer)
        _Open(self, container, doer)
        if container and container.prefab == "omni_box" then
            build_pager(container, doer or self.owner)
        else
            kill_pager()
        end
    end

    local _Close = self.Close
    self.Close = function(self, ...)
        kill_pager()
        return _Close(self, ...)
    end
end)

print("[omnidst/boxpages] 多箱翻页已加载")
