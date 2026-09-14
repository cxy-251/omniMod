--[[ 随身箱子（联机版）：配方 + 文本 + 隔箱合成 + 采集自动堆叠进箱子。
     （modimport 加载，跑在 mod 环境。箱子预制物见 scripts/prefabs/omni_box.lua）

  联机版 Inventory:Has / GetCraftingIngredient 本来就会把「已打开的容器」算进合成材料。
  这里额外把物品栏里**没打开的** omni_box 也算进去 —— 不用先开箱就能合成/建造。
]]

local G = GLOBAL

------------------------------------------------------------------ 文本 + 配方
G.STRINGS.NAMES.OMNI_BOX = "随身箱子"
G.STRINGS.RECIPE_DESC.OMNI_BOX = "四区大容量 · 食物反鲜不腐 · 每格 999 · 可放进物品栏随身带"
G.STRINGS.CHARACTERS.GENERIC.DESCRIBE.OMNI_BOX = "全部家当都在里面。"

-- 借用香包 krampus_sack 的图标（它是真实物品，.tex 一定在物品栏图集里）
AddRecipe2("omni_box",
    { Ingredient("cutgrass", 1) },
    G.TECH.NONE,
    { image = "krampus_sack.tex" },
    { "CONTAINERS" })

------------------------------------------------------------------ 隔箱合成
local function closed_boxes(inv)
    local list, seen = {}, {}
    if not inv then return list end
    local function add(v)
        if v and v.prefab == "omni_box" and v.components and v.components.container and not seen[v]
           and not (inv.opencontainers and inv.opencontainers[v]) then
            seen[v] = true
            list[#list + 1] = v
        end
    end
    for _, v in pairs(inv.itemslots or {}) do add(v) end
    add(inv.activeitem)
    local of = inv.GetOverflowContainer and inv:GetOverflowContainer()
    if of then for _, v in pairs(of.slots or {}) do add(v) end end
    return list
end

AddComponentPostInit("inventory", function(Inventory)
    ---------------------------------------------------------------- Has
    local _Has = Inventory.Has
    function Inventory:Has(item, amount, checkallcontainers)
        local enough, found = _Has(self, item, amount, checkallcontainers)
        if checkallcontainers then
            for _, box in ipairs(closed_boxes(self)) do
                local _, bf = box.components.container:Has(item, amount, true)
                found = found + (bf or 0)
            end
        end
        return found >= amount, found
    end

    ---------------------------------------------------------------- GetCraftingIngredient
    local _GCI = Inventory.GetCraftingIngredient
    function Inventory:GetCraftingIngredient(item, amount)
        local ci = _GCI(self, item, amount)
        local total = 0
        for _, c in pairs(ci) do total = total + c end
        if total >= amount then return ci end
        for _, box in ipairs(closed_boxes(self)) do
            for ent, cnt in pairs(box.components.container:GetCraftingIngredient(item, amount - total, true)) do
                ci[ent] = cnt
                total = total + cnt
            end
            if total >= amount then break end
        end
        return ci
    end

    ---------------------------------------------------------------- RemoveItem（合成消耗时物品可能在箱子里）
    local _RI = Inventory.RemoveItem
    function Inventory:RemoveItem(item, wholestack, checkallcontainers, keepoverstacked)
        if item and item.components and item.components.inventoryitem then
            for _, box in ipairs(closed_boxes(self)) do
                local c = box.components.container
                for _, v in pairs(c.slots) do
                    if v == item then
                        if not wholestack and item.components.stackable and item.components.stackable:StackSize() > 1 then
                            return item.components.stackable:Get()
                        end
                        return c:RemoveItem(item, wholestack)
                    end
                end
            end
        end
        return _RI(self, item, wholestack, checkallcontainers, keepoverstacked)
    end

    ---------------------------------------------------------------- 采集自动堆叠进箱子
    local _GiveItem = Inventory.GiveItem
    function Inventory:GiveItem(inst, slot, src_pos)
        if not slot and inst and inst.prefab and inst.components and inst.components.stackable
           and inst.components.inventoryitem and not inst.components.inventoryitem:IsHeldBy(self.inst) then
            for _, box in ipairs(closed_boxes(self)) do
                for _, v in pairs(box.components.container.slots) do
                    if v ~= inst and v.prefab == inst.prefab and v.components.stackable
                       and not v.components.stackable:IsFull() then
                        local leftover = v.components.stackable:Put(inst)
                        if v.components.perishable then v.components.perishable:SetPercent(1) end
                        if leftover == nil then return true end
                        inst = leftover
                    end
                end
            end
        end
        return _GiveItem(self, inst, slot, src_pos)
    end
end)

print("[omnidst/box] 随身箱子（配方 + 隔箱合成）已加载")
