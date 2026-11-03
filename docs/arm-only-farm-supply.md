# 孵化器和种植建筑仅机械臂供货（supply24）

2026-11-02；ONI 744825；构建 `fix-20261102-supply24`。

## 需求与原因

用户希望机械臂向孵化器送蛋，向种植建筑送种子、肥料和灌溉物资，复制人保留孵化器的拥抱唱歌工作。原版优先级 1 是低优先级而非禁用任务，复制人空闲或工作类别优先级较高时仍会接供货单。

本地程序集核对：孵化器与 PlantablePlot 的实体订单通过 SingleEntityReceptacle.CreateFetchChore 创建 FetchChore；植物的 ManualDeliveryKG 把目的 Storage 指向种植建筑，同样创建 FetchChore。孵化器拥抱唱歌则在 EggIncubator.UpdateChore 中创建独立的 WorkChore<EggIncubatorWorkable>（EggSing）。因此仅限制 FetchChore 的接单者即可分离供货和拥抱。

## 行为

在 EggIncubator、FarmTile、HydroponicFarm、PlanterBox 的完整预制体配置入口加入 ArmOnlyFarmSupply，默认 onlyArms=true。已有存档中这些建筑加载时也采用默认值，无须重建；新设置按建筑序列化，可随建筑设置复制。

仅对这些建筑自身 Storage 的 FetchChore 追加供货资格：onlyArms 开启时，consumerState.hasSolidTransferArm 才可接单。任务本身保留，机械臂照常参与原版调度、数量、范围和共享材料取货流程。条件排序在物品查找之前执行，避免被禁止的人工候选触发共享池临时出库。判定只读取设置布尔值和接单者已有标记，无逐帧扫描。

建筑操作菜单在仅机械臂模式显示“允许复制人供货”，点击后恢复原版人工与机械臂共同供货；允许人工时显示“仅机械臂供货”。切换回仅机械臂或复制该设置时，只撤销正在向该建筑执行 FetchChore 的复制人供货，保留其它任务。扫描复制人仅发生于用户切换／复制设置，不新增模拟定时器。

不增加原版 Automatable 组件，避免连带屏蔽 WorkChore。EggSing、拥抱唱歌、收获和其它工作不进入新资格条件；管道供液照常。不会根据附近机械臂状态自动改回人工，有需要可逐建筑切换按钮。没有修改其它机器、储物箱、动物喂食器的供货策略或原建筑优先级。

保留 count23 的房间计数修复、fish22 的鱼苗自动打包、颚鱼食谱与 load21 的读档修复。新挂载仅为四个明确声明的非泛型建筑配置方法，以及 FetchChore 的唯一明确构造函数，不拦截共享 GenericInstance.GoTo。

## 验证与交付

依据本地游戏程序集静态核对供货目的地、构造函数签名、资格排序、拥抱唱歌的独立工作类型与操作菜单接口。Release 编译 0 警告、0 错误；git diff --check 和安装包内容核对通过。遵照用户要求不新增或运行测试、不启动或操作游戏。实际机械臂接单和复制人拥抱由用户重启后正常游玩确认。

安装前备份 count23 的四个文件、最新原存档及修改前源码；安装后逐项核对。原存档和模组启用设置不改。运行中的游戏需退出并重启后加载新版。

交付：supply24 已安装，四个安装文件、上一版及原存档备份校验通过；备份目录：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-02-supply24-170700`；DLL SHA-256：`7f3d3bfc4079704fdaf736655d3a171c7db7234aa55d5ec85fb30e8b3e5aad28`。
