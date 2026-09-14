--[[ 一组作弊功能。全部**默认打开**。可用作弊菜单（cheatmenu 模块）或控制台指令调整。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  控制台（` 键，settings.ini 已开 ENABLECONSOLE）：
    omni()                 查看所有状态
    omni_map(true/false)   地图全开        （默认 开）
    omni_speed(n)          行走速度倍率     （默认 2；omni_speed(1) 恢复正常）
    omni_tech(true/false)  免费建造（所有东西直接造，不要材料）  （默认 开）
    omni_work(true/false)  秒砍伐/秒挖矿/秒锤/秒挖 + 采集不出动作（一下完成）  （默认 开）
    omni_hp(true/false)    生命下限锁 10    （默认 开；能掉血但不会低于 10）
    omni_dmg(n)            伤害倍率         （默认 10；omni_dmg(1) 恢复正常）
    omni_light(true/false) 身上永久光照     （默认 开）
    omni_hpbar(true/false) 鼠标指到生物显示血量/攻击  （默认 开）
    omni_sanity()          理智一键回满
    omni_off() / omni_on() 全关 / 全恢复默认

  随身箱子在 建造栏 → 生存 里造（不是控制台）。

  所有改动在 AddSimPostInit / AddPlayerPostInit 里重新套用 —— 下洞穴、进迷宫、
  三大世界互跳之后都会自动生效。
]]

local G = GLOBAL

local state = { map = true, speed = 2, tech = true, work = true, hp = true, dmg = 10,
                light = true, hpbar = true, janitor = true }
local HP_FLOOR   = 10
local TECH_BONUS = 10

local function player()
    return G.GetPlayer and G.GetPlayer() or nil
end

local function apply_map()
    if not state.map then return end
    local w = G.GetWorld and G.GetWorld()
    if w and w.minimap and w.minimap.MiniMap then
        w.minimap.MiniMap:ShowArea(0, 0, 0, 10000)
    end
end

local function apply_player()
    local p = player()
    if not p or not p.components then return end

    local lm = p.components.locomotor
    if lm then
        lm._omnidsm_base = lm._omnidsm_base or lm.runspeed
        lm.runspeed = lm._omnidsm_base * (state.speed or 1)
    end

    -- 免费建造：freebuildmode 让 CanBuild/KnowsRecipe 直接返回 true —— 所有配方可造、
    -- 不检查材料。再把三系科技加成拉满 + 刷新配方 UI，保证所有制作栏都显示出来。
    local b = p.components.builder
    if b then
        b.freebuildmode = state.tech and true or false
        local v = state.tech and TECH_BONUS or 0
        b.science_bonus, b.magic_bonus, b.ancient_bonus = v, v, v
        if b.EvaluateTechTrees then b:EvaluateTechTrees() end
        if b.inst then b.inst:PushEvent("unlockrecipe") end
    end

    local h = p.components.health
    if h and h.SetMinHealth then
        h:SetMinHealth(state.hp and HP_FLOOR or 0)
    end

    -- 伤害倍率见下面的 CalcDamage 包装（直接改 combat.damagemultiplier 会被角色/buff 覆盖）

    -- 秒砍伐：把玩家 worker 组件的各工作效率拉满（空手也算），可随开关还原
    local wk = p.components.worker
    if wk and wk.SetAction and G.ACTIONS then
        local eff = state.work and 999 or 0
        for _, a in ipairs({ G.ACTIONS.CHOP, G.ACTIONS.MINE, G.ACTIONS.HAMMER, G.ACTIONS.DIG }) do
            if a then wk:SetAction(a, eff) end
        end
    end

    -- 身上永久光照
    if not p.Light and p.entity and p.entity.AddLight then p.entity:AddLight() end
    if p.Light then
        p.Light:SetFalloff(0.6)
        p.Light:SetIntensity(0.75)
        p.Light:SetRadius(state.light and 6 or 0)
        p.Light:SetColour(1, 1, 1)
        p.Light:Enable(state.light and true or false)
    end
end

-- ---- 伤害倍率：包装 Combat:CalcDamage，玩家出手时把最终伤害 ×state.dmg ----
-- 比改 combat.damagemultiplier 稳 —— 后者会被沃尔夫冈的力量、各种 buff 每帧覆盖掉。
AddComponentPostInit("combat", function(Combat)
    local _Calc = Combat.CalcDamage
    function Combat:CalcDamage(target, weapon, mult)
        local d = _Calc(self, target, weapon, mult)
        if state.dmg and state.dmg ~= 1 and d and d > 0
           and self.inst == (G.GetPlayer and G.GetPlayer()) then
            d = d * state.dmg
        end
        return d
    end
end)

-- 生物血量显示搬到独立模块 healthinfo.lua（要同时改 GetDisplayName 和 BufferedAction:
-- GetActionString 两处才能在手持工具时也显示）。

local function apply_all()
    apply_map()
    apply_player()
end

local function fmt(v)
    if type(v) == "boolean" then return v and "开" or "关" end
    return tostring(v)
end

-- ---- 秒砍伐 / 秒挖矿：玩家对可工作物一击完成 ----
-- 挂钩 workable.WorkedBy，把单次工作量顶到剩余量。靠 state.work 实时开关。
-- 只对"有物品栏的施工者"（单机里基本就是玩家）生效，不动其它生物。
local _work_dbg = 0
AddComponentPostInit("workable", function(Workable)
    local _WorkedBy = Workable.WorkedBy
    function Workable:WorkedBy(worker, numworks)
        if state.work and worker and worker.components and worker.components.inventory
           and (self.workleft or 0) > 0 then
            numworks = self.workleft
            if _work_dbg < 5 then
                _work_dbg = _work_dbg + 1
                print("[omnidsm/cheats] 秒砍伐生效 -> " .. tostring(self.inst and self.inst.prefab))
            end
        end
        return _WorkedBy(self, worker, numworks)
    end
end)

-- ---- 采集不出动作：把 dolongaction（采草/摘果/挖花/收割…）压到几帧完成 ----
-- 靠 state.work 开关：关掉时走原版慢动作。
AddStategraphPostInit("wilson", function(sg)
    local st = sg.states and sg.states["dolongaction"]
    if not st then return end
    local _onenter, _ontimeout = st.onenter, st.ontimeout
    st.onenter = function(inst, timeout)
        if not state.work then return _onenter(inst, timeout) end
        if inst.components.locomotor then inst.components.locomotor:Stop() end
        inst.AnimState:PlayAnimation("pickup")
        inst.sg:SetTimeout(4 * G.FRAMES)
    end
    st.ontimeout = function(inst)
        if not state.work then return _ontimeout(inst) end
        inst:PerformBufferedAction()
        inst.sg:GoToState("idle")
    end
end)

-- ---- 控制台指令（挂全局）----
G.omni = function()
    print(string.format(
        "[omni] 地图=%s 速度x%s 免费建造=%s 秒砍伐=%s 锁血(>=%d)=%s 伤害x%s 光照=%s 血量显示=%s 防崩=%s",
        fmt(state.map), fmt(state.speed), fmt(state.tech), fmt(state.work), HP_FLOOR, fmt(state.hp),
        fmt(state.dmg), fmt(state.light), fmt(state.hpbar), fmt(state.janitor)))
end
G.omni_map   = function(on) state.map   = (on ~= false);      apply_all();    G.omni() end
G.omni_speed = function(n)  state.speed = G.tonumber(n) or 1; apply_player(); G.omni() end
G.omni_tech  = function(on) state.tech  = (on ~= false);      apply_player(); G.omni() end
G.omni_work  = function(on) state.work  = (on ~= false);      G.omni() end
G.omni_hp    = function(on) state.hp    = (on ~= false);      apply_player(); G.omni() end
G.omni_dmg   = function(n)  state.dmg   = G.tonumber(n) or 1; apply_player(); G.omni() end
G.omni_light = function(on) state.light = (on ~= false);     apply_player(); G.omni() end
G.omni_hpbar = function(on) state.hpbar = (on ~= false);                    G.omni() end
G.omni_janitor = function(on) state.janitor = (on ~= false);               G.omni() end
G.omni_sanity = function()
    local p = player()
    if not (p and p.components and p.components.sanity) then print("[omni] 没有玩家") return end
    p.components.sanity:SetPercent(1)
    print("[omni] 理智已回满")
end
G.omni_off = function()
    state.map, state.speed, state.tech, state.work, state.hp, state.dmg = false, 1, false, false, false, 1
    state.light, state.hpbar, state.janitor = false, false, false
    apply_player(); G.omni()
end
G.omni_on = function()
    state.map, state.speed, state.tech, state.work, state.hp, state.dmg = true, 2, true, true, true, 10
    state.light, state.hpbar, state.janitor = true, true, true
    apply_all(); G.omni()
end

-- ---- 每次世界加载 / 角色生成后重新套用 ----
AddSimPostInit(function()
    local w = G.GetWorld and G.GetWorld()
    if w and w.DoTaskInTime then
        w:DoTaskInTime(0.5, apply_all)
        w:DoTaskInTime(2.0, apply_all)
    else
        apply_all()
    end
end)
AddPlayerPostInit(function(p)
    if p and p.DoTaskInTime then p:DoTaskInTime(0, apply_player) end
end)

-- 给 cheatmenu 用
G.OMNIDSM = { state = state, apply = apply_all, HP_FLOOR = HP_FLOOR }

print("[omnidsm/cheats] 已加载，默认全开")
