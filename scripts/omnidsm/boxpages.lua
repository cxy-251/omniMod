--[[ 多箱翻页：打开任意一个「随身箱子」，UI 上加 ◀ N/总数 ▶，一个窗口翻遍所有箱子。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  做法：包 ContainerWidget:Open，若打开的是 omni_box 就加一排翻页控件；
  翻页 = 关掉当前箱子、打开列表里的上/下一个（UI 会自然刷新成新的一页）。
  always-on，无开关。
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

AddClassPostConstruct("widgets/containerwidget", function(self)
    local Text        = G.require("widgets/text")
    local ImageButton = G.require("widgets/imagebutton")

    local _Open = self.Open
    self.Open = function(self, container, doer)
        _Open(self, container, doer)

        if self._omni_pager then self._omni_pager:Kill(); self._omni_pager = nil end
        if not (container and container.prefab == "omni_box" and doer) then return end

        local boxes = my_boxes(doer)
        if #boxes < 2 then return end
        local idx = 1
        for i, b in ipairs(boxes) do if b == container then idx = i break end end

        local pager = self:AddChild(G.require("widgets/widget")("omni_pager"))
        self._omni_pager = pager
        pager:SetPosition(0, 330, 0)

        local function go(delta)
            local nb = boxes[((idx - 1 + delta) % #boxes) + 1]
            if nb and nb ~= container then
                container.components.container:Close()
                nb.components.container:Open(doer)
            end
        end

        local L = pager:AddChild(ImageButton("images/ui.xml", "spin_arrow.tex", nil, nil, nil, nil, { 1, 1 }, { 0, 0 }))
        L:SetPosition(-90, 0, 0); L:SetScale(-1, 1, 1)
        L:SetOnClick(function() go(-1) end)

        local R = pager:AddChild(ImageButton("images/ui.xml", "spin_arrow.tex", nil, nil, nil, nil, { 1, 1 }, { 0, 0 }))
        R:SetPosition(90, 0, 0)
        R:SetOnClick(function() go(1) end)

        local lbl = pager:AddChild(Text(G.NUMBERFONT or G.BODYTEXTFONT, 28))
        lbl:SetPosition(0, 0, 0)
        lbl:SetString(idx .. " / " .. #boxes)
    end
end)

print("[omnidsm/boxpages] 多箱翻页已加载")
