--[[ 画框种植 / 画框施肥（联机版）。
     （modimport 加载，跑在 mod 环境）

  ★ 前两版都是"按确认键之后才判断要不要摆多个格子"（长按/看某个键还按没按着），
  你反馈的实际操作习惯是反过来的：**还在看候选（准星/放置预览）的时候，就用
  十字键选好这次要摆几个格子，A 键只是确认执行**——跟原版"点一下就种一个"的
  节奏完全对得上，不存在"按下去先种了一个，过一会儿才决定要不要多种"的时间差。

  改成"模式"而不是"手感判定"：
    - 十字键（CONTROL_FOCUS_UP/DOWN/LEFT/RIGHT，准星在瞄地面、没有别的菜单抢
      焦点时就是裸的十字键信号）在你手上/背包里选着能种的植物或肥料时，按一下
      就把"这次要摆的方阵边长"往后循环一档：1(不摆，原版) -> 3 -> 5 -> 7 -> 1 ...
      按一下改一下、用气泡文字报当前档位，不需要按住。
    - A / 鼠标右键 该怎么按还怎么按，直接触发 ACTIONS.DEPLOY / ACTIONS.FERTILIZE；
      我们在这两个函数外面包一层：单次动作**一成功就立刻**（不用等、不用判断
      按键还按没按着）按当前档位把周围格子摆满。

  中心格已经被原版那次单独处理掉了，我们再种一次时 CanDeploy 会因为"已经有东西"
  自动跳过，不会重复种/重复扣东西。档位=1 时 box_offsets 只有中心一个点，天然
  什么都不会多做，等于原版。

  只对"种下去会变成一株植物"的物品生效（用游戏自己的 deployedplant 标签，跟
  deployable.lua 内部判定 itemplanted 事件用的是同一个标签），不会误伤栅栏/地板/
  陷阱这些其它可放置物。

  ★ "十字键在预览状态下具体是哪个 CONTROL_ id"这件事我没法离线验证，猜的是
  CONTROL_FOCUS_*。猜不中的话打开 omni_boxplant_debug(true)，随便按一个方向键，
  日志会把当时按下的所有 control id 打出来，把结果发回来就能一次定位，不用再
  一轮一轮猜。

  开关 / 调参（控制台 ~ 键，或作弊菜单）：
    omni_boxplant(true/false)   总开关（默认开）
    omni_boxplant_cycle()       手动把档位往后循环一档（不想用十字键时用控制台切）
    omni_boxplant_debug(true/false)  调试日志
]]

local G = GLOBAL

local SIZE_PRESETS = { 1, 3, 5, 7 }

------------------------------------------------------------------ 状态（挂在 cheats.lua 建好的 G.OMNIDST.state 上）
local function S()
    G.OMNIDST = G.OMNIDST or {}
    G.OMNIDST.state = G.OMNIDST.state or {}
    local s = G.OMNIDST.state
    if s.boxplant == nil then s.boxplant = true end
    if s.boxplant_size == nil then s.boxplant_size = SIZE_PRESETS[1] end
    if s.boxplant_debug == nil then s.boxplant_debug = false end
    return s
end

local function announce(player, msg)
    if player and player.components and player.components.talker then
        player.components.talker:Say(msg, 2)
    end
    print("[omnidst/boxplant] " .. msg)
end

local function dbg(...)
    if S().boxplant_debug then print("[omnidst/boxplant][debug]", ...) end
end

------------------------------------------------------------------ 手上/背包里当前"相关"的物品
-- 覆盖两种常见情形：拿到光标上的 active item；手柄把焦点停在背包格子上但还
-- 没拿起来（controller cursor item）。
local function get_relevant_item(player)
    -- 客户端（带洞穴时）没有 components.inventory，只有 replica
    local inv = (player.replica and player.replica.inventory) or player.components.inventory
    local item = inv and inv:GetActiveItem()
    if item then return item end
    local pc = player.components.playercontroller
    if pc and pc.GetCursorInventoryObject then
        local it = pc:GetCursorInventoryObject()
        if it then return it end
    end
    return nil
end

-- 用标签判断：带洞穴时客户端这边只有 replica，没有 components，但标签是同步过来的
-- （deployable 组件自带 "deployable" 标签，fertilizer 组件自带 "fertilizer" 标签）。
local function is_plant_item(item)
    return item ~= nil and item:HasTag("deployedplant") and item:HasTag("deployable")
end
local function is_fert_item(item)
    return item ~= nil and item:HasTag("fertilizer")
end

