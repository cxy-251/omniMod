--[[ 懒人护符（橙色护符 orangeamulet = "The Lazy Forager"）不再消耗耐久。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  这个 build 里橙色护符的耐久是 **finiteuses**（不是 fueled）—— 戴上后每 ICD 秒
  自动捡一次附近掉落物，每捡一件 `finiteuses:Use(1)`。
  `FiniteUses:Use` 里判断 `if not self.unlimited_uses then ...`，所以把
  `unlimited_uses = true` 就永远不掉耐久（耐久条也一直满）。
]]

AddPrefabPostInit("orangeamulet", function(inst)
    if not (inst and inst.components) then return end
    if inst.components.finiteuses then
        inst.components.finiteuses.unlimited_uses = true
        inst.components.finiteuses:SetPercent(1)
    end
    if inst.components.fueled then
        inst.components.fueled.rate = 0
    end
end)

print("[omnidsm/lazyforager] 橙色护符（懒人护符）已设为不掉耐久")
