--[[ 功能模块模板。复制成 <功能名>.lua，在 modmain.lua 的 FEATURES 里登记。

  关键约定：所有"运行时改角色"的东西都要在 AddSimPostInit 里做 —— 每次世界加载
  （含下洞穴 / 三大世界互跳）都会触发，这样后台指令那种"过场就重置"的问题不会出现。
]]

local M = {}

function M.init(G)
    -- G 就是 GLOBAL（游戏全局表）

    -- 例：每次世界/洞穴加载后重新套用（不会被过场重置）
    G.AddSimPostInit(function()
        local player = G.GetPlayer and G.GetPlayer()
        if not player then return end
        -- player.components.locomotor.runspeed = ...
        -- player.components.health:SetInvincible(true)
    end)

    -- 例：读配置（modinfo.lua 的 configuration_options 里定义）
    -- local val = G.GetModConfigData("some_option")
end

return M
