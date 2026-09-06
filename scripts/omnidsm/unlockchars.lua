--[[ 解锁所有人物。

  单机饥荒里人物选择界面靠 PlayerProfile:IsCharacterUnlocked(name) 判定亮/灰（黑）。
  没解锁的默认剪影全黑。原版靠攒 XP 一个个解，新档只有 Wilson。

  PlayerProfile 是全局 class（playerprofile.lua 里 `PlayerProfile = Class(...)`），
  gamelogic.lua 还会重新 `Profile = PlayerProfile()`，所以改 class 方法最稳。
  DLC 人物（Warly/Wormwood/Wilba/Wheeler 等）能不能出现在列表里由 DLC 是否
  "已安装/已启用"决定 —— 我们的 gbe_fork `unlock_all=1` 已经让三个 DLC 全部认成
  已安装，所以列表是全的，这里只要把"解锁判定"打开就行。
]]

local M = {}

function M.init(G)
    local PlayerProfile = G.PlayerProfile
    if not PlayerProfile then
        print("[omnidsm/unlockchars] GLOBAL.PlayerProfile 不存在，跳过")
        return
    end

    PlayerProfile.IsCharacterUnlocked = function(self, character)
        return true
    end

    -- 当前已存在的实例（main.lua 早就 `Profile = PlayerProfile()` 过了）也一起覆盖，
    -- 保险，避免个别地方缓存了旧方法。
    if G.Profile then
        G.Profile.IsCharacterUnlocked = PlayerProfile.IsCharacterUnlocked
    end

    print("[omnidsm/unlockchars] 所有人物已解锁")
end

return M
