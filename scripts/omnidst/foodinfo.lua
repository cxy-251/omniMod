--[[ 鼠标指到食物：显示 饥/血/理智 数值 + 新鲜度。（modimport 加载，跑在 mod 环境）

  跟 cheats 里的生物血量一个套路：钩 widgets/hoverer:OnUpdate，指到有 edible 组件的
  实体就在提示末尾补数值。always-on。
]]

local G = GLOBAL

local function food_suffix(inst)
    local e = inst and inst.components and inst.components.edible
    if not e then return nil end
    local p = G.ThePlayer
    local function r(x) return math.floor((x or 0) * 10 + 0.5) / 10 end
    local hu = (p and e.GetHunger and r(e:GetHunger(p))) or r(e.hungervalue)
    local he = (p and e.GetHealth and r(e:GetHealth(p))) or r(e.healthvalue)
    local sa = (p and e.GetSanity and r(e:GetSanity(p))) or r(e.sanityvalue)
    local parts = {}
    if hu ~= 0 then parts[#parts + 1] = "饥" .. (hu > 0 and "+" or "") .. hu end
    if he ~= 0 then parts[#parts + 1] = "血" .. (he > 0 and "+" or "") .. he end
    if sa ~= 0 then parts[#parts + 1] = "智" .. (sa > 0 and "+" or "") .. sa end
    if inst.components.perishable then
        parts[#parts + 1] = "鲜" .. math.floor(inst.components.perishable:GetPercent() * 100 + 0.5) .. "%"
    end
    if #parts == 0 then return nil end
    return "  " .. table.concat(parts, " ")
end

AddClassPostConstruct("widgets/hoverer", function(self)
    local _OnUpdate = self.OnUpdate
    if not _OnUpdate then return end
    self.OnUpdate = function(self, ...)
        _OnUpdate(self, ...)
        local ent = G.TheInput and G.TheInput:GetWorldEntityUnderMouse()
        if not (ent and ent.components and ent.components.edible) then return end
        local sfx = food_suffix(ent)
        if not sfx then return end
        local base = (self.str ~= nil and self.str)
                     or (ent.GetDisplayName and ent:GetDisplayName())
                     or ent.name or ""
        self.text:SetString(base .. sfx)
        self.text:Show()
        self.str = base .. sfx
    end
end)

print("[omnidst/foodinfo] 食物数值悬停已加载")
