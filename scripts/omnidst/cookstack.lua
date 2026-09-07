--[[ 一锅煮整叠：锅里放整叠食材，一次做出整叠成品。（modimport 加载，跑在 mod 环境）

  联机版 Stewer:StartCooking 抓完 ingredient_prefabs 后立刻 container:DestroyContents()，
  所以要在调用原函数前算好「各格最小堆叠数 stack」、把多的返还玩家、只留 stack，
  记 self.foodstack；Harvest 时按 stack 补足产出。服务器侧（AddComponentPostInit 每实例）。
]]

local G = GLOBAL
local cooking = G.require("cooking")

AddComponentPostInit("stewer", function(self)
    local _Start = self.StartCooking
    self.StartCooking = function(self, doer, ...)
        local cont = self.inst.components.container
        if self.targettime == nil and cont ~= nil then
            -- 1) 最小堆叠数
            local stack = 9999
            for _, v in pairs(cont.slots) do
                if v.components.stackable then
                    stack = math.min(stack, v.components.stackable:StackSize())
                else
                    stack = 1; break
                end
            end
            if stack == 9999 or stack < 1 then stack = 1 end

            -- 2) 把每格多出来的返还玩家，格子里只留 stack 个（DestroyContents 就只吃这么多）
            if stack > 1 then
                for _, v in pairs(cont.slots) do
                    local st = v.components.stackable
                    if st then
                        local extra = st:StackSize() - stack
                        if extra > 0 then
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
            local total = per * (stack - 1)     -- 原版已给 1 份 * per，这里补 (stack-1) 份
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
