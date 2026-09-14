--[[ 一锅煮整叠：炊具（锅等）里放整叠食材，一次做出整叠成品。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  抄 Cook Stack Food 的精简版：包 stewer 的 StartCooking / Harvest / OnSave / OnLoad。
  - StartCooking：算出各格里最小的堆叠数 stack，把多出来的返还给玩家，记 self.foodstack。
  - Harvest：原版给 1 份，再补 (stack-1)*配方产量 份，按堆叠上限分次给。
  - OnSave/OnLoad：存 foodstack。
  always-on，无开关。
]]

local G = GLOBAL
local cooking = G.require("cooking")

AddComponentPostInit("stewer", function(self)
    local _Start = self.StartCooking
    self.StartCooking = function(self, ...)
        local stack = 9999
        for _, v in pairs(self.inst.components.container.slots) do
            if v.components.stackable then
                stack = math.min(v.components.stackable:StackSize(), stack)
            else
                stack = 1
                break
            end
        end
        if stack == 9999 then stack = 1 end

        -- 多出来的食材返还
        for _, v in pairs(self.inst.components.container.slots) do
            if v.components.stackable then
                local extra = v.components.stackable:StackSize() - stack
                if extra > 0 then
                    local food = G.SpawnPrefab(v.prefab)
                    if food then
                        food.components.stackable:SetStackSize(extra)
                        if food.components.perishable and v.components.perishable then
                            food.components.perishable:SetPercent(v.components.perishable:GetPercent())
                        end
                        local p = G.GetPlayer and G.GetPlayer()
                        if p and p.components.inventory then
                            p.components.inventory:GiveItem(food)
                        else
                            food.Transform:SetPosition(self.inst:GetPosition():Get())
                            food.components.inventoryitem:OnDropped(true)
                        end
                    end
                end
            end
        end

        self.foodstack = stack
        return _Start(self, ...)
    end

    local _Harvest = self.Harvest
    self.Harvest = function(self, harvester, ...)
        if not self.done then return _Harvest(self, harvester, ...) end
        local product, stack, spoilage = self.product, self.foodstack, self.product_spoilage
        if product and stack and stack > 1 then
            local rec = cooking.recipes[self.inst.prefab] and cooking.recipes[self.inst.prefab][product]
            local per = (rec and rec.stacksize) or 1
            local total = per * (stack - 1)      -- 额外要给的份数（原版已给 1 份的 per 份）
            while total > 0 do
                local n = math.min(total, 40)
                local food = G.SpawnPrefab(product == "spoiledfood" and "spoiled_food" or product)
                if not food then break end
                if food.components.stackable then food.components.stackable:SetStackSize(n) end
                if food.components.perishable and spoilage then food.components.perishable:SetPercent(spoilage) end
                if harvester and harvester.components.inventory then
                    harvester.components.inventory:GiveItem(food)
                else
                    food.Transform:SetPosition(self.inst:GetPosition():Get())
                    food.components.inventoryitem:OnDropped(true)
                end
                total = total - n
            end
        end
        self.foodstack = nil
        return _Harvest(self, harvester, ...)
    end

    local _OnSave = self.OnSave
    self.OnSave = function(self, ...)
        local data = _OnSave(self, ...)
        if data and self.foodstack then data.foodstack = self.foodstack end
        return data
    end
    local _OnLoad = self.OnLoad
    self.OnLoad = function(self, data, ...)
        local ret = _OnLoad(self, data, ...)
        if data and data.foodstack then self.foodstack = data.foodstack end
        return ret
    end
end)

print("[omnidsm/cookstack] 一锅煮整叠已加载")
