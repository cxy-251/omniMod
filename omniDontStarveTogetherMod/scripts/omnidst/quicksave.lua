--[[ 手动保存（不用退出）。（modimport 加载，跑在 mod 环境）

  联机版暂停菜单只有"保存并退出"。游戏自带控制台指令 c_save() 就是"只存盘"：
  服务器上推 ms_save 事件；带洞穴时客户端不是服务器，它自己会 c_remote 转发过去。
  这里包一层 G.omni_save()，作弊菜单里加「保存进度」。
]]

local G = GLOBAL

G.omni_save = function()
    if G.c_save then
        G.c_save()
        if G.ThePlayer and G.ThePlayer.components.talker then
            G.ThePlayer.components.talker:Say("保存中…", 2)
        end
        print("[omnidst/quicksave] 已触发保存")
    else
        print("[omnidst/quicksave] 没有 c_save，保存失败")
    end
end

print("[omnidst/quicksave] 已加载（G.omni_save() / 作弊菜单「保存进度」）")
