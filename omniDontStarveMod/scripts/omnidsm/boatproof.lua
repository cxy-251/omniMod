--[[ 海难：船不破损、无限耐久。（由 modmain.lua 的 modimport 加载）

  海难的船耐久在 boathealth 组件里，所有减少（行驶自然消耗 / 撞浪 / 被攻击 /
  着火）最终都走 BoatHealth:DoDelta(amount<0)。包一层：开关打开时，负的变化量
  一律抹成 0，并顺手把耐久补满 —— 已经磨损的船一上来也会满血、不再漏水。

  开关：state.boat（默认开）。控制台 omni_boat(true/false)，作弊菜单也有一行。
]]

local G = GLOBAL

local function S()
    G.OMNIDSM = G.OMNIDSM or {}
    G.OMNIDSM.state = G.OMNIDSM.state or {}
    local s = G.OMNIDSM.state
    if s.boat == nil then s.boat = true end
    return s
end

AddComponentPostInit("boathealth", function(BoatHealth)
    local _DoDelta = BoatHealth.DoDelta
    function BoatHealth:DoDelta(amount, damageType, source, ignore_invincible)
        if S().boat and amount and amount < 0 then amount = 0 end
        local r = _DoDelta(self, amount, damageType, source, ignore_invincible)
        if S().boat and self.currenthealth < self.maxhealth then
            _DoDelta(self, self.maxhealth - self.currenthealth, "repair", nil, true)
        end
        return r
    end
end)

G.omni_boat = function(on)
    S().boat = (on ~= false)
    print("[omnidsm/boatproof] 船无限耐久：" .. (S().boat and "开" or "关"))
end

print("[omnidsm/boatproof] 已加载（船无限耐久，omni_boat 可关）")
