--[[ 懒人护符（单机饥荒里是「橙色护符」orangeamulet）不再消耗耐久。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  橙色护符 = The Lazy Forager，戴上自动捡拾附近掉落物，用 fueled（噩梦燃料）当耐久。
  把消耗速率清零 —— 戴着永远不掉耐久，但仍可正常充能。
]]

local G = GLOBAL

AddPrefabPostInit("orangeamulet", function(inst)
    if inst and inst.components and inst.components.fueled then
        inst.components.fueled.rate = 0
        inst.components.fueled.SetRate = function(self) self.rate = 0 end
    end
end)

print("[omnidsm/lazyforager] 橙色护符（懒人护符）已设为不掉耐久")
