--[[ 鼠标指到食物：显示 饥/血/理智 数值 + 新鲜度。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  跟 healthinfo 一个套路：改 EntityScript:GetDisplayName（空手悬停）+
  BufferedAction:GetActionString（手持物品悬停）。always-on，无开关。
]]

local G = GLOBAL

local function food_suffix(inst)
    if not (inst and inst.components and inst.components.edible) then return nil end
    local p = G.GetPlayer and G.GetPlayer()
    local e = inst.components.edible
    local function r(x) return math.floor(x * 10 + 0.5) / 10 end
    local hu = p and e.GetHunger and r(e:GetHunger(p)) or r(e.hungervalue or 0)
    local he = p and e.GetHealth and r(e:GetHealth(p)) or r(e.healthvalue or 0)
    local sa = p and e.GetSanity and r(e:GetSanity(p)) or r(e.sanityvalue or 0)
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

do
    local ES = G.EntityScript
    if ES and ES.GetDisplayName then
        local _gdn = ES.GetDisplayName
        function ES:GetDisplayName(...)
            local name = _gdn(self, ...)
            if type(name) == "string" then
                local s = food_suffix(self)
                if s then name = name .. s end
            end
            return name
        end
    end
end

do
    local PC = G.require("components/playercontroller")
    local BA = G.BufferedAction
    if PC and BA then
        local flag = false
        local _GLMA = PC.GetLeftMouseAction
        function PC:GetLeftMouseAction(...)
            local lmb = _GLMA(self, ...)
            if lmb and lmb.target and not (lmb.invobject == nil and lmb.target ~= lmb.doer) then
                flag = true
            end
            return lmb
        end
        local _GRMA = PC.GetRightMouseAction
        function PC:GetRightMouseAction(...) flag = false; return _GRMA(self, ...) end
        local _DA = PC.DoAction
        function PC:DoAction(...) flag = false; return _DA(self, ...) end
        local _GAS = BA.GetActionString
        function BA:GetActionString(...)
            local str = _GAS(self, ...)
            if flag and type(str) == "string" and self.target then
                local s = food_suffix(self.target)
                if s then str = str .. s end
            end
            flag = false
            return str
        end
    end
end

print("[omnidsm/foodinfo] 食物数值显示已加载")
