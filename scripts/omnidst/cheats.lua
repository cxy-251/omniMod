--[[ 一组作弊功能（联机版）。全部**默认打开**。作弊菜单或控制台 omni_* 调整。
     （modimport 加载，跑在 mod 环境）

  ★ 联机版：改世界/组件的东西只在服务器跑（TheWorld.ismastersim）。单人自建房里
    客户端就是服务器，ThePlayer 就是房主，所以直接改即可。

  控制台（~ 键）：
    omni()                查看所有状态
    omni_map(bool)        地图全开             （默认 开）
    omni_speed(n)         行走速度倍率          （默认 2）
    omni_tech(bool)       免费建造（所有东西直接造，不要材料）（默认 开）
    omni_work(bool)       秒砍/秒挖/秒锤/秒挖   （默认 开）
    omni_hp(bool)         生命下限锁 10         （默认 开）
    omni_dmg(n)           伤害倍率              （默认 10）
    omni_light(bool)      身上永久光照          （默认 开）
    omni_hpbar(bool)      鼠标指到生物显示血量  （默认 开）
    omni_sanity()         理智一键回满
    omni_health()         生命一键回满
    omni_hunger()         饱食一键回满
    omni_off() / omni_on()  全关 / 全恢复默认
]]

local G = GLOBAL

local state = { map = true, speed = 2, tech = true, work = true, hp = true, dmg = 10,
                light = true, hpbar = true, janitor = true }
local HP_FLOOR = 10

local function is_server() return G.TheWorld ~= nil and G.TheWorld.ismastersim end
-- ★ 联机版：Host 游戏时 sim 跑在独立的分片服务器进程里，客户端 G.ThePlayer 有、
--   但服务器进程 G.ThePlayer 是 nil。服务器侧要用 AllPlayers。
local function me()
    if G.ThePlayer then return G.ThePlayer end
    local ap = G.AllPlayers
    return ap and ap[1] or nil
end

-- 采集提速用：游戏自带的快速采集 tag（采草/摘果/挖花 -> doshortaction，农作物 -> domediumaction）
local FAST_TAGS = { "fastpicker", "farmplantfastpicker", "quagmire_fasthands" }

----------------------------------------------------------------- 地图全开
-- 地图全开：只在「本次世界会话」里做一次。反复全图 RevealArea 会让小地图纹理
-- 一直重刷 + 频繁写盘（SD 卡上很卡），所以做完就打标记，不再重复。
local _revealed_world = nil
local function apply_map(force, p)
    if not state.map then return end
    p = p or me()
    if not (p and p.player_classified and p.player_classified.MapExplorer) then return end
    local map = G.TheWorld and G.TheWorld.Map
    if not map then return end
    if not force and _revealed_world == G.TheWorld then return end
    _revealed_world = G.TheWorld
    local w, h = map:GetSize()
    local step = 10
    local TGM = G.TileGroupManager
    for tx = 0, w, step do
        for ty = 0, h, step do
            local tile = map:GetTile(tx, ty)
            if not TGM or not TGM:IsInvalidTile(tile) then
                local x, _, z = map:GetTileCenterPoint(tx, ty)
                p.player_classified.MapExplorer:RevealArea(x, 0, z)
            end
        end
    end
    print("[omnidst/cheats] 地图已全开（本次会话一次性）")
end

