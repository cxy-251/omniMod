--[[ OmniDontStarveMod — 世界生成改动

  - 巨人国（森林）：环形连通世界 —— branching=never（无分支死路）+ loop=always（首尾相连）
  - 洞穴：简化 —— branching=never（少拐弯）
  - 海难 / 哈姆雷特：不动（本来就是岛）

  overrides 是 {key, value} 对的列表（见 map/levels/*.lua）。
  branching 取值：default / most / least / never   （map/storygen.lua）
  loop 取值：never / default / always              （map/forest_map.lua）
]]

GLOBAL.setmetatable(env, { __index = function(_, k) return GLOBAL.rawget(GLOBAL, k) end })

local function set_override(level, key, value)
    level.overrides = level.overrides or {}
    for i = #level.overrides, 1, -1 do
        if level.overrides[i][1] == key then table.remove(level.overrides, i) end
    end
    table.insert(level.overrides, { key, value })
end

local function ring_forest(level)
    set_override(level, "branching", "never")
    set_override(level, "loop", "always")
    print("[omniDSM worldgen] 森林 -> 环形连通世界 (" .. tostring(level.id) .. ")")
end

local function simple_cave(level)
    set_override(level, "branching", "never")
    print("[omniDSM worldgen] 洞穴 -> 简化 (" .. tostring(level.id) .. ")")
end

AddLevelPreInit("SURVIVAL_DEFAULT", ring_forest)
AddLevelPreInit("SURVIVAL_DEFAULT_PLUS", ring_forest)
AddLevelPreInit("CAVE_LEVEL_1", simple_cave)
AddLevelPreInit("CAVE_LEVEL_2", simple_cave)

print("[omniDSM worldgen] modworldgenmain 已加载")
