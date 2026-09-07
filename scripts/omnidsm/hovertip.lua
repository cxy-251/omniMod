--[[ 给鼠标悬停提示（HoverText）加个深色底框，纯文字对着环境看不清的问题。
     （由 modmain.lua 的 modimport 加载，运行在 mod 环境里）

  原版 HoverText 只有描边文字、没底。这里包 widgets/hoverer：
  加一个半透明黑底 Image，放到文字后面，在 OnUpdate 里按当前文字大小调整。
  always-on。
]]

local G = GLOBAL

AddClassPostConstruct("widgets/hoverer", function(self)
    local Image = G.require("widgets/image")

    self.omnibg = self:AddChild(Image("images/global.xml", "square.tex"))
    self.omnibg:SetTint(0, 0, 0, 0.7)
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
        local W = math.max(w or 0, sw or 0) + 28
        local H = (h or 30) + (self.secondarystr and ((sh or 30) + 8) or 0) + 14
        local ty = self.text:GetPosition().y
        local sy = (self.secondarystr and self.secondarytext) and self.secondarytext:GetPosition().y or ty
        self.omnibg:SetSize(W, H)
        self.omnibg:SetPosition(0, (ty + sy) / 2, 0)
        self.omnibg:Show()
    end
end)

print("[omnidsm/hovertip] 悬停提示底框已加载")
