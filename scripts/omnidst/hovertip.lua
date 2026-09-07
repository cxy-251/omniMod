--[[ 给鼠标悬停提示加一块深色底，解决纯描边文字对着环境看不清。
     （modimport 加载，跑在 mod 环境）

  联机版 HoverText 只有 self.text / self.secondarytext 两行描边字、没底。
  这里钩 widgets/hoverer，加一张纯色贴图 tint 成半透明黑放到文字后面，
  在 OnUpdate 里按当前文字尺寸调整。always-on。
]]

local G = GLOBAL

AddClassPostConstruct("widgets/hoverer", function(self)
    local Image = G.require("widgets/image")
    self.omnibg = self:AddChild(Image("images/global.xml", "square.tex"))
    self.omnibg:SetTint(0, 0, 0, 0.62)
    self.omnibg:SetClickable(false)
    self.omnibg:MoveToBack()
    self.omnibg:Hide()

    local _OnUpdate = self.OnUpdate
    self.OnUpdate = function(self, ...)
        _OnUpdate(self, ...)
        if not self.omnibg then return end
        local shown = self.text and self.text.shown and self.str ~= nil and self.str ~= ""
        if not shown then self.omnibg:Hide() return end

        local w, h = self.text:GetRegionSize()
        local sw, sh = 0, 0
        if self.secondarytext and self.secondarytext.shown and self.secondarystr then
            sw, sh = self.secondarytext:GetRegionSize()
        end
        local W = math.max(w or 0, sw or 0) + 26
        local H = (h or 26) + ((sh and sh > 0) and (sh + 6) or 0) + 16
        local ty = self.text:GetPosition().y
        local sy = (self.secondarytext and self.secondarystr) and self.secondarytext:GetPosition().y or ty
        self.omnibg:SetSize(W, H)
        self.omnibg:SetPosition(0, (ty + sy) / 2, 0)
        self.omnibg:Show()
    end
end)

print("[omnidst/hovertip] 悬停提示深色底已加载")
