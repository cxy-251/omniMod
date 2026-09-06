--[[ 隔箱合成：合成/建造时，把「随身箱子」(omni_box) 里的材料也算进去，不用先打开箱子。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  改写玩家 inventory 组件的三个方法：
    Count               -> Has / 配方是否可造 能看见箱子里的
    GetCraftingIngredient -> 合成取材料时补上箱子里的
    RemoveItem          -> builder 扣材料时，实体在箱子里就从箱子扣

  已经打开的箱子（在 opencontainers 里）跳过，避免和原生逻辑重复计数。
]]

local G = GLOBAL

local function boxes_of(inv)
    local list, seen = {}, {}
    if not inv then return list end
    local function add(v)
        if v and v.prefab == "omni_box" and v.components and v.components.container
           and not (inv.opencontainers and inv.opencontainers[v]) and not seen[v] then
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
end)

print("[omnidsm/box_craft] 隔箱合成已启用")
