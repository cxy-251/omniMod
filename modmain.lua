--[[ OmniDontStarveTogetherMod — 自制联机版《饥荒》mod（对标单机版 ../omniDontStarveMod）

  ★ 联机版和单机版的关键区别（写功能时注意）：
    - 全局：GLOBAL.TheWorld（不是 GetWorld()）、GLOBAL.ThePlayer / GLOBAL.AllPlayers
      （不是 GetPlayer()）。
    - 主机判定：`GLOBAL.TheWorld.ismastersim` —— 改世界/生物/组件的东西**只在服务器跑**，
      客户端跑会不同步甚至报错。UI 在客户端跑。solo 自建房时 ThePlayer 就是房主，
      直接改也行，但规范写法是 ismastersim 里改。
    - TheNet:GetIsServer() / GetIsClient() / GetIsDedicated()。
    - 客户端要改服务器状态得发 RPC（AddModRPCHandler / SendModRPCToServer），或者
      solo 房里直接 c_ 命令。
    - api_version = 10。mod 环境比单机版宽一些，但游戏全局仍建议 GLOBAL.xxx。
    - 联机版角色**默认全解锁**，不需要 unlockchars。

  功能模块放 scripts/omnidst/<名字>.lua，用 modimport 加载。
]]

GLOBAL.setmetatable(env, { __index = function(_, k) return GLOBAL.rawget(GLOBAL, k) end })

local MODNAME = "OmniDontStarveTogetherMod"
local VERSION = "0.6.2"

-- 随身箱子预制物
PrefabFiles = { "omni_box" }
Assets = { Asset("ANIM", "anim/treasure_chest.zip") }

local FEATURES = {
    "nonet",       -- 断掉 MOTD / 更新检查 / 数据上报
    "unlockall",   -- 角色本就全解锁；技能树全开
    "cheats",      -- 地图全开/移速/免费建造/秒采伐/锁血/伤害/光照/生物血量 —— 服务器侧
    "cheatmenu",   -- 暂停菜单 → 作弊菜单（手柄+键鼠）
    "lazyforager", -- 橙色护符不掉耐久
    "janitor",     -- 防崩：定时 GC + 左上角内存显示
    "targetinfo",  -- 底部信息条：生物血量/攻击 + 食物 饥/血/智/鲜（手柄+鼠标）
    "cookstack",   -- 一锅煮整叠 + 急速烹饪
    "status",      -- 组合状态栏：徽章数字 + 天数/季节/温度
    "box",         -- 随身箱子（配方 + 隔箱合成）
    "boxpages",    -- 多个箱子翻页
    -- "worldgen",    -- 环形世界（modworldgenmain.lua）
}

local pcall = GLOBAL.pcall
for _, name in ipairs(FEATURES) do
    local ok, err = pcall(modimport, "scripts/omnidst/" .. name .. ".lua")
    print(("[%s] feature %s: %s"):format(MODNAME, name, ok and "loaded" or ("FAILED -> " .. tostring(err))))
end

print(("[%s] v%s loaded (%d feature(s))"):format(MODNAME, VERSION, #FEATURES))
