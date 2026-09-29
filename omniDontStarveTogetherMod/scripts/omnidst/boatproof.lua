--[[ 船不破损、无限耐久（联机版）。（modimport 加载，跑在 mod 环境）

  联机版的船是一个带 health 组件的平台实体，撞东西由 hullhealth:OnCollide 算伤害并
  生成漏洞（boatleak），草船还会自己慢慢腐坏（hullhealth 的 OnUpdate）。
  开关打开时：船的 health 不再减少；撞击不再算伤害、不再生成漏洞；不再自己腐坏。
  已经存在的漏洞不会自动消失，拿木板补一下即可。

  只在服务器跑（船的血量是服务器状态）。开关 state.boat，默认开，控制台 omni_boat(true/false)。
]]

local G = GLOBAL

local function S()
    G.OMNIDST = G.OMNIDST or {}
    G.OMNIDST.state = G.OMNIDST.state or {}
    local s = G.OMNIDST.state
    if s.boat == nil then s.boat = true end
    return s
end

AddComponentPostInit("health", function(Health)
    local _DoDelta = Health.DoDelta
    function Health:DoDelta(amount, ...)
        if S().boat and amount and amount < 0 and self.inst:HasTag("boat") then return 0 end
        return _DoDelta(self, amount, ...)
    end
end)

AddComponentPostInit("hullhealth", function(HullHealth)
    local _OnCollide = HullHealth.OnCollide
    function HullHealth:OnCollide(...)
        if S().boat then return end
        return _OnCollide(self, ...)
    end
    local _OnUpdate = HullHealth.OnUpdate
    if _OnUpdate then
        function HullHealth:OnUpdate(...)
            if S().boat then return end
            return _OnUpdate(self, ...)
        end
    end
end)

G.omni_boat = function(on)
    if G.TheWorld and not G.TheWorld.ismastersim and G.TheNet and G.TheNet.SendRemoteExecute then
        G.TheNet:SendRemoteExecute("omni_boat(" .. tostring(on ~= false) .. ")")
    end
    S().boat = (on ~= false)
    print("[omnidst/boatproof] 船无限耐久：" .. (S().boat and "开" or "关"))
end

print("[omnidst/boatproof] 已加载（船无限耐久，omni_boat 可关）")
