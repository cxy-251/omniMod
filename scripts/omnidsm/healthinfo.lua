--[[ 鼠标指到生物：在名字后面显示 [当前/最大] 攻X。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  抄 Health Info Plus 的思路，两处都要改：
  1) EntityScript:GetDisplayName —— 空手悬停时 hoverer 用它拼名字。
  2) BufferedAction:GetActionString —— 手里拿着工具/武器时，hoverer 走的是动作字符串，
     不会再拼一次名字（它判断 `lmb.invobject == nil` 才拼）。所以在 playercontroller
     的 GetLeftMouseAction 里打个标记，再在 GetActionString 里补上血量。

  开关：state.hpbar（cheats 模块），默认开。控制台 omni_hpbar(true/false)。
]]

local G = GLOBAL

local function on()
    return not (G.OMNIDSM and G.OMNIDSM.state) or G.OMNIDSM.state.hpbar ~= false
end

local function suffix(target)
    if not (target and target.components and target.components.health) then return nil end
    local hc = target.components.health
    local dmg = target.components.combat and target.components.combat.defaultdamage or 0
    return string.format("  [%d/%d]%s",
        math.floor(hc.currenthealth + 0.5), math.floor(hc.maxhealth + 0.5),
        dmg > 0 and ("  攻" .. math.floor(dmg)) or "")
end

-- 1) 空手悬停
do
    local ES = G.EntityScript
    if ES and ES.GetDisplayName then
        local _gdn = ES.GetDisplayName
        function ES:GetDisplayName(...)
            local name = _gdn(self, ...)
            if on() and type(name) == "string" and self ~= (G.GetPlayer and G.GetPlayer()) then
                local s = suffix(self)
                if s then name = name .. s end
            end
            return name
        end
    end
end

-- 2) 手持工具时（走动作字符串那条路）
do
    local PC = G.require("components/playercontroller")
    local BA = G.BufferedAction
    if PC and BA then
        local flag = false

        local _GLMA = PC.GetLeftMouseAction
        function PC:GetLeftMouseAction(...)
            local lmb = _GLMA(self, ...)
            -- hoverer 只有在 (target 且 invobject==nil 且 target~=doer) 时才自己拼名字；
            -- 其它情况（手里有东西）需要我们在动作字符串里补
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
            if on() and flag and type(str) == "string" and self.target then
                local s = suffix(self.target)
                if s then str = str .. s end
            end
            flag = false
            return str
        end
    end
end

print("[omnidsm/healthinfo] 生物血量显示已加载")
