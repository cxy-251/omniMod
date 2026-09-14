-- OmniDontStarveMod — 自制单机《饥荒》mod（对标 ONI 的 omniMod）
-- 单机版 DS mod，不是联机版。API 6。三大 DLC 全兼容。

name = "OmniDontStarveMod"
description = [[自用作弊 / 沙盒 / 稳定 合集。功能按需一个个加。
当前：脚手架，无功能。]]
author = "xcai"
version = "0.0.1"

forumthread = ""

-- 单机 DS mod API（联机版是 10，这里是单机）
api_version = 6

dont_starve_compatible      = true
reign_of_giants_compatible  = true
shipwrecked_compatible      = true
hamlet_compatible           = true
dst_compatible              = false

client_only_mod        = false
all_clients_require_mod = false

icon_atlas = nil
icon = nil

priority = 0

-- 以后每个功能的开关都挂这里（会出现在 主菜单 > Mods > 配置 里）
configuration_options = {}
