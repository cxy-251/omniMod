# 通用仅机械臂供货与霸王鹦海豹食谱（supply32）

2026-11-03；ONI U59-744825-SCRPAN；构建 `fix-20261103-supply32`，接续 phosphor31。

后续说明：本页为 supply32 历史交付记录。其中海豹食谱已按用户要求在 [balance33](predator-population-and-hatchery.md) 删除，现为只猎杀成年海豹并保留原版脂肪掉落；通用供货改动继续保留。

## 供货覆盖

旧逻辑只在 EggIncubator、FarmTile、HydroponicFarm、PlanterBox 和 MilkPress 五个配置的 DoPostConfigureComplete 挂载 ArmOnlyFarmSupply，导致其它加工、储存、研究与消费设施根本没有开关。

现在在 Assets.AddBuildingDef 完成时读取已完整配置的 BuildingComplete 预制体：含自身或子对象 Storage、SingleEntityReceptacle 或 ManualDeliveryKG 的建筑统一挂载供货策略。BuildingConfigManager.ConfigurePost 完成后再补一次，接入较晚添加的库存组件。只配置完整预制体，不给施工件、普通墙砖线管或带 ChoreConsumer 的工作者挂载；没有运行期全地图扫描或新增周期更新。

覆盖条件可直接命中碎石机、窖炉、金属精炼器、普通与高级烹饪设施、食物与普通储存设施、小动物喂食器、输送装载器、植物分析仪、研究台、饮水机、地质调谐仪、气压服存放设施及原五类建筑。此为本地配置源码与识别条件的静态核对，未启动游戏逐栋确认。其它同样采用实体库存的新增建筑通过相同入口接入，不再逐栋维护 ID 白名单。

保留 ArmOnlyFarmSupply 的完整类型名、命名空间及 onlyArms 存档字段，已有手工设置继续读取；新接入建筑默认 true。每栋可切换“供货：仅机械臂／允许复制人”。缺少 CopyBuildingSettings 的预制体补上同类型复制入口，已有复制组保持原值。实际输入库在子对象上的建筑，通过所属 BuildingComplete 找到同一策略；复制人、机械臂或其它 ChoreConsumer 自身库存不作为建筑供货目的地处理。

原料和补充物资的 FetchChore 在创建时添加工作者限制，预条件只读取策略布尔值与 consumerState.hasSolidTransferArm，不改变订单筛选、数量、优先级、来源和投放范围。建造 BuildFetch、修理 RepairFetch、送餐照护 DeliverFood 及带个人 Urge 的任务不受限制；吃饭的个人目标库存也不挂策略。普通 FoodFetch（向建筑补充食材和调味品）仍由开关控制。拥抱唱歌、收获、生产操作等非 Fetch 工作保留。

旧的“切换后取消供货”只识别直接 FetchChore，漏掉复制人常用的 FetchAreaChore 批量运输。现在用户切换或复制为仅机械臂时，检查复制人当前单单或批量订单，取消涉及该建筑的人工补货；不在动物状态回调里切换任务。最终 DeliverComplete 再按当前目的地和供货策略核验，防止旧存档在途任务或配置变化后仍完成投递。失效时沿用原版 DeliverFail 释放预约并处理剩余货物，不强行存入、不销毁货物。

该开关只约束实体搬运订单，不替代管道、不改变其它自动转移或共享池的直接存取。机械臂仍需覆盖原版允许的投放点；缺少可用机械臂时需补齐设施或对该建筑允许人工供货。

## 霸王鹦新增海豹食物项

本地翻译确认霸王鹦为 Raptor（Rhex）、海豹为 Seal；成年和幼年霸王鹦都通过 BaseRaptorConfig.StandardDiets 后交给 SetupDiet。原版 EatButcheredPrey 名单包含多种陆生动物，却没有 Seal／SealBaby。

不能只往旧猎物名单添加标签：BaseSealConfig 的屠宰产物为 50 kg Tallow（脂肪），霸王鹦旧食谱只认识 Meat／DinosaurMeat。Diet.AvailableCaloriesInPrey 对 EatButcheredPrey 是按已认识的屠宰产物计热量，直接添加海豹会得到零热量。

本版在 StandardDiets 返回后、Diet 及其索引构造之前添加 Seal／SealBaby 的独立 EatPrey 项，启用 DLC2 内容时生效，已有项不重复追加。采用普通小型猎物两个肉单位的营养作模组平衡：按海豹原版 100 kg 体重折算，每 kg 热量为 RaptorTuning.CALORIES_PER_UNIT_EATEN × 2/100，原版标准值下 100 kg 海豹对应 3,200 kcal。产物换算系数同乘 2/100，故盐水冰每卡产量保持霸王鹦原版值，既不零热量，也不因低单位质量热量放大排泄量。

食谱覆盖成年与幼年霸王鹦，也覆盖成年与幼年海豹；保留其它食物、代谢、胃容量、毛羽生长与盐水冰品类。沿用原版同房间寻找、可达性、扑击、摄食和剩余掉落流程，不远程传送或强制捕杀，不新增动物更新循环；本条不额外开放地面脂肪作为独立食物。

## 交付核验

依据本机程序集静态核对预制体注册顺序、库存组件、FetchChore 构造器、批量运输清理及捕食热量路径；Release 编译及包与安装文件校验。遵照用户要求，不启动或操作游戏，不新增或运行测试；实际供货与捕食效果仍待正常游玩确认。安装前备份旧版四个模组文件、最新稳定原存档、修改前源码与项目文件、日志；原存档和模组启用配置不修改。

交付回执：`fix-20261103-supply32` 已安装，Release 编译 0 警告、0 错误，四个安装文件与安装包内容一致。DLL SHA-256：`8672727f946d970fc147c67be38395c44906a76804b4696b1e3c4dab87f8fd88`。备份目录：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-03-supply32-002159`；配套原存档：`示例殖民地 Cycle 261 (CrashRecovery).sav`。原存档未替换，未启动或操作游戏。
