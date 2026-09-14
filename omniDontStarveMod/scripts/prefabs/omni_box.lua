--[[ 随身箱子（自制，仿 Portable Cellar 的核心体验，代码全新写）

  - 背包物品，能放进默认物品格 → 多个箱子塞满物品栏 = 全部家当随身
  - 60 格（10×6）大容量
  - 箱子里的食物**完全不腐坏**（放进去 StopPerishing，拿出来 StartPerishing）
  - 箱子里每格可堆到 999
  - 能跨三大世界携带（就是个 inventoryitem，随人走）
  - 不能把箱子塞进箱子（itemtestfn 拦掉，免存档膨胀）

  由 modmain.lua 的 PrefabFiles 注册。
]]

local assets = {
    Asset("ANIM", "anim/treasure_chest.zip"),
}

-- 四个区域，每区 QC×QR，中间十字留一点间隔。总格数 = 4 * QC * QR。
local QC, QR = 10, 7
local STEP   = 64     -- 单格间距（跟原版箱子接近，悬停放大不挤）
local GAP    = 14     -- 十字额外间隔（小一点）

local slotpos = {}
for _, qy in ipairs({ 1, -1 }) do        -- 上、下
    for _, qx in ipairs({ -1, 1 }) do    -- 左、右
        local cx = qx * (QC * STEP / 2 + GAP / 2)
        local cy = qy * (QR * STEP / 2 + GAP / 2)
        for r = 0, QR - 1 do
            for c = 0, QC - 1 do
                table.insert(slotpos, Vector3(
                    cx + (c - (QC - 1) / 2) * STEP,
                    cy + ((QR - 1) / 2 - r) * STEP,
                    0))
            end
        end
    end
end

local BIG_STACK = 999

local function on_itemget(inst, data)
    local item = data and data.item
    if not item or not item.components then return end
    if item.components.perishable then
        item.components.perishable:SetPercent(1)      -- 反鲜：放进去立刻恢复到最新鲜
        item.components.perishable:StopPerishing()    -- 之后也不再腐坏
    end
    if item.components.stackable then
        item.components.stackable._omni_maxsize = item.components.stackable._omni_maxsize
            or item.components.stackable.maxsize
        item.components.stackable.maxsize = BIG_STACK
    end
end

local function on_itemlose(inst, data)
    local item = data and data.item
    if not item or not item.components then return end
    if item.components.perishable then
        item.components.perishable:StartPerishing()   -- 恢复腐坏
    end
    if item.components.stackable and item.components.stackable._omni_maxsize then
        -- 只有当前堆叠没超过原上限才还原，避免还原后溢出丢东西
        if (item.components.stackable.stacksize or 1) <= item.components.stackable._omni_maxsize then
            item.components.stackable.maxsize = item.components.stackable._omni_maxsize
        end
    end
end

local function onopen(inst)
    inst.AnimState:PlayAnimation("open")
    if inst.SoundEmitter then inst.SoundEmitter:PlaySound("dontstarve/wilson/chest_open") end
end

local function onclose(inst)
    inst.AnimState:PlayAnimation("closed")
    if inst.SoundEmitter then inst.SoundEmitter:PlaySound("dontstarve/wilson/chest_close") end
end

local function itemtest(inst, item, slot)
    return item == nil or item.prefab ~= "omni_box"
end

local function fn(Sim)
    local inst = CreateEntity()
    inst.entity:AddTransform()
    inst.entity:AddAnimState()
    inst.entity:AddSoundEmitter()

    MakeInventoryPhysics(inst)

    inst.AnimState:SetBank("chest")
    inst.AnimState:SetBuild("treasure_chest")
    inst.AnimState:PlayAnimation("closed")

    inst:AddComponent("inspectable")

    inst:AddComponent("inventoryitem")
    inst.components.inventoryitem.cangoincontainer = true
    inst.components.inventoryitem.imagename = "krampus_sack"   -- 借用游戏自带的物品栏图标，避免找不到贴图报错

    inst:AddComponent("container")
    inst.components.container:SetNumSlots(#slotpos)
    inst.components.container.widgetslotpos = slotpos
    inst.components.container.widgetpos = Vector3(0, 30, 0)  -- 网格 + 底部翻页条整体大致居中
    inst.components.container.side_align_tip = 0
    inst.components.container.type = "chest"
    inst.components.container.itemtestfn = itemtest
    inst.components.container.onopenfn = onopen
    inst.components.container.onclosefn = onclose

    -- 冰箱级 + 完全冻结腐坏 + 999 堆叠
    inst:AddTag("fridge")
    inst:ListenForEvent("itemget", on_itemget)
    inst:ListenForEvent("itemlose", on_itemlose)

    return inst
end

return Prefab("common/inventory/omni_box", fn, assets)