-- 档位是在客户端按十字键改的，真正种/施肥发生在服务器进程（带洞穴时是另一个进程），
-- 所以每次改档位都同步给服务器一份。不带洞穴时客户端就是服务器，直接改。
AddModRPCHandler("omnidst", "boxsize", function(player, n)
    n = G.tonumber(n)
    if n then S().boxplant_size = n end
end)
local function sync_size(n)
    if G.TheWorld and not G.TheWorld.ismastersim then
        G.pcall(SendModRPCToServer, GetModRPC("omnidst", "boxsize"), n)
    end
end

------------------------------------------------------------------ 十字键：循环档位
local MODE_CYCLE_CONTROLS = {
    G.CONTROL_FOCUS_UP, G.CONTROL_FOCUS_DOWN, G.CONTROL_FOCUS_LEFT, G.CONTROL_FOCUS_RIGHT,
}
local function cycle_size()
    local cur = S().boxplant_size
    local idx = 1
    for i, v in ipairs(SIZE_PRESETS) do
        if v == cur then idx = i break end
    end
    local nxt = SIZE_PRESETS[idx % #SIZE_PRESETS + 1]
    S().boxplant_size = nxt
    sync_size(nxt)
    return nxt
end
G.omni_boxplant_cycle = function()
    local n = cycle_size()
    print("[omnidst/boxplant] 档位 -> " .. (n == 1 and "1（不画框，原版单个）" or (n .. "×" .. n)))
    return n
end

AddComponentPostInit("playercontroller", function(PlayerController)
    local _OnControl = PlayerController.OnControl
    function PlayerController:OnControl(control, down)
        local handled = _OnControl(self, control, down)
        if down and S().boxplant then
            local item = get_relevant_item(self.inst)
            local relevant = is_plant_item(item) or is_fert_item(item)
            if S().boxplant_debug and relevant then
                dbg("手上/背包相关物品下按键 control=" .. tostring(control))
            end
            if relevant then
                for _, c in ipairs(MODE_CYCLE_CONTROLS) do
                    if c and control == c then
                        local n = cycle_size()
                        announce(self.inst, n == 1 and "画框种植：关（原版单个）" or ("画框种植：" .. n .. "×" .. n))
                        break
                    end
                end
            end
        end
        return handled
    end
end)

------------------------------------------------------------------ 背包里找"跟这个同名的还有没有"
-- 不认定它一定是 active item —— 手柄十字键直用背包格子那条路径，东西压根没被
-- 拿到手上，一直待在原来的格子里。
local function find_stack_item(inv, prefab)
    local cur = inv:GetActiveItem()
    if cur and cur.prefab == prefab then return cur end
    for _, v in pairs(inv.itemslots or {}) do
        if v and v.prefab == prefab then return v end
    end
    return nil
end

------------------------------------------------------------------ 画框种植
local function box_offsets(n)
    local half = math.floor((n - 1) / 2)
    local list = {}
    for iz = -half, half do
        for ix = -half, half do
            list[#list + 1] = { ix, iz }
        end
    end
    return list
end

local function fill_box_plant(player, prefab, center)
    local inv = player.components.inventory
    local first = find_stack_item(inv, prefab)
    if not (first and first.components.deployable) then return 0 end
    local radius = (first.components.deployable.DeploySpacingRadius and first.components.deployable:DeploySpacingRadius()) or 1
    local step = math.max(radius * 2, 0.75)
    local cx, _, cz = center:Get()
    local placed = 0
    for _, off in ipairs(box_offsets(S().boxplant_size)) do
        local cur = find_stack_item(inv, prefab)
        if cur == nil or cur.components.deployable == nil then break end
        local pt = G.Vector3(cx + off[1] * step, 0, cz + off[2] * step)
        if cur.components.deployable:CanDeploy(pt, nil, player) then
            local obj = inv:RemoveItem(cur)
            if obj and obj.components.deployable then
                if obj.components.deployable:Deploy(pt, player) then
                    placed = placed + 1
                else
                    inv:GiveItem(obj)
                end
            end
        end
    end
    return placed
end

------------------------------------------------------------------ 画框施肥
-- 跟 actions.lua 里 ACTIONS.FERTILIZE.fn 完全同一套判断（crop / grower / pickable /
-- quagmire_fertilizable 四选一），只是目标从"鼠标指的那个"变成"范围内所有够格的"。
local FERT_TAGS = { "notreadyforharvest", "fertile", "infertile", "barren", "fertilizable" }
local FERT_CELL = 2 -- 施肥目标之间没有固定间距，用这个当"一格"的近似尺寸

local function try_fertilize_target(item, doer, target)
    if item == nil or not item:IsValid() or item.components.fertilizer == nil then
        return false
    end
    local applied = false
    if target.components.crop
       and not (target.components.crop:IsReadyForHarvest() or target:HasTag("withered")) then
        applied = target.components.crop:Fertilize(item, doer)
    elseif target.components.grower and target.components.grower:IsEmpty() then
        applied = target.components.grower:Fertilize(item, doer)
    elseif target.components.pickable and target.components.pickable.CanBeFertilized
           and target.components.pickable:CanBeFertilized() then
        applied = target.components.pickable:Fertilize(item, doer)
    elseif target.components.quagmire_fertilizable then
        applied = target.components.quagmire_fertilizable:Fertilize(item, doer)
    end
    if applied and item:IsValid() and item.components.fertilizer then
        item.components.fertilizer:OnApplied(doer, target)
    end
    return applied
end

local function fill_box_fertilize(player, prefab, center)
    local inv = player.components.inventory
    local half = math.floor((S().boxplant_size - 1) / 2)
    local radius = (half + 0.5) * FERT_CELL
    local cx, _, cz = center:Get()
    local ents = G.TheSim:FindEntities(cx, 0, cz, radius, nil, nil, FERT_TAGS)
    table.sort(ents, function(a, b)
        local ax, _, az = a.Transform:GetWorldPosition()
        local bx, _, bz = b.Transform:GetWorldPosition()
        return ((ax - cx) * (ax - cx) + (az - cz) * (az - cz))
             < ((bx - cx) * (bx - cx) + (bz - cz) * (bz - cz))
    end)
    local applied = 0
    for _, target in ipairs(ents) do
        local cur = find_stack_item(inv, prefab)
        if cur == nil or cur.components.fertilizer == nil then break end
        if try_fertilize_target(cur, player, target) then
            applied = applied + 1
        end
    end
    return applied
end

------------------------------------------------------------------ 单次动作一成功，立刻按当前档位展开（不等、不判断按键）
local function expand_now(doer, kind, prefab, center)
    if not S().boxplant then return end
    if S().boxplant_size <= 1 then return end
    if not (doer and doer:IsValid() and doer.components.inventory) then return end
    local ok, err = G.pcall(function()
        if kind == "plant" then
            local placed = fill_box_plant(doer, prefab, center)
            if placed > 0 then announce(doer, string.format("画框种植 +%d", placed)) end
        else
            local applied = fill_box_fertilize(doer, prefab, center)
            if applied > 0 then announce(doer, string.format("画框施肥 +%d", applied)) end
        end
    end)
    if not ok then print("[omnidst/boxplant] !", tostring(err)) end
end

------------------------------------------------------------------ 挂在动作本体上（所有输入路径的汇合点）
local _DeployFn = G.ACTIONS.DEPLOY.fn
G.ACTIONS.DEPLOY.fn = function(act)
    local invobject = act.invobject
    local is_plant = is_plant_item(invobject)
    local prefab = invobject and invobject.prefab
    local doer = act.doer
    local success, reason = _DeployFn(act)
    dbg("DEPLOY", "success=" .. tostring(success), "prefab=" .. tostring(prefab), "is_plant=" .. tostring(is_plant))
    if success and is_plant and doer and G.TheWorld and G.TheWorld.ismastersim then
        local pt = act.GetActionPoint and act:GetActionPoint()
        if pt then expand_now(doer, "plant", prefab, pt) end
    end
    return success, reason
end

local _FertilizeFn = G.ACTIONS.FERTILIZE.fn
G.ACTIONS.FERTILIZE.fn = function(act)
    local invobject = act.invobject
    local is_fert = is_fert_item(invobject)
    local prefab = invobject and invobject.prefab
    local doer = act.doer
    local target = act.target
    local success = _FertilizeFn(act)
    dbg("FERTILIZE", "success=" .. tostring(success), "prefab=" .. tostring(prefab), "is_fert=" .. tostring(is_fert))
    if success and is_fert and doer and G.TheWorld and G.TheWorld.ismastersim then
        local pt = act.GetActionPoint and act:GetActionPoint()
        if not pt and target and target:IsValid() and target.Transform then
            local x, _, z = target.Transform:GetWorldPosition()
            pt = G.Vector3(x, 0, z)
        end
        if pt then expand_now(doer, "fert", prefab, pt) end
    end
    return success
end

------------------------------------------------------------------ 开关 / 调参
G.omni_boxplant = function(on)
    S().boxplant = (on ~= false)
    print("[omnidst/boxplant] 总开关：" .. (S().boxplant and "开" or "关"))
end
G.omni_boxplant_debug = function(on)
    S().boxplant_debug = (on ~= false)
    print("[omnidst/boxplant] 调试日志：" .. (S().boxplant_debug and "开" or "关"))
end

print("[omnidst/boxplant] 已加载（十字键选档位，确认键立刻按档位画框）")
