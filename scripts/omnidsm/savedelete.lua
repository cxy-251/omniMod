--[[ 主菜单加一个「删除存档」—— 清掉所有现存存档，方便重开。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  包 MainScreen:MainMenu，每次重建主菜单时都在末尾补一项。
  点击 -> 确认弹窗 -> 循环 SaveGameIndex:DeleteSlot(1..NUM_SAVE_SLOTS)。
]]

local G = GLOBAL

AddClassPostConstruct("screens/mainscreen", function(self)
    local PopupDialogScreen = G.require("screens/popupdialog")

    local function wipe_all()
        local n = G.NUM_SAVE_SLOTS or 10
        for i = 1, n do
            if G.SaveGameIndex and G.SaveGameIndex.DeleteSlot then
                G.SaveGameIndex:DeleteSlot(i, nil, { keepBackup = false })
            end
        end
        print("[omnidsm/savedelete] 已清空全部存档 (1.." .. n .. ")")
    end

    local function ask()
        G.TheFrontEnd:PushScreen(PopupDialogScreen(
            "删除存档",
            "确定要删掉所有现存存档吗？此操作不可撤销。",
            {
                { text = "删除", cb = function()
                    wipe_all()
                    G.TheFrontEnd:PopScreen()
                    if self.MainMenu then self:MainMenu() end
                end },
                { text = "取消", cb = function() G.TheFrontEnd:PopScreen() end },
            }))
    end

    local _MainMenu = self.MainMenu
    self.MainMenu = function(self, ...)
        _MainMenu(self, ...)
        if self.menu and self.menu.AddItem then
            self.menu:AddItem("删除存档", ask)
        end
    end

    -- ctor 里已经调过一次 MainMenu 了，这里重建一次让按钮立刻出现
    if self.MainMenu then self:MainMenu() end
end)

print("[omnidsm/savedelete] 主菜单「删除存档」已加载")
