--[[ 懒人护符（橙色护符 orangeamulet）不再消耗耐久。（modimport 加载，跑在 mod 环境）

  联机版橙色护符用的是 finiteuses 组件（SetOnFinished(inst.Remove)），戴上后每
  ORANGEAMULET_ICD 秒自动捡一次附近掉落物，每次 finiteuses:Use(1)。
  联机版的 FiniteUses 没有 unlimited_uses 标志 —— 直接把这个实例的 Use / OnUsedAsItem
  变成空操作，并保持满耐久。服务器侧。
]]

local G = GLOBAL

AddPrefabPostInit("orangeamulet", function(inst)
    if not (G.TheWorld and G.TheWorld.ismastersim) then return end
    if not (inst and inst.components) then return end
    local fu = inst.components.finiteuses
    if not fu then return end
    fu.Use = function() end
    fu.OnUsedAsItem = function() end
    inst:DoTaskInTime(0, function()
        if inst:IsValid() and inst.components.finiteuses then
            inst.components.finiteuses:SetUses(inst.components.finiteuses.total)
        end
    end)
end)

print("[omnidst/lazyforager] 橙色护符（懒人护符）已设为不掉耐久")
