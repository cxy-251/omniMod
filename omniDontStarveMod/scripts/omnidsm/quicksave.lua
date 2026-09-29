--[[ 手动保存（不用退出）。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  游戏本来就有存档逻辑（Autosaver 组件，站在安全的地方定时自动存 —— 跟游戏自带
  的控制台指令 c_save() 走的是同一个函数），只是平时没有一个"我现在就要存"的
  入口，暂停菜单只有"保存并退出"。这里复用 Autosaver:DoSave()（会弹出跟自动
  存档一样的"保存中…"提示，不是自己瞎写存盘逻辑），加两个不退出就能触发的口子：

    - 作弊菜单里加一行「保存进度」。
    - 控制台 / 其它脚本可以调 G.omni_save()（等价于游戏自带的 c_save()）。
]]

local G = GLOBAL

G.omni_save = function()
    local p = G.GetPlayer and G.GetPlayer()
    if not (p and p.components and p.components.autosaver) then
        print("[omnidsm/quicksave] 没有玩家或存档组件，保存失败")
        return
    end
    p.components.autosaver:DoSave()
    if p.components.talker then
        p.components.talker:Say("保存中…", 2)
    end
    print("[omnidsm/quicksave] 已触发保存")
end

print("[omnidsm/quicksave] 已加载（G.omni_save() / 作弊菜单「保存进度」）")
