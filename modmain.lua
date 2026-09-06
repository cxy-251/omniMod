--[[ OmniDontStarveMod — 自制单机《饥荒》mod（对标 ONI 的 omniMod）

  功能按需一个个加：一个功能一个文件放 scripts/omnidsm/<名字>.lua，
  在下面 FEATURES 里登记。每个文件用 modimport 加载 —— 直接在文件作用域里干活，
  不用返回表。

  ★ 单机饥荒 mod 环境（modimport 进来的文件）里能直接用的裸名很有限：
    pairs ipairs print math table type string tostring Class GLOBAL TUNING
    Prefab Asset Ingredient modname MODROOT modimport
    + AddSimPostInit / AddPlayerPostInit / AddPrefabPostInit / AddComponentPostInit
      / AddClassPostConstruct / AddGamePostInit ...
  其它一律走 GLOBAL.xxx —— 包括 require / pcall / tonumber / assert，
  以及所有游戏运行时全局（TheFrontEnd / GetPlayer / GetWorld / SetPause /
  ANCHOR_MIDDLE / TITLEFONT / CONTROL_CANCEL / Vector3 / PlayerProfile ...）。
]]

local MODNAME = "OmniDontStarveMod"
local VERSION = "0.0.3"

local FEATURES = {
    "unlockchars",   -- 解锁所有人物
    "cheats",        -- 地图全开/行走速度/科技全解锁/锁血下限10/伤害倍率（默认全开，控制台 omni_* 可调）
    "cheatmenu",     -- 上面这些的游戏内菜单（暂停菜单→作弊菜单），手柄 + 键鼠都能用
    -- "treeshake_bear",
    -- "autopickup",
    -- "janitor",
}

local pcall = GLOBAL.pcall

for _, name in ipairs(FEATURES) do
    local ok, err = pcall(modimport, "scripts/omnidsm/" .. name .. ".lua")
    if ok then
        print(("[%s] feature loaded: %s"):format(MODNAME, name))
    else
        print(("[%s] feature FAILED: %s -> %s"):format(MODNAME, name, tostring(err)))
    end
end

print(("[%s] v%s loaded (%d feature(s))"):format(MODNAME, VERSION, #FEATURES))
