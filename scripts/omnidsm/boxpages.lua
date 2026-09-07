--[[ 多箱翻页 + 开箱暂停。（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  - 打开任意「随身箱子」→ 屏幕底部中间出现 <  i / n  >，翻遍所有箱子（一个窗口）。
    翻页控件挂 HUD、固定屏幕位置、深色底亮字，黑夜也看得清；箱子一关就 Kill。
    翻页 = 关当前箱子 + 打开列表里上/下一个。
  - 打开随身箱子时给玩家挂 notarget（生物不来打你），关闭时摘掉。
    没用硬暂停 —— 硬暂停会连带把"从箱子里拿东西"也卡住。
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

local pager
local shielded

local function kill_pager()
    if pager and pager.inst and pager.inst:IsValid() then pager:Kill() end
    pager = nil
end

local function box_unshield()
    if shielded and shielded:IsValid() and shielded._omni_added_notarget then
        shielded:RemoveTag("notarget")
        shielded._omni_added_notarget = nil
    end
    shielded = nil
end

local function box_shield(doer)
    box_unshield()
    if doer and doer:IsValid() and not doer:HasTag("notarget") then
        doer:AddTag("notarget")
        doer._omni_added_notarget = true
        shielded = doer
    end
end

local function build_pager(container, doer)
    kill_pager()
    local boxes = my_boxes(doer)
    if not (doer.HUD and doer.HUD.controls) or #boxes < 2 then return end
    local idx = 1
    for i, b in ipairs(boxes) do if b == container then idx = i break end end

    local Widget     = G.require("widgets/widget")
    local Image      = G.require("widgets/image")
    local Text       = G.require("widgets/text")
    local TextButton = G.require("widgets/textbutton")

    pager = doer.HUD.controls:AddChild(Widget("omni_boxpager"))
    pager:SetVAnchor(G.ANCHOR_BOTTOM)
    pager:SetHAnchor(G.ANCHOR_MIDDLE)
    pager:SetPosition(0, 90, 0)   -- 屏幕底部中间上方一点，在物品栏之上

    local bg = pager:AddChild(Image("images/global.xml", "square.tex"))
    bg:SetSize(220, 44)
    bg:SetTint(0, 0, 0, 0.72)
    bg:SetClickable(false)

    local function go(delta)
        local nb = boxes[((idx - 1 + delta) % #boxes) + 1]
        if nb and nb ~= container then
            container.components.container:Close()
            nb.components.container:Open(doer)
        end
    end
    local function arrow(txt, x, dir)
        local b = pager:AddChild(TextButton(txt))
        b:SetFont(G.BUTTONFONT); b:SetTextSize(38)
        b:SetColour(1, 1, 1, 1); b:SetOverColour(1, 0.85, 0.3, 1)
        b:SetPosition(x, 0, 0)
        b:SetOnClick(function() go(dir) end)
    end
    arrow("<", -88, -1)
    arrow(">", 88, 1)

    local lbl = pager:AddChild(Text(G.NUMBERFONT or G.BUTTONFONT, 28))
    lbl:SetColour(1, 1, 1, 1)
    lbl:SetPosition(0, 0, 0)
    lbl:SetString(idx .. " / " .. #boxes .. "  箱")
end

AddClassPostConstruct("widgets/containerwidget", function(self)
    local _Open = self.Open
    self.Open = function(self, container, doer)
        _Open(self, container, doer)
        if container and container.prefab == "omni_box" and doer then
            box_shield(doer)
            build_pager(container, doer)
        else
            kill_pager()
        end
    end

    local _Close = self.Close
    self.Close = function(self, ...)
        kill_pager()
        box_unshield()
        return _Close(self, ...)
    end
end)

print("[omnidsm/boxpages] 多箱翻页 + 开箱暂停 已加载")
