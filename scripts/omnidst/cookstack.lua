--[[ 烹饪相关（联机版）：（modimport 加载，跑在 mod 环境）
  1. 烹饪锅 / 便携烹饪锅 接受整叠食材，一次做出整叠成品
  2. 便携烹饪锅任何人物都能造（去掉「masterchef」限制）

  ★ 联机版 cookpot 容器默认 acceptsstacks = false（每格只能放 1 个）——
    这就是「烹饪锅不能堆叠」的原因。改 containers.params 里的这个字段。
  ★ Stewer:StartCooking 抓完 ingredient_prefabs 立刻 DestroyContents，所以要在
    调原函数前算好各格最小堆叠数、把多的返还、只留 stack，记 self.foodstack；
    Harvest 时按 stack 补足产出。服务器侧。
]]

local G = GLOBAL
local cooking = G.require("cooking")

-- 1) 让烹饪锅/便携锅接受整叠
do
    local ok, containers = G.pcall(G.require, "containers")
    if ok and containers and containers.params then
        for _, name in ipairs({ "cookpot", "portablecookpot", "archive_cookpot" }) do
            if containers.params[name] then
                containers.params[name].acceptsstacks = true
            end
        end
        print("[omnidst/cookstack] 烹饪锅已改为接受整叠食材")
    end
end

-- 2) 便携烹饪锅：任何人物都能造 + 都能摆放使用
do
    local r = G.AllRecipes and G.AllRecipes["portablecookpot_item"]
    if r then
        r.builder_tag = nil       -- 造：去掉「大厨」限制
        r.builder_skill = nil
    end
end
-- 摆放：deployable.restrictedtag = "masterchef" 是「只有沃利能放下」的关卡
AddPrefabPostInit("portablecookpot_item", function(inst)
    if inst.components and inst.components.deployable then
        inst.components.deployable.restrictedtag = nil
    end
end)
-- 使用：便携锅/香料锅/搅拌机 带 "mastercookware" 标签，开容器时 CanOpenCharacterSpecificContainer
-- 检查 doer 有没有 "masterchef" —— 没有就报「烹饪水平还不够」。直接给所有玩家挂上 masterchef。
AddPlayerPostInit(function(inst)
    if G.TheWorld and G.TheWorld.ismastersim then
        inst:AddTag("masterchef")
    end
end)
print("[omnidst/cookstack] 便携烹饪锅已对所有人物解锁（造 + 摆放 + 使用）")

-- 3) 一锅煮整叠 + 急速烹饪
AddComponentPostInit("stewer", function(self)
    -- 急速烹饪：把烹饪时间压到 2%
    self.cooktimemult = (self.cooktimemult or 1) * 0.02

    local _Start = self.StartCooking
    self.StartCooking = function(self, doer, ...)
        local cont = self.inst.components.container
        if self.targettime == nil and cont ~= nil then
            local stack = 9999
            for _, v in pairs(cont.slots) do
                if v.components.stackable then
                    stack = math.min(stack, v.components.stackable:StackSize())
                else
                    stack = 1
                end
            end
            if stack == 9999 or stack < 1 then stack = 1 end

            -- 无论 stack 是几，都把每格多出来的返还，格子只留 stack —— 这样绝不会「吃掉」多余食材
            for _, v in pairs(cont.slots) do
                local st = v.components.stackable
                if st and st:StackSize() > stack then
                    local extra = st:StackSize() - stack
                    local back = G.SpawnPrefab(v.prefab)
                    if back then
                        if back.components.stackable then back.components.stackable:SetStackSize(extra) end
                        if back.components.perishable and v.components.perishable then
                            back.components.perishable:SetPercent(v.components.perishable:GetPercent())
                        end
                        if doer and doer.components.inventory then
                            doer.components.inventory:GiveItem(back, nil, self.inst:GetPosition())
                        else
                            back.Transform:SetPosition(self.inst.Transform:GetWorldPosition())
                        end
                        st:SetStackSize(stack)
                    end
                end
            end
            self.foodstack = stack
        end
        return _Start(self, doer, ...)
    end

    local _Harvest = self.Harvest
    self.Harvest = function(self, harvester, ...)
        local stack = self.foodstack
        local product, spoil = self.product, self.product_spoilage
        local ret = _Harvest(self, harvester, ...)
        if stack and stack > 1 and product then
            local rec = cooking.GetRecipe(self.inst.prefab, product)
            local per = (rec and rec.stacksize) or 1
            local total = per * (stack - 1)
            while total > 0 do
                local n = math.min(total, 40)
                local food = G.SpawnPrefab(product)
                if not food then break end
                if food.components.stackable then food.components.stackable:SetStackSize(n) end
                if food.components.perishable and spoil then food.components.perishable:SetPercent(spoil) end
                if harvester and harvester.components.inventory then
                    harvester.components.inventory:GiveItem(food, nil, self.inst:GetPosition())
                else
                    food.Transform:SetPosition(self.inst.Transform:GetWorldPosition())
                end
                total = total - n
            end
        end
        self.foodstack = nil
        return ret
    end

    local _OnSave = self.OnSave
    self.OnSave = function(self, ...)
        local data = _OnSave(self, ...)
        if data and self.foodstack then data.foodstack = self.foodstack end
        return data
    end
    local _OnLoad = self.OnLoad
    self.OnLoad = function(self, data, ...)
        local r = _OnLoad(self, data, ...)
        if data and data.foodstack then self.foodstack = data.foodstack end
        return r
    end
end)

print("[omnidst/cookstack] 一锅煮整叠已加载")
