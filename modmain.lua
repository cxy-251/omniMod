--[[ OmniDontStarveMod — 自制单机《饥荒》mod（对标 ONI 的 omniMod）

  现在是空壳，没有任何功能。功能按需一个个加：
  每个功能一个模块放 scripts/omnidsm/<名字>.lua，导出一个 .init() ，在下面 FEATURES 里登记。

  约定见 README.md / HANDOFF.md。
]]

local MODNAME = "OmniDontStarveMod"
local VERSION = "0.0.1"

GLOBAL = GLOBAL or _G

-- 功能模块（做一个开一个）：
local FEATURES = {
    "unlockchars",       -- 解锁所有人物
    "cheats",            -- 后台指令可控：地图全开/行走速度/科技全解锁/锁血下限10/伤害倍率（默认全开）
    -- "treeshake_bear", -- 召唤熊獾利用其践踏震树掉落物，不砸自家建筑
    -- "autopickup",     -- 身边一圈掉落物自动进包，无耐久损耗（可只宝石+矿物）
    -- "janitor",        -- 定时 collectgarbage + 清远处掉落物/尸体/残渣 + 屏幕内存MB提示
}

for _, name in ipairs(FEATURES) do
    local ok, mod = pcall(require, "omnidsm/" .. name)
    if ok and type(mod) == "table" and mod.init then
        mod.init(GLOBAL)
        print(("[%s] feature loaded: %s"):format(MODNAME, name))
    else
        print(("[%s] feature FAILED: %s (%s)"):format(MODNAME, name, tostring(mod)))
    end
end

print(("[%s] v%s loaded (%d feature(s))"):format(MODNAME, VERSION, #FEATURES))
