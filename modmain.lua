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

-- 让 mod 环境（含 modimport 的文件、PrefabFiles 的预制物文件）读不到的裸名
-- 回退到 _G —— 这样 CreateEntity / MakeInventoryPhysics / Vector3 / STRINGS / TheSim
-- 等游戏全局可以直接用（Portable Cellar 等大量 mod 的标准写法）。
GLOBAL.setmetatable(env, { __index = function(_, k) return GLOBAL.rawget(GLOBAL, k) end })

local MODNAME = "OmniDontStarveMod"
local VERSION = "0.3.1"

-- ---- 自制预制物：随身箱子 ----
PrefabFiles = { "omni_box" }
Assets = { Asset("ANIM", "anim/treasure_chest.zip") }
GLOBAL.STRINGS.NAMES.OMNI_BOX = "随身箱子"
GLOBAL.STRINGS.CHARACTERS.GENERIC.DESCRIBE.OMNI_BOX = "全部家当都在里面。"
GLOBAL.STRINGS.RECIPE_DESC.OMNI_BOX = "196 格 · 食物反鲜不腐 · 每格 999 · 可放进物品栏随身带"

-- 做进建造栏（生存 tab）。免费建造默认开着，材料随便给一个占位。
local box_recipe = Recipe("omni_box", { Ingredient("cutgrass", 1) }, RECIPETABS.SURVIVAL, TECH.NONE)
box_recipe.atlas = "images/inventoryimages.xml"
box_recipe.image = "krampus_sack.tex"

local FEATURES = {
    "unlockchars",   -- 解锁所有人物
    "cheats",        -- 地图全开/行走速度/科技全解锁/锁血下限10/伤害倍率（默认全开，控制台 omni_* 可调）
    "cheatmenu",     -- 上面这些的游戏内菜单（暂停菜单→作弊菜单），手柄 + 键鼠都能用
    "lazyforager",   -- 橙色护符（懒人护符）不掉耐久
    "box_craft",     -- 隔箱合成：合成时也算随身箱子里的材料
    "janitor",       -- 防崩：定时GC + 清远处垃圾 + 左上角内存显示
    "healthinfo",    -- 鼠标指到生物显示血量/攻击
    "foodinfo",      -- 鼠标指到食物显示 饥/血/理智 数值
    "cookstack",     -- 锅里放整叠食材一次做出整叠
    "status",        -- 组合状态栏：徽章精确数字 + 天数/季节/温度
    "boxpages",      -- 多箱翻页：一个 UI 翻遍所有随身箱子
    -- "treeshake_bear",
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
