--[[ 随身箱子（联机版，自制，仿 Portable Cellar 的核心体验，代码全新写）

  - 背包物品，能放进默认物品格 → 多个箱子塞满物品栏 = 全部家当随身
  - 大容量：四区，中间十字留宽间隔
  - 箱子里食物**反鲜**（放进去 SetPercent(1) + StopPerishing），拿出来恢复腐坏
  - 箱子里每格可堆到 999，接受整叠
  - 能跨世界携带（就是个 inventoryitem，随人走）
  - 不能把箱子塞进箱子（itemtestfn 拦掉）

  由 modmain.lua 的 PrefabFiles 注册。
]]

local assets =
{
    Asset("ANIM", "anim/treasure_chest.zip"),
}

-- 四区网格：每区 QC×QR，中间十字留 GAP 间隔。总格数 = 4 * QC * QR。
-- ★ 联机版容器有网络槽位上限 containers.MAXITEMSLOTS（原版约 15），槽位数就是所有
--   已注册容器里最大的那个。注册完 params 后要手动把它顶上去，否则 container_classified
--   建网络槽时会「wrong number of arguments to 'insert'」崩。80 格已是 4 倍原版，
--   再大网络变量会吃紧。
-- containerwidget 会整体 ×0.6 缩放，所以原始坐标可以放大些。
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

-- 注册容器参数（DST 容器是中心化的）
local containers = require("containers")
containers.params.omni_box =
{
    acceptsstacks = true,
    type = "chest",
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
-- 顶高网络槽位上限
containers.MAXITEMSLOTS = math.max(containers.MAXITEMSLOTS or 0, #slotpos)

local BIG_STACK = 999

local function on_itemget(inst, data)
    local item = data and data.item
    if not (item and item.components) then return end
    if item.components.perishable then
        item.components.perishable:SetPercent(1)
        item.components.perishable:StopPerishing()
    end
    if item.components.stackable then
        local st = item.components.stackable
        st._omni_max = st._omni_max or st.originalmaxsize or st.maxsize
        st.originalmaxsize = st.originalmaxsize or st.maxsize
        st.maxsize = BIG_STACK
    end
end

local function on_itemlose(inst, data)
    local item = data and data.item
    if not (item and item.components) then return end
    if item.components.perishable then
        item.components.perishable:StartPerishing()
    end
    local st = item.components.stackable
    if st and st._omni_max and (st.stacksize or 1) <= st._omni_max then
        st.maxsize = st._omni_max
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

local function itemtest(container, item, slot)
    return item == nil or item.prefab ~= "omni_box"
end

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
    -- 借用香包的物品栏图标；不设 atlasname，让引擎自动从打包的 inventoryimages 里找
    inst.components.inventoryitem.imagename = "krampus_sack"

    inst:AddComponent("container")
    inst.components.container:WidgetSetup("omni_box")
    inst.components.container.itemtestfn = itemtest
    inst.components.container.onopenfn = onopen
    inst.components.container.onclosefn = onclose
    inst.components.container.skipclosesnd = true
    inst.components.container.skipopensnd = true

    inst:ListenForEvent("itemget", on_itemget)
    inst:ListenForEvent("itemlose", on_itemlose)

    inst:AddComponent("hauntable")
    inst.components.hauntable:SetHauntValue(TUNING.HAUNT_TINY)

    return inst
end

return Prefab("omni_box", fn, assets)
