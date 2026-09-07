--[[ 多箱翻页：打开任意一个「随身箱子」→ 屏幕下方出现 ◀ i/n ▶，一个窗口翻遍所有箱子。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  翻页控件挂在 HUD 上（固定屏幕位置、带深色底、亮字，黑夜也看得清），
  箱子一关就 Kill。翻页 = 关当前箱子 + 打开列表里的上/下一个。always-on。
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

local pager  -- 同一时间只会有一个箱子界面

local function kill_pager()
    if pager and pager.inst and pager.inst:IsValid() then pager:Kill() end
    pager = nil
end

local function build_pager(container, doer)
    kill_pager()
    local boxes = my_boxes(doer)
    if #boxes < 2 or not (doer.HUD and doer.HUD.controls) then return end
    local idx = 1
    for i, b in ipairs(boxes) do if b == container then idx = i break end end

    local Widget     = G.require("widgets/widget")
    local Image      = G.require("widgets/image")
    local Text       = G.require("widgets/text")
    local TextButton = G.require("widgets/textbutton")

    pager = doer.HUD.controls:AddChild(Widget("omni_boxpager"))
    pager:SetVAnchor(G.ANCHOR_BOTTOM)
    pager:SetHAnchor(G.ANCHOR_MIDDLE)
    pager:SetPosition(0, 205, 0)

    local bg = pager:AddChild(Image("images/global.xml", "square.tex"))
    bg:SetSize(260, 56)
    bg:SetTint(0, 0, 0, 0.72)

    local function go(delta)
        local nb = boxes[((idx - 1 + delta) % #boxes) + 1]
        if nb and nb ~= container then
            container.components.container:Close()
            nb.components.container:Open(doer)
        end
    end

    local function arrow(txt, x, dir)
        local b = pager:AddChild(TextButton(txt))
        b:SetFont(G.BUTTONFONT); b:SetTextSize(40)
        b:SetColour(1, 1, 1, 1); b:SetOverColour(1, 0.85, 0.3, 1)
        b:SetPosition(x, 0, 0)
        b:SetOnClick(function() go(dir) end)
        return b
    end
    arrow("<", -100, -1)
    arrow(">", 100, 1)

    local lbl = pager:AddChild(Text(G.NUMBERFONT or G.BUTTONFONT, 30))
    lbl:SetColour(1, 1, 1, 1)
    lbl:SetPosition(0, 0, 0)
    lbl:SetString(idx .. " / " .. #boxes .. "  箱")
end

AddClassPostConstruct("widgets/containerwidget", function(self)
    local _Open = self.Open
    self.Open = function(self, container, doer)
        _Open(self, container, doer)
        if container and container.prefab == "omni_box" and doer then
            build_pager(container, doer)
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

print("[omnidsm/boxpages] 多箱翻页已加载")
