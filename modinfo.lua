-- OmniDontStarveTogetherMod — 自制联机版《饥荒》mod
-- 对标单机版的 ../omniDontStarveMod。联机版 API 10，客户端/服务器架构。

name = "OmniDontStarveTogetherMod"
description = [[自用作弊 / 沙盒 / 稳定 / QoL 合集（联机版）。功能按需一个个加。
当前：脚手架。]]
author = "xcai"
version = "0.6.2"

forumthread = ""

api_version = 10

dst_compatible          = true
dont_starve_compatible  = false
all_clients_require_mod  = true    -- 改世界/作弊/预制物，服务器和客户端都要装
client_only_mod          = false

icon_atlas = nil
icon = nil

priority = 0

server_filter_tags = {}

configuration_options = {}
