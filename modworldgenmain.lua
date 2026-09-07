--[[ OmniDontStarveMod — 世界生成改动

  - 巨人国（森林）：一整个完整连通的环，各地区之间直接衔接
      · branching=never  无分支死路 -> 一条链
      · islands=never    island_percent=0 -> 任何地区都不会被隔成孤岛，全部用
        LockGraph 相连（storygen.lua:333 那个分支永不走 blank）
      · loop=always      触发"闭环"逻辑
      · 覆写 Story:SeperateStoryByBlanks —— 原版闭环是把出生点和终点用**不可通行的
        blank 隔开**（所以只是"靠近但不连通"）；这里改成直接 LockGraph 把两头**接上**
        （可通行），于是能一路绕圈走回出生点。
  - 洞穴：branching=never，少拐弯。
  - 海难 / 哈姆雷特：不动（本来就是岛）。

  overrides 是 {key,value} 对列表（map/levels/*.lua）。
  branching: default/most/least/never（map/storygen.lua:297）
  loop: never/default/always（map/forest_map.lua）
]]

GLOBAL.setmetatable(env, { __index = function(_, k) return GLOBAL.rawget(GLOBAL, k) end })

------------------------------------------------------------------ level overrides
local function set_override(level, key, value)
    level.overrides = level.overrides or {}
    for i = #level.overrides, 1, -1 do
        if level.overrides[i][1] == key then table.remove(level.overrides, i) end
    end
    table.insert(level.overrides, { key, value })
end

local function ring_forest(level)
    set_override(level, "branching", "never")   -- 必需任务连成一条链，无分支死路
    set_override(level, "islands", "never")      -- 各地区全部陆连，不隔孤岛
    set_override(level, "loop", "always")        -- 首尾闭合
    -- 去掉可选任务 —— 它们是挂在主链上的"分支地区"。只留必需任务的那条链，
    -- 闭合之后就是一圈地区、没有伸出去的枝杈（力导向布局会把闭合的链排成大致圆形）。
    level.numoptionaltasks = 0
    level.optionaltasks = {}
    print("[omniDSM worldgen] 森林 -> 环形连通、去分支 (" .. tostring(level.id) .. ")")
end

local function simple_cave(level)
    set_override(level, "branching", "never")
    print("[omniDSM worldgen] 洞穴 -> 简化 (" .. tostring(level.id) .. ")")
end

AddLevelPreInit("SURVIVAL_DEFAULT", ring_forest)
AddLevelPreInit("SURVIVAL_DEFAULT_PLUS", ring_forest)
AddLevelPreInit("CAVE_LEVEL_1", simple_cave)
AddLevelPreInit("CAVE_LEVEL_2", simple_cave)

------------------------------------------------------------------ 真闭环
GLOBAL.require("map/storygen")   -- 让 GLOBAL.Story 就位

if GLOBAL.Story and GLOBAL.Story.SeperateStoryByBlanks then
    local Story = GLOBAL.Story
    local _orig = Story.SeperateStoryByBlanks
    function Story:SeperateStoryByBlanks(startnode, endnode)
        -- 只拦"闭环"那次调用（起点是整张图的出生节点）；lock/key 之间的隔断不动
        if self.startNode ~= nil and startnode == self.startNode then
            self.rootNode:LockGraph(
                tostring(startnode.id) .. "->" .. tostring(endnode.id) .. "_omniloop",
                startnode, endnode,
                { type = "none", key = GLOBAL.KEYS.NONE, node = nil })
            print("[omniDSM worldgen] 环形闭合 " .. tostring(startnode.id) .. " <-> " .. tostring(endnode.id))
        else
            return _orig(self, startnode, endnode)
        end
    end
    print("[omniDSM worldgen] Story:SeperateStoryByBlanks 已覆写（真闭环）")
end

print("[omniDSM worldgen] modworldgenmain 已加载")
