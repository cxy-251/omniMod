# 食物统计失效引用修复（food38）

2026-11-03；ONI U59-744825-SCRPAN；构建 `fix-20261103-food38`，接续 zh37。

## 已确认的错误路径

用户提供的堆栈与本机 Player.log 一致：`KCalTracker.UpdateData` → `WorldResourceAmountTracker<RationTracker>.CountAmount` → `RationTracker.GetItemData` → `Pickupable.GetComponent<Edible>`，在 Unity 的 GetComponentFastPath 抛空引用。该位置说明被统计的 Pickupable 已失效；不是这个堆栈能证明食物总量被删除或食物不够。

原版 WorldInventory 返回内部 HashSet；泛型统计函数不检查失效物品，GetItemData 也直接取 Edible。共享库存旧合并逻辑直接把原版集合复制到 HashSet，未过滤已销毁引用或失效标签，因此原集合中的坏引用能进入共享视图。实体池 Append 虽检查 Unity 存活，但旧的本地部分仍可能带坏引用。日志没有具体物品实例 ID，不能证明这次是哪一个收集、合并、进食或销毁动作留下了引用。

## 处理

共享与本地库存查询出口统一核验 Unity 对象、KPrefabID、销毁标记及当前标签；食物额外核验 PrimaryElement、Edible、FoodInfo 和 FoodID。共享合并从有效项建立新视图，池输出也采用相同验证。没有共享入口时同样过滤坏引用，仅在发现坏项时复制原版集合；不在原版 HashSet 枚举中删除物品，不修改存储实体。全部失效时返回空视图，避免继续引用原始坏集合。关联世界与 CreatePickupablesList 路径继续去重并使用相同核验。

Pickupable.OnCleanUp 前及时释放共享池的归属、标签索引、请求与数量账本，避免等待分批刷新才清除已销毁项。Untrack 加入同一条 Entry 的归属检查，使存储移除和销毁通知连续到达时只扣账一次；已移除的旧 Entry 不会误删后来登记的新 Entry。

具体 RationTracker.GetItemData 入口另做失效检查，坏项返回合法的空字符串 ID 及零数值，以免原版字典对 null ID 再抛异常。有效物品继续执行原版食物 ID、热量和单位计算；异常保护节流记录。没有吞掉整个统计函数的所有异常，没有把热量固定为零，也没有补丁 Mono 可能共享的泛型 CountAmount。

## 核验与交付

本机程序集静态核对原版 Pickupable 清理、WorldInventory 列表、共享合并、RationTracker 和受保护的 ItemData 结构体；核对复制视图、全部失效、重复回收和有效食物通路。Release 编译及安装包／安装文件逐项核对。按用户要求，不启动或操作游戏、不新增或运行测试；无法据此保证所有其它崩溃均已解决，实际运行仍待游玩确认。

安装前备份 zh37 四个文件、最新稳定原存档、修改前源码与日志，原存档及模组启用配置保持不变。交付回执：`fix-20261103-food38` 已安装，Release 编译 0 警告、0 错误，四个安装文件与安装包一致。DLL SHA-256：`43b881b1d7706e24f2b2a7b8ae8dde610250aa4e92055c6834d452917fbc1132`；备份：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-03-food38-235321`；配套原存档：`示例殖民地 Cycle 281 (AutoBackup).sav`。
