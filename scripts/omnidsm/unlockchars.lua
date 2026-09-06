--[[ 解锁所有人物。（由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  人物选择界面靠 PlayerProfile:IsCharacterUnlocked(name) 判定亮/黑剪影。
  新档只有 Wilson。PlayerProfile 是全局 class，gamelogic.lua 还会重新
  `Profile = PlayerProfile()`，所以覆盖 class 方法最稳。
  DLC 人物能否出现在列表里由 DLC 是否"已装/已启用"决定 —— gbe_fork `unlock_all=1`
  已让三个 DLC 全部认成已装。
]]

local PlayerProfile = GLOBAL.PlayerProfile
if not PlayerProfile then
    print("[omnidsm/unlockchars] GLOBAL.PlayerProfile 不存在，跳过")
    return
end

PlayerProfile.IsCharacterUnlocked = function(self, character)
    return true
end

if GLOBAL.Profile then
    GLOBAL.Profile.IsCharacterUnlocked = PlayerProfile.IsCharacterUnlocked
end

print("[omnidsm/unlockchars] 所有人物已解锁")
