--[[ 断掉游戏自己发起的联网请求。（modimport 加载，跑在 mod 环境）

  gbe_fork 的 disable_networking 只挡 Steam 那层；游戏引擎自己的 TheSim:QueryServer
  还会去拉：
    - MOTD 公告板文字 + 图片（主菜单右下角，离线时每次 5 秒超时后重试，很烦）
    - 「有新版本」检查
  这里把 MotdManager 整个关掉，并兜底把 QueryServer 里指向 klei 域名的请求直接失败回调，
  不再干等超时。
]]

local G = GLOBAL

-- 1) MOTD：直接禁用（IsEnabled=false 会让 Initialize 既不读缓存也不下载）
local ok, Motd = G.pcall(G.require, "motdmanager")
if ok and Motd then
    Motd.IsEnabled          = function() return false end
    Motd.Initialize         = function() end
    Motd.DownloadMotdInfo   = function() end
    Motd.GetImagesToDownload = function() return {} end
    Motd.LoadCachedImages   = function() end
    print("[omnidst/nonet] MotdManager 已禁用")
end
-- 已经建好的实例（frontend 早于 mod 时）
if G.TheFrontEnd and G.TheFrontEnd.MotdManager then
    local m = G.TheFrontEnd.MotdManager
    m.IsEnabled = function() return false end
    m.DownloadMotdInfo = function() end
    m.isloading_motdinfo = false
end

-- 2) 关掉「有新版本」更新提示（离线也拉不到）
if G.TheFrontEnd then
    G.TheFrontEnd.ShouldShowUpdateAvailable = function() return false end
end

print("[omnidst/nonet] 已加载")
