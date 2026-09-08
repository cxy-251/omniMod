--[[ 随身箱子（联机版，自制，仿 Portable Cellar 的核心体验，代码全新写）

  - 背包物品，能放进默认物品格 → 多个箱子塞满物品栏 = 全部家当随身
  - 大容量：四区，中间十字留宽间隔（联机版网络槽位上限 ~15，这里顶到 80）
  - 箱子里食物**反鲜**（放进去 SetPercent(1) + StopPerishing），拿出来恢复腐坏
  - 箱子里每格可堆到 999，接受整叠
  - 能跨世界携带（就是个 inventoryitem，随人走）
  - 不能把箱子塞进箱子（itemtestfn 拦掉）

  ★ 联机版容器的 itemtestfn / type / acceptsstacks / widget 在 WidgetSetup 之后
    是**只读**的，必须写进 containers.params，不能在 prefab 里赋值（否则崩
    "Cannot change read only property"）。

  由 modmain.lua 的 PrefabFiles 注册。
]]

local assets =
{
    Asset("ANIM", "anim/treasure_chest.zip"),
}

-- 四区网格：每区 QC×QR，中间十字留 GAP 间隔。总格数 = 4 * QC * QR。
-- containerwidget 会整体 ×0.6 缩放，坐标可放大些。
local QC, QR = 5, 4
local STEP   = 64
local GAP    = 48

local slotpos = {}
for _, qy in ipairs({ 1, -1 }) do
    for _, qx in ipairs({ -1, 1 }) do
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
    if not (item and item.components) then return end
    -- 食物反鲜 + 不腐
    if item.components.perishable then
        item.components.perishable:SetPercent(1)
        item.components.perishable:StopPerishing()
    end
    -- 无限堆叠：用官方 API（直接改 stackable.originalmaxsize 会崩，它是只读的）
    if item.components.stackable then
        item.components.stackable:SetIgnoreMaxSize(true)
    end
end

local function on_itemlose(inst, data)
    local item = data and data.item
    if not (item and item.components) then return end
    -- 拿出来恢复腐坏；堆叠上限故意不还原（保留大叠，方便随身带）
    if item.components.perishable then
        item.components.perishable:StartPerishing()
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

------------------------------------------------------------------ 容器参数（中心化）
local containers = require("containers")
containers.params.omni_box =
{
    acceptsstacks = true,
    type          = "chest",
    onopenfn      = onopen,
    onclosefn     = onclose,
    skipopensnd   = true,
    skipclosesnd  = true,
    widget =
    {
        slotpos   = slotpos,
        slotscale = 0.85,
        animbank  = "ui_chest_3x3",
        animbuild = "ui_chest_3x3",
        pos       = Vector3(0, 40, 0),
        side_align_tip = 160,
    },
}
function containers.params.omni_box.itemtestfn(container, item, slot)
    return item == nil or item.prefab ~= "omni_box"
end
-- 顶高网络槽位上限（否则 container_classified 建网络槽会崩）
containers.MAXITEMSLOTS = math.max(containers.MAXITEMSLOTS or 0, #slotpos)

------------------------------------------------------------------ 预制物
local function fn()
    local inst = CreateEntity()
    inst.entity:AddTransform()
    inst.entity:AddAnimState()
    inst.entity:AddSoundEmitter()
    inst.entity:AddNetwork()

    MakeInventoryPhysics(inst)

    inst.AnimState:SetBank("chest")
    inst.AnimState:SetBuild("treasure_chest")
    inst.AnimState:PlayAnimation("closed")

    inst:AddTag("fridge")

    MakeInventoryFloatable(inst, "med", 0.1, 0.75)

    inst.entity:SetPristine()
    if not TheWorld.ismastersim then
        return inst
    end

    inst:AddComponent("inspectable")

    inst:AddComponent("inventoryitem")
    inst.components.inventoryitem.cangoincontainer = true
    inst.components.inventoryitem.imagename = "krampus_sack"

    inst:AddComponent("container")
    inst.components.container:WidgetSetup("omni_box")

    inst:ListenForEvent("itemget", on_itemget)
    inst:ListenForEvent("itemlose", on_itemlose)

    inst:AddComponent("hauntable")
    inst.components.hauntable:SetHauntValue(TUNING.HAUNT_TINY)

    return inst
end

return Prefab("omni_box", fn, assets)
