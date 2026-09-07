--[[ 给鼠标悬停提示（HoverText）加个卡片底框，解决纯描边文字对着环境看不清。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  原版 HoverText 只有描边文字、没底。这里包 widgets/hoverer：
  加一张饥荒设置界面那种 small_dialog 卡片贴图放到文字后面，
  在 OnUpdate 里按当前文字大小调整（多留边距，让字落在卡片内圈、不压边框）。
  always-on。
]]

local G = GLOBAL

AddClassPostConstruct("widgets/hoverer", function(self)
    local Image = G.require("widgets/image")

    -- 用饥荒设置界面那种卡片贴图当底框
    self.omnibg = self:AddChild(Image("images/globalpanels.xml", "small_dialog.tex"))
    self.omnibg:SetClickable(false)
    self.omnibg:MoveToBack()

    local _OnUpdate = self.OnUpdate
    self.OnUpdate = function(self, ...)
        _OnUpdate(self, ...)
        if not self.omnibg then return end
        local show = self.shown and self.str ~= nil and self.text ~= nil
        if not show then
            self.omnibg:Hide()
            return
        end
        local w, h = self.text:GetRegionSize()
        local sw, sh = 0, 0
        if self.secondarystr and self.secondarytext then
            sw, sh = self.secondarytext:GetRegionSize()
        end
        -- small_dialog 有装饰边框，多留边距让字落在内圈
        local W = math.max(w or 0, sw or 0) + 90
        local H = (h or 30) + (self.secondarystr and ((sh or 30) + 8) or 0) + 60
        local ty = self.text:GetPosition().y
        local sy = (self.secondarystr and self.secondarytext) and self.secondarytext:GetPosition().y or ty
        self.omnibg:SetSize(W, H)
        self.omnibg:SetPosition(0, (ty + sy) / 2, 0)
        self.omnibg:Show()
    end
end)

print("[omnidsm/hovertip] 悬停提示底框已加载")
