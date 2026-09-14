--[[ 随身箱子(omni_box)与物品栏的整合：
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  1) 隔箱合成：合成/建造时，把物品栏（含背包）里所有 omni_box 的内容也算进去，
     不用先打开箱子。改写 inventory 的 Count / GetCraftingIngredient / RemoveItem。
     已打开的箱子（在 opencontainers）跳过，避免和原生逻辑重复计数。

  2) 采集自动堆叠进箱子：拿到某物品时，如果哪个箱子里已经有它、且那格没满，
     就直接堆进箱子那格，而不是占用物品栏。改写 inventory 的 GiveItem。
]]

local G = GLOBAL

-- include_open=true 时连"已打开的箱子"也算进来
local function boxes_of(inv, include_open)
    local list, seen = {}, {}
    if not inv then return list end
    local function add(v)
        if v and v.prefab == "omni_box" and v.components and v.components.container and not seen[v]
           and (include_open or not (inv.opencontainers and inv.opencontainers[v])) then
            seen[v] = true
            list[#list + 1] = v
        end
    end
    for _, v in pairs(inv.itemslots or {}) do add(v) end
    add(inv.activeitem)
    if inv.overflow and inv.overflow.components and inv.overflow.components.container then
        for _, v in pairs(inv.overflow.components.container.slots or {}) do add(v) end
    end
    return list
end

AddComponentPostInit("inventory", function(Inventory)
    ---------------------------------------------------------------- 隔箱合成
    local _Count = Inventory.Count
    function Inventory:Count(item, checkall)
        local n = _Count(self, item, checkall)
        for _, box in ipairs(boxes_of(self)) do
            n = n + box.components.container:Count(item)
        end
        return n
    end

    local _GCI = Inventory.GetCraftingIngredient
    function Inventory:GetCraftingIngredient(item, amount)
        local ci = _GCI(self, item, amount)
        local total = 0
        for _, c in pairs(ci) do total = total + c end
        if total >= amount then return ci end
        for _, box in ipairs(boxes_of(self)) do
            for ent, cnt in pairs(box.components.container:GetCraftingIngredient(item, amount - total, true)) do
                ci[ent] = cnt
                total = total + cnt
            end
            if total >= amount then break end
        end
        return ci
    end

    local _RI = Inventory.RemoveItem
    function Inventory:RemoveItem(item, wholestack, checkall)
        if item and item.components and item.components.inventoryitem then
            for _, box in ipairs(boxes_of(self)) do
                local c = box.components.container
                for _, v in pairs(c.slots) do
                    if v == item then
                        if not wholestack and item.components.stackable
                           and item.components.stackable:StackSize() > 1 then
                            return item.components.stackable:Get()
                        end
                        return c:RemoveItem(item, wholestack)
                    end
                end
            end
        end
        return _RI(self, item, wholestack, checkall)
    end

    ---------------------------------------------------------------- 采集自动堆叠进箱子
    local _GiveItem = Inventory.GiveItem
    function Inventory:GiveItem(inst, slot, screen_src_pos, skipsound)
        if not slot and inst and inst.prefab and inst.components and inst.components.stackable
           and inst.components.inventoryitem and not inst.components.inventoryitem:IsHeld() then
            for _, box in ipairs(boxes_of(self, true)) do
                for _, v in pairs(box.components.container.slots) do
                    if v ~= inst and v.prefab == inst.prefab and v.components.stackable
                       and not v.components.stackable:IsFull() then
                        local leftover = v.components.stackable:Put(inst, screen_src_pos)
                        if v.components.perishable then v.components.perishable:SetPercent(1) end
                        if leftover == nil then return true end
                        inst = leftover
                    end
                end
            end
        end
        return _GiveItem(self, inst, slot, screen_src_pos, skipsound)
    end
end)

print("[omnidsm/box_craft] 隔箱合成 + 采集自动堆叠 已启用")
