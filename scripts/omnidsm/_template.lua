--[[ 功能模块模板。复制成 <功能名>.lua，在 modmain.lua 的 FEATURES 里登记。
     由 modimport 加载 —— 直接在文件作用域干活，不要返回表。

  mod 环境里能直接用的裸名：pairs ipairs print math table type string tostring
    Class GLOBAL TUNING + AddSimPostInit / AddPlayerPostInit / AddPrefabPostInit
    / AddComponentPostInit / AddClassPostConstruct / AddGamePostInit ...
  其它一律 GLOBAL.xxx（require / pcall / tonumber / 以及所有游戏运行时全局）。

  约定：所有"运行时改角色"的东西放 AddSimPostInit / AddPlayerPostInit ——
  每次世界加载（含下洞穴、三大世界互跳）都会触发，过场不重置。
]]

local G = GLOBAL

AddSimPostInit(function()
    local player = G.GetPlayer and G.GetPlayer()
    if not player then return end
    -- player.components.locomotor.runspeed = ...
end)

print("[omnidsm/_template] loaded")
