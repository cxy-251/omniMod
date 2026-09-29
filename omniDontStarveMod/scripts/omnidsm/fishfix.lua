--[[ 防崩：钓鱼时"钩到的鱼已经失效"导致的原生崩溃。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  背景：日志抓到过两次这个链路：
    Stale Component Reference: GUID xxx, @scripts/components/fishable.lua:81
    pure virtual method called / terminate called without an active exception
  三份 fishable.lua（基础版 + DLC0002 + DLC0003）的 `RemoveFish`/`ReleaseFish`
  都是拿到 fish 就直接摸 `fish.entity:Show()` / `fish.Physics:SetActive(true)`，
  从来没检查过这条鱼是不是还有效。

  ★ 第一版用 `fish:IsValid()`（也就是 `fish.entity:IsValid() and not fish.retired`）
  当判断依据，结果把正常钓上来的鱼也一起拦住了——钓不出东西。没能在不跑游戏的
  情况下查清楚具体是 `retired` 这个 Lua 标记的问题还是别的，这版换成只看
  `fish.entity:IsValid()`（更底层、跳过 `retired` 那层），逻辑也从"整个函数
  跳过"改成"只跳过会崩的那几句摸实体的操作，其它记账正常做"：
    - 实体有效 -> 跟原版一模一样，摸 Show/Physics、返回这条鱼（正常钓到）。
    - 实体失效 -> 不摸 entity/Physics（这是会崩的地方），清掉记账，返回 nil
      （这一竿算没钓到，不会崩，但也没法凭空生一条鱼出来给你——如果真的失效了，
      这条鱼本来就没法要）。

  默认打开调试日志（每次 Remove/ReleaseFish 都打一行"实体有没有效"），方便这次
  再出问题时能直接从日志里看出是哪种情况，而不是又要靠猜。确认稳定之后可以把
  下面 DEBUG 改成 false 关掉刷屏。

  如果这版还是不对劲（钓不出鱼 / 还是崩），直接把 modmain.lua 里 "fishfix" 那行
  改回注释状态就行，跟上次撤销的做法一样。
]]

local G = GLOBAL

local DEBUG = true
local function dbg(...)
    if DEBUG then print("[omnidsm/fishfix][debug]", ...) end
end

local function entity_ok(fish)
    return fish ~= nil and fish.entity ~= nil and fish.entity:IsValid()
end

AddComponentPostInit("fishable", function(Fishable)
    local _RemoveFish = Fishable.RemoveFish
    function Fishable:RemoveFish(fish)
        local was_hooked = fish ~= nil and self.hookedfish and self.hookedfish[fish] == fish
        local ok = entity_ok(fish)
        dbg("RemoveFish", "fish=" .. tostring(fish), "was_hooked=" .. tostring(was_hooked), "entity_ok=" .. tostring(ok))
        if ok then
            return _RemoveFish(self, fish)
        end
        if was_hooked then
            print("[omnidsm/fishfix] RemoveFish：钩到的鱼实体已失效，跳过摸它（防崩），这一竿算没钓到")
            self.hookedfish[fish] = nil
            if not self.respawntask then self:RefreshFish() end
        end
        return nil
    end

    local _ReleaseFish = Fishable.ReleaseFish
    function Fishable:ReleaseFish(fish)
        local was_hooked = fish ~= nil and self.hookedfish and self.hookedfish[fish] == fish
        local ok = entity_ok(fish)
        dbg("ReleaseFish", "fish=" .. tostring(fish), "was_hooked=" .. tostring(was_hooked), "entity_ok=" .. tostring(ok))
        if ok then
            return _ReleaseFish(self, fish)
        end
        if was_hooked then
            print("[omnidsm/fishfix] ReleaseFish：鱼实体已失效，跳过摸它（防崩）")
            self.hookedfish[fish] = nil
        end
    end
end)

print("[omnidsm/fishfix] 已加载（v2：只跳过失效实体那几句，不再整函数拦截）")
