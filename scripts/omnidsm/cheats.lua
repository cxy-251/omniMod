--[[ 一组后台指令可控的作弊功能。全部**默认打开**，可用控制台指令手动关闭/调整。

  控制台（按 ` 键打开，settings.ini 已开 ENABLECONSOLE）：
    omni()              查看所有状态
    omni_map(true/false)   地图全开        （默认 开）
    omni_speed(n)          行走速度倍率     （默认 2；omni_speed(1) 即恢复正常）
    omni_tech(true/false)  科技全解锁       （默认 开；材料仍需要，只是不用科学机器/随处可造）
    omni_hp(true/false)    生命下限锁 10    （默认 开；能掉血但不会低于 10，不会死）
    omni_dmg(n)            伤害倍率         （默认 3；omni_dmg(1) 即恢复正常）
    omni_off()             全部关掉
    omni_on()              全部恢复默认

  所有改动在 AddSimPostInit / AddPlayerPostInit 里重新套用 —— 下洞穴、进迷宫、
  三大世界互跳之后都会自动生效，不会像后台指令那样过场就重置。
]]

local M = {}

-- 默认全开
local state = {
    map   = true,
    speed = 2,
    tech  = true,
    hp    = true,
    dmg   = 3,
}
local HP_FLOOR = 10
local TECH_BONUS = 10   -- 科技等级实际最高 3~4，给 10 保证全解锁

local G  -- GLOBAL，init 时赋值

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

    -- 行走速度：以"首次见到的 runspeed"为基准乘倍率（兼容非 Wilson 角色）
    local lm = p.components.locomotor
    if lm then
        lm._omnidsm_base = lm._omnidsm_base or lm.runspeed
        lm.runspeed = lm._omnidsm_base * (state.speed or 1)
    end

    -- 科技全解锁：抬高三系科技加成，重新评估科技树（材料照常需要）
    local b = p.components.builder
    if b then
        local v = state.tech and TECH_BONUS or 0
        b.science_bonus, b.magic_bonus, b.ancient_bonus = v, v, v
        if b.EvaluateTechTrees then b:EvaluateTechTrees() end
    end

    -- 生命下限：引擎 SetVal 自带 minhealth 钳制，掉到 10 就触发 "minhealth" 而不是 "death"
    local h = p.components.health
    if h and h.SetMinHealth then
        h:SetMinHealth(state.hp and HP_FLOOR or 0)
    end

    -- 伤害倍率：Combat:CalcDamage 用 self.damagemultiplier
    local c = p.components.combat
    if c then
        c.damagemultiplier = state.dmg or 1
    end
end

local function apply_all()
    apply_map()
    apply_player()
end

local function fmt(v)
    if type(v) == "boolean" then return v and "开" or "关" end
    return tostring(v)
end

function M.init(g)
    G = g

    -- ---- 控制台指令（挂到全局，` 控制台里直接调用）----
    G.omni = function()
        print(string.format(
            "[omni] 地图全开=%s  速度x%s  科技全解锁=%s  锁血(>=%d)=%s  伤害x%s",
            fmt(state.map), fmt(state.speed), fmt(state.tech), HP_FLOOR, fmt(state.hp), fmt(state.dmg)))
    end
    G.omni_map = function(on)   state.map   = (on ~= false); apply_all(); G.omni() end
    G.omni_speed = function(n)  state.speed = tonumber(n) or 1; apply_player(); G.omni() end
    G.omni_tech = function(on)  state.tech  = (on ~= false); apply_player(); G.omni() end
    G.omni_hp = function(on)    state.hp    = (on ~= false); apply_player(); G.omni() end
    G.omni_dmg = function(n)    state.dmg   = tonumber(n) or 1; apply_player(); G.omni() end
    G.omni_off = function()
        state.map, state.speed, state.tech, state.hp, state.dmg = false, 1, false, false, 1
        apply_player(); G.omni()
    end
    G.omni_on = function()
        state.map, state.speed, state.tech, state.hp, state.dmg = true, 2, true, true, 3
        apply_all(); G.omni()
    end

    -- ---- 每次世界加载 / 角色生成后重新套用 ----
    G.AddSimPostInit(function()
        local w = G.GetWorld and G.GetWorld()
        if w and w.DoTaskInTime then
            w:DoTaskInTime(0.5, apply_all)   -- 稍等玩家/小地图实体就位
            w:DoTaskInTime(2.0, apply_all)
        else
            apply_all()
        end
    end)
    G.AddPlayerPostInit(function(p)
        if p and p.DoTaskInTime then p:DoTaskInTime(0, apply_player) end
    end)

    -- 给 cheatmenu 模块用：读状态 + 主动重套
    G.OMNIDSM = {
        state = state,
        apply = apply_all,
        HP_FLOOR = HP_FLOOR,
    }

    print("[omnidsm/cheats] 已加载，默认全开（控制台 omni() 看状态，或用作弊菜单）")
end

return M