----------------------------------------------------------------- 玩家属性
local _ap_dbg = 0
local function apply_player(p)
    p = p or me()
    if not (p and p.components) then return end
    if _ap_dbg < 6 then
        _ap_dbg = _ap_dbg + 1
        local c = p.components
        print(("[omnidst/cheats] apply_player #%d: ismastersim=%s  locomotor=%s builder=%s health=%s worker=%s combat=%s  Light=%s")
            :format(_ap_dbg, tostring(G.TheWorld and G.TheWorld.ismastersim),
            tostring(c.locomotor ~= nil), tostring(c.builder ~= nil), tostring(c.health ~= nil),
            tostring(c.worker ~= nil), tostring(c.combat ~= nil), tostring(p.Light ~= nil)))
    end

    -- 行走速度：用外部倍率，干净、不会被每帧重算冲掉
    local lm = p.components.locomotor
    if lm and lm.SetExternalSpeedMultiplier then
        lm:SetExternalSpeedMultiplier(p, "omnidst_speed", state.speed or 1)
        if _ap_dbg <= 6 then
            print(("[omnidst/cheats]   speed: state=%s  lm.externalspeedmultiplier=%s")
                :format(tostring(state.speed), tostring(lm.externalspeedmultiplier)))
        end
    elseif lm then
        lm._omni_base = lm._omni_base or lm.runspeed
        lm.runspeed = lm._omni_base * (state.speed or 1)
    end

    -- 免费建造：赋值会触发 builder 的属性 setter，同步到 replica，制作栏全部点亮
    local b = p.components.builder
    if b then
        if b.freebuildmode ~= (state.tech and true or false) then
            b.freebuildmode = state.tech and true or false
        end
        if b.EvaluateTechTrees then b:EvaluateTechTrees() end
        p:PushEvent("techlevelchange")
    end

    -- 生命下限
    local h = p.components.health
    if h and h.SetMinHealth then h:SetMinHealth(state.hp and HP_FLOOR or 0) end

    -- 秒砍伐：把玩家 worker 各工作效率拉满
    local wk = p.components.worker
    if wk and wk.SetAction and G.ACTIONS then
        local eff = state.work and 999 or nil
        for _, a in ipairs({ G.ACTIONS.CHOP, G.ACTIONS.MINE, G.ACTIONS.HAMMER, G.ACTIONS.DIG }) do
            if a then wk:SetAction(a, eff or 1) end
        end
    end

    -- 采集提速：挂 / 摘 游戏自带的快速采集 tag（不打断动作，安全）
    for _, tag in ipairs(FAST_TAGS) do
        if state.work then
            if not p:HasTag(tag) then p:AddTag(tag) end
        else
            if p:HasTag(tag) then p:RemoveTag(tag) end
        end
    end

    -- 身上永久光照（本地视觉，单人房同进程直接加）
    if not p.Light and p.entity and p.entity.AddLight then p.entity:AddLight() end
    if p.Light then
        p.Light:SetFalloff(0.6)
        p.Light:SetIntensity(0.75)
        p.Light:SetRadius(state.light and 6 or 0)
        p.Light:SetColour(1, 1, 1)
        p.Light:Enable(state.light and true or false)
    end
end

----------------------------------------------------------------- 伤害倍率
AddComponentPostInit("combat", function(Combat)
    local _Calc = Combat.CalcDamage
    function Combat:CalcDamage(target, weapon, multiplier)
        local d = _Calc(self, target, weapon, multiplier)
        if state.dmg and state.dmg ~= 1 and d and d > 0
           and self.inst ~= nil and self.inst:HasTag("player") then
            d = d * state.dmg
        end
        return d
    end
end)

----------------------------------------------------------------- 秒砍伐 / 秒挖矿 / 秒锤 / 秒挖
-- 联机版里 CHOP/MINE/HAMMER/DIG 走 actions.lua 的 DoToolWork -> 直接调
-- Workable:WorkedBy_Internal（跳过 WorkedBy）。所以要包 **类** 上的 WorkedBy_Internal。
local _wdbg = 0
do
    local ok, WK = G.pcall(G.require, "components/workable")
    if ok and WK and WK.WorkedBy_Internal then
        local _wbi = WK.WorkedBy_Internal
        function WK:WorkedBy_Internal(worker, numworks)
            if state.work and worker ~= nil and worker.components and worker.components.inventory
               and worker:HasTag("player") and (self.workleft or 0) > 0 then
                numworks = self.workleft
                if _wdbg < 5 then
                    _wdbg = _wdbg + 1
                    print("[omnidst/cheats] 秒砍伐生效 -> " .. tostring(self.inst and self.inst.prefab))
                end
            end
            return _wbi(self, worker, numworks)
        end
        print("[omnidst/cheats] Workable:WorkedBy_Internal 已包裹")
    end
end

-- （采集提速改用 FAST_TAGS，见文件顶部 + apply_player。不动 SGwilson 计时，
--   那会打断 doshortaction 第 6 帧的 PerformBufferedAction，导致花/胡萝卜采集不了。）

-- 生物血量 / 食物数值的悬停显示挪到独立模块 targetinfo.lua ——
-- 联机版 widgets/hoverer 是「鼠标专用」（手柄下整个 widget 被 Hide），
-- Steam Deck 手柄玩必须读 playercontroller.controller_target 才行。

----------------------------------------------------------------- 应用 / 控制台
local function apply_all()
    apply_map(true)
    apply_player()
end

local function fmt(v)
    if type(v) == "boolean" then return v and "开" or "关" end
    return tostring(v)
end

-- ★ 联机版 Host：世界跑在独立分片服务器进程里。作弊菜单/控制台在**客户端**跑，
--   直接改客户端的 state 没用（服务器那份 state 不变）。所以客户端调用时用
--   SendRemoteExecute 把命令发到服务器执行（Host 时你就是管理员）。
--   hpbar / janitor 是纯客户端/HUD 的，不用转发。
local CLIENT_ONLY = { omni_hpbar = true, omni_janitor = true }
local function remote(cmd)
    if G.TheWorld and not G.TheWorld.ismastersim and G.TheNet and G.TheNet.SendRemoteExecute then
        G.TheNet:SendRemoteExecute(cmd)
        return true
    end
    return false
end

