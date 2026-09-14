--[[ OmniDontStarveMod — 世界生成改动

  巨人国（森林）：保证是一个环形连通世界，外环上有枝杈（可选任务）也没关系。
    · branching = never   必需任务连成一条链
    · islands   = never   任何地区都不隔成孤岛，全部陆连
    · loop      = always  首尾闭合
    · 覆写 Story:SeperateStoryByBlanks —— 原版闭环是把出生点和终点用不可通行的 blank
      隔开（"靠近但不连通"）；改成直接 LockGraph 把两头接上（可通行），能绕圈走回出生地。
    · numoptionaltasks 拉满 —— 一张图里**所有可选任务/内容都生成**，不再随机只挑 4 个。

  洞穴：branching = never，少拐弯。
  海难 / 哈姆雷特：不动（本来就是岛）。

  overrides 是 {key,value} 对列表（map/levels/*.lua）。
  branching: default/most/least/never（storygen.lua:297）；loop: never/default/always。
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
    set_override(level, "islands", "never")
    set_override(level, "loop", "always")
    -- 一张图里所有内容都生成：可选任务全上
    if level.optionaltasks then
        level.numoptionaltasks = #level.optionaltasks
    end
    print("[omniDSM worldgen] 森林 -> 环形连通、内容全开 (" .. tostring(level.id) .. ")")
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
GLOBAL.require("map/storygen")

if GLOBAL.Story and GLOBAL.Story.SeperateStoryByBlanks then
    local Story = GLOBAL.Story
    local _orig = Story.SeperateStoryByBlanks
    function Story:SeperateStoryByBlanks(startnode, endnode)
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
