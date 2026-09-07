--[[ 角色全开 + 技能树全开。（modimport 加载，跑在 mod 环境）

  ★ 角色：联机版**默认就是全解锁**的（playerprofile.lua 里根本没有
    IsCharacterUnlocked 这种判定）。人物选择界面直接就能选所有人，不用改。

  ★ 技能树：这是联机版 2023 年加的东西 —— 每个角色一棵「技能树」，靠活着的天数 /
    做角色专属的事 攒技能点，花点数点被动强化。离线时它拉不到线上进度（启动日志里那句
    "Skill tree will be cleared" 就是这个）。这里的做法：
      1. skip_validation = true —— 绕过「点数够不够 / 前置技能」校验
      2. 把技能经验灌满
      3. 把该角色技能树里所有真实技能（有 rpc_id 的）全部激活
    在服务器侧（ismastersim）做；每次角色实体生成 / 过图后重跑。
]]

local G = GLOBAL

local function unlock_skilltree(inst)
    if not (G.TheWorld and G.TheWorld.ismastersim) then return end
    if not (inst and inst:IsValid() and inst.prefab) then return end
    if inst._omni_skills_done then return end   -- 每个角色实例只跑一次（每次 ActivateSkill 都写盘）

    local stu = inst.components.skilltreeupdater
    if stu == nil then return end   -- 该角色没有技能树组件

    local ok, defs = G.pcall(G.require, "prefabs/skilltree_defs")
    if not ok or not defs or not defs.SKILLTREE_DEFS then return end
    local tree = defs.SKILLTREE_DEFS[inst.prefab]
    if tree == nil then
        inst._omni_skills_done = true
        print("[omnidst/unlockall] " .. tostring(inst.prefab) .. " 没有技能树，跳过")
        return
    end

    -- 已经点满就别再点了（避免重复写盘）
    local activated = stu.GetActivatedSkills and stu:GetActivatedSkills() or nil
    local total_real = 0
    for _, sd in pairs(tree) do
        if type(sd) == "table" and sd.rpc_id ~= nil then total_real = total_real + 1 end
    end
    local have = 0
    if activated then for _ in pairs(activated) do have = have + 1 end end
    if total_real > 0 and have >= total_real then
        inst._omni_skills_done = true
        return
    end

    -- 1) 关掉校验
    if stu.SetSkipValidation then stu:SetSkipValidation(true) end
    if stu.skilltree then stu.skilltree.skip_validation = true end
    if G.TheSkillTree then G.TheSkillTree.skip_validation = true end

    -- 2) 灌满技能经验
    local maxxp = 0
    for _, v in ipairs(G.TUNING.SKILL_THRESHOLDS or {}) do maxxp = maxxp + v end
    if maxxp > 0 and stu.AddSkillXP then stu:AddSkillXP(maxxp) end

    -- 3) 激活全部真实技能
    local n = 0
    for skillname, sd in pairs(tree) do
        if type(sd) == "table" and sd.rpc_id ~= nil then
            local okk = G.pcall(function() stu:ActivateSkill(skillname) end)
            if okk then n = n + 1 end
        end
    end
    inst._omni_skills_done = true
    print(("[omnidst/unlockall] %s 技能树已全开（%d 个技能）"):format(inst.prefab, n))
end

AddPlayerPostInit(function(inst)
    inst:DoTaskInTime(3, unlock_skilltree)
    inst:DoTaskInTime(8, unlock_skilltree)   -- 兜底：第一次可能 skilltree 还没换成 TheSkillTree（有 done 标记，不会重复干活）
end)

-- 控制台手动补一发
G.omni_skills = function()
    unlock_skilltree(G.ThePlayer)
end

print("[omnidst/unlockall] 已加载（角色本就全解锁；技能树将全开）")