G.omni = function()
    print(string.format(
        "[omni] 地图=%s 速度x%s 免费建造=%s 秒砍伐=%s 锁血(>=%d)=%s 伤害x%s 光照=%s 血量显示=%s 防崩=%s",
        fmt(state.map), fmt(state.speed), fmt(state.tech), fmt(state.work), HP_FLOOR, fmt(state.hp),
        fmt(state.dmg), fmt(state.light), fmt(state.hpbar), fmt(state.janitor)))
end
G.omni_map     = function(on) if remote("omni_map("..tostring(on)..")") then return end; state.map = (on ~= false); apply_map(true); G.omni() end
G.omni_speed   = function(n)  if remote("omni_speed("..tostring(G.tonumber(n) or 1)..")") then return end; state.speed = G.tonumber(n) or 1; apply_player(); G.omni() end
G.omni_tech    = function(on) if remote("omni_tech("..tostring(on)..")") then return end; state.tech = (on ~= false); apply_player(); G.omni() end
G.omni_work    = function(on) if remote("omni_work("..tostring(on)..")") then return end; state.work = (on ~= false); apply_player(); G.omni() end
G.omni_hp      = function(on) if remote("omni_hp("..tostring(on)..")") then return end; state.hp = (on ~= false); apply_player(); G.omni() end
G.omni_dmg     = function(n)  if remote("omni_dmg("..tostring(G.tonumber(n) or 1)..")") then return end; state.dmg = G.tonumber(n) or 1; G.omni() end
G.omni_light   = function(on) if remote("omni_light("..tostring(on)..")") then return end; state.light = (on ~= false); apply_player(); G.omni() end
G.omni_hpbar   = function(on) state.hpbar = (on ~= false); G.omni() end
G.omni_janitor = function(on) state.janitor = (on ~= false); G.omni() end
local function fill(comp)
    local p = me()
    if p and p.components[comp] and p.components[comp].SetPercent then p.components[comp]:SetPercent(1) end
end
G.omni_sanity = function() if remote("omni_sanity()") then return end; fill("sanity"); print("[omni] 理智回满") end
G.omni_health = function() if remote("omni_health()") then return end; fill("health"); print("[omni] 生命回满") end
G.omni_hunger = function() if remote("omni_hunger()") then return end; fill("hunger"); print("[omni] 饱食回满") end
G.omni_off = function()
    if remote("omni_off()") then return end
    state.map, state.speed, state.tech, state.work, state.hp, state.dmg = false, 1, false, false, false, 1
    state.light, state.hpbar, state.janitor = false, false, false
    apply_player(); G.omni()
end
G.omni_on = function()
    if remote("omni_on()") then return end
    state.map, state.speed, state.tech, state.work, state.hp, state.dmg = true, 2, true, true, true, 10
    state.light, state.hpbar, state.janitor = true, true, true
    apply_all(); G.omni()
end

----------------------------------------------------------------- 每次世界/角色加载后重套
-- 全部用 pcall 包一层：万一以后哪里写错了，也不会每帧刷一屏 Lua 报错把日志写爆盘。
local function safe(f, ...) local ok, e = G.pcall(f, ...); if not ok then print("[omnidst/cheats] !", tostring(e)) end end

-- 新建服务器 / 首次生成世界时，玩家实体常常先生成一个「临时的」再被销毁重建，
-- 一次性 DoTaskInTime 会落在废弃实体上。所以改成**持续每 3 秒重套**（幂等，成本极低），
-- 加上监听 ms_playerspawn，新世界 / 过图 / 复活 都能覆盖到。
AddSimPostInit(function()
    local w = G.TheWorld
    if not (w and w.DoTaskInTime) then return end
    w:DoTaskInTime(2, function() safe(apply_all) end)
    w:DoPeriodicTask(3, function() safe(apply_player) end)
    if w.ListenForEvent then
        w:ListenForEvent("ms_playerspawn", function(_, data)
            local p = (type(data) == "table" and data.player) or data
            if p and p.DoTaskInTime then
                p:DoTaskInTime(1, function() safe(apply_player, p); safe(apply_map, false, p) end)
            end
        end)
    end
end)
AddPlayerPostInit(function(p)
    if p and p.DoPeriodicTask then
        -- 保险：直接挂在玩家实体上每 3 秒重套（不依赖 AddSimPostInit 是否重触发）
        p:DoPeriodicTask(3, function()
            if p:IsValid() then safe(apply_player, p) end
        end)
    end
    if p and p.DoTaskInTime then
        p:DoTaskInTime(2, function() safe(apply_player, p) end)
        p:DoTaskInTime(6, function() safe(apply_player, p); safe(apply_map, false, p) end)
    end
end)

G.OMNIDST = { state = state, apply = apply_all, HP_FLOOR = HP_FLOOR }

print("[omnidst/cheats] 已加载，默认全开")
