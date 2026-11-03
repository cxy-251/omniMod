# 原版计数与可选房间免拥挤（comfort25）

> 后续变更：tuning27 已把免拥挤控制移至陆地／水生梳理站，默认关闭，旧放生点开关停用；下文为 comfort25 历史说明。

2026-11-02；ONI 744825；构建 `fix-20261102-comfort25`。

## 默认行为与撤回

用户报告 count23/supply24 下放生点数量异常变成约 1，要求默认恢复原版，把“房间不够也能养”的效果改为可设置功能。用户补充该功能应使房间内动物不感觉拥挤，并要求所有已覆盖建筑默认仅机械臂供货，新增植物粉碎机。

本次完整撤回活体房间名册清理、容量计数替换、鱼池计数替换和一般动物去重登记：删除 Audit/Prune、CritterCapacity_ActualRoomCount_Patch、CritterCapacity_ActualPondCount_Patch、OvercrowdingMonitor_UniqueRoomEntry_Patch。非共享实体与鱼池登记回归原版。运行时房间名册不写入存档，重启重载后由原版重新建立；不会将数值伪装为 1，也不会截断动物实际数量。只保留共享池／共享门户内蛋与本地空间的隔离及出库恢复，避免已入共享库存的蛋继续造成不可见的“狭窄”。

## 可选房间功能

普通放生点和水生放生点新增 ComfortRanchPolicy，序列化 ignoreCrowding 默认 false。建筑菜单显示当前状态“免拥挤：关／开”，默认关，点击切换。支持保存与复制建筑设置；不自动开启已有建筑。

同一原版 CavityInfo 区域中，任一放生点显式开启即可对该区域生效：OvercrowdingMonitor 仍按原版更新区域并分析真实动物／蛋数量，仅在对齐效果时免除 Overcrowded、FutureOvercrowded、Confined（拥挤、狭窄、封闭），清除对应效果与状态标签，恢复成年动物原版的 Expecting 标记。其它饥饿、温度、存活、导航、空间占用和实际动物数量不改；动物离开区域或所有开启点关闭／拆除后，下次原版更新恢复正常效果。

放生点本身开启后允许超过用户设定数量继续接收动物。仅在 CreatureDeliveryPoint.RebalanceFetches 中将原版 creatureLimit 的一次读取替换为局部调度上限 max(用户设定数量, 真实数量+1)，保留真实 storedCreatureCount、用户设定和 UI 数字，不向全局属性写入临时值。沿用原版物流开关、工作状态、物种筛选和逐单生成流程，避免批量创建大量任务。切换设置时显式重平衡订单，即使数量没变化也能立即生效。

开启点注册表在需要时每帧最多建立一次 CavityInfo 集合，动物查询为集合查找；默认无开启点时直接走原版，不新增逐帧模拟组件、不扫描全图动物。没有对共享泛型状态机打补丁。

## 供货默认与植物粉碎机

ArmOnlyFarmSupply.onlyArms 继续默认 true；免拥挤默认 false，两者是独立设置。已覆盖孵化器、种植砖、水培砖、种植箱，本次新增 MilkPressConfig（本地简中名称“植物粉碎机”，英文 Plant Pulverizer）。共五种建筑通过各自声明的 DoPostConfigureComplete 加入设置组件。

仅限制目的 Storage 属于这些建筑的 FetchChore 接单者。植物粉碎机的 ComplexFabricator 进料存储位于同一建筑对象，因此复用既有供货开关；原有生产流程不改。供货菜单直接显示当前状态“供货：仅机械臂／供货：允许复制人”，默认前者，点击切换；避免把切换动作的名称误读为当前状态。孵化器 WorkChore<EggIncubatorWorkable>/EggSing 拥抱唱歌不受供货限制。

## 启动流程排查

本次已保全当前与上一份日志。17:09:55 启动的进程实际加载 supply24，17:10:17.158 记录 -- MAIN MENU --，17:10:40.526 才加载原存档；因此该次进程有进入主菜单。项目源码没有自动载档调用。原版 MainMenu.OnSpawn 支持 GenericGameSettings.autoResumeGame、scriptedProfile.saveGame 和 KPlayerPrefs 的一次性 AutoResumeSaveFile 标志；后者会在 ResumeGame 时被消费删除。

当前偏好文件未见 AutoResumeSaveFile，已只读核对运行进程为 `/Applications/OxygenNotIncluded.app/Contents/MacOS/Oxygen Not Included`，没有附加指定存档的启动参数；无法由现存日志区分该次是自动续档还是点击继续。没有据此更改配置或强制屏蔽原版流程。新增两条只读事件记录：进入 MainMenu 时记录三个自动续档条件，ResumeGame 时记录简短调用链，用于下次启动明确触发来源。正常启动和选择存档流程均不改动，也不记录存档内容。

## 验证与交付

依据本地程序集静态核对放生点重平衡、拥挤效果更新、五个建筑配置入口和原版主菜单续档流程。Release 编译 0 警告、0 错误，git diff --check、ZIP 内容及安装文件校验通过；核验错误计数补丁与旧泛型 GoTo 拦截已不在新 DLL，新模式与供货组件存在。遵照用户要求不新增／运行测试，不启动或操作游戏。实际 UI、免拥挤效果与启动触发来源由用户重启后正常使用确认。

安装前备份 supply24 的四个文件、最新原存档、修改前源码和两份本轮日志。原存档、模组启用设置及游戏启动设置均不修改。

交付：comfort25 已安装。备份目录：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-02-comfort25-173445`；DLL SHA-256：`e4990e8d625d1ff2144dfc40cef7df72e00f9899da82b7258e797e0c336aa840`。默认免拥挤关闭、供货仅机械臂；实际运行效果待用户重启后确认。
