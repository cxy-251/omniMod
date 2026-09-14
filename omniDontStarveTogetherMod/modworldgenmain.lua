--[[ OmniDontStarveTogetherMod — 世界生成改动（对标单机版 ../omniDontStarveMod/modworldgenmain.lua）

  森林(SURVIVAL_TOGETHER)：
    · branching = never  必需任务连成一条链
    · islands   = never  任何地区都不隔成孤岛，全部陆连（island_percent=0 → 全部 LockGraph 真连通）
    · loop      = always  首尾闭合
    · 覆写 Story:SeperateStoryByBlanks —— 原版闭环是塞一个不可通行的 blank 把出生点和终点
      隔开（"靠近但不连通"）；改成直接 LockGraph 把两头接上，能绕圈走回出生地。
    · default / cave_default taskset 的 numoptionaltasks 拉满 —— 一张图里所有可选内容都生成。

  洞穴(DST_CAVE / DST_CAVE_PLUS)：branching = never，少拐弯。
  ★ 只对**新建的世界**生效。
]]

GLOBAL.setmetatable(env, { __index = function(_, k) return GLOBAL.rawget(GLOBAL, k) end })

local function set_overrides(level, kv)
    level.overrides = level.overrides or {}
    for k, v in pairs(kv) do level.overrides[k] = v end
end

local function ring_world(level)
    set_overrides(level, { branching = "never", islands = "never", loop = "always" })
    print("[omnidst worldgen] " .. tostring(level.id) .. " -> 环形连通 + 内容全开")
end
AddLevelPreInit("SURVIVAL_TOGETHER", ring_world)
AddLevelPreInit("DST_CAVE", ring_world)
AddLevelPreInit("DST_CAVE_PLUS", ring_world)

-- 所有可选任务/内容都生成
local function all_content(taskset)
    if taskset and taskset.optionaltasks then
        taskset.numoptionaltasks = #taskset.optionaltasks
        print("[omnidst worldgen] taskset 内容全开 -> " .. tostring(taskset.numoptionaltasks) .. " 个可选任务")
    end
end
AddTaskSetPreInit("default", all_content)
AddTaskSetPreInit("cave_default", all_content)

------------------------------------------------------------------ 真闭环
GLOBAL.require("map/storygen")

if GLOBAL.Story and GLOBAL.Story.SeperateStoryByBlanks then
    local Story = GLOBAL.Story
    -- 覆写：不再塞不可通行 blank，直接 LockGraph 成真通道。
    -- 在我们的 override 下（branching=never + islands=never），这个函数只会被
    -- 首尾闭环那一处调用，所以无条件改写是安全的。
    function Story:SeperateStoryByBlanks(startnode, endnode)
        if startnode and endnode and self.rootNode then
            local sid = (startnode.data and startnode.data.task) or startnode.id or "s"
            local eid = (endnode.data and endnode.data.task) or endnode.id or "e"
            self.rootNode:LockGraph(tostring(sid) .. "->" .. tostring(eid) .. "_omniloop",
                startnode, endnode, { type = "none", key = GLOBAL.KEYS.NONE, node = nil })
            print("[omnidst worldgen] 环形闭合 " .. tostring(sid) .. " <-> " .. tostring(eid))
        end
    end
    print("[omnidst worldgen] Story:SeperateStoryByBlanks 已覆写（真闭环）")
end

print("[omnidst worldgen] modworldgenmain 已加载")
