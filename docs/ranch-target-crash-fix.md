# 照料状态空目标崩溃修复（ranch19）

2026-11-02，ONI 744825，构建 `fix-20261102-ranch19`。

## 本次崩溃证据

用户重启游戏后 `Player.log` 已是新进程的启动日志，真正的异常保留在 `Player-prev.log`；两份日志已在继续排查前复制保全。上一进程加载的是 `fetch18`。

致命异常为石壳哈奇 `HatchHard` 的 `RanchedStates.GoTo(root.ranch.Move.MoveToRanch)`，抛出 `InvalidOperationException: TargetParameter: ranchTarget is null`。调用栈显示在 `EventTransitionData.Register → PushTransition → PushTransitions → PushState → GoTo` 注册目标事件时失败。原版 `PushState` 先注册转移、后执行入站动作，因此只在入站动作里判断空目标不足以覆盖本次崩溃。

容灾保存已成功生成第 248 周期 `示例殖民地 Cycle 248 (CrashRecovery).sav` 及预览。保全副本 SHA-256：`293670d75bbfbace0bab212bc72c3a0aeb538e7e3f4257581880477d11b38a3c`。本轮不重写或替换任何原存档。

同一崩溃日志中还出现了硫在 World 39／40 的机械臂投递成功，以及 World 0 碎石机多次接单、投递成功。这是用户实际游玩产生的取货恢复证据；本轮故障落在动物照料状态，而非仅凭库存显示推断原因。

## 代码中的时序缺口

`MessageCreatureArrived` 在动物进入 `ranch.Ranching` 的动作中触发。旧补丁在这个回调内部立即调用 `RanchCreature`；完成补丁又直接清空 `Monitor.TargetRanchStation` 并调用 `StopSM`。外层动物状态切换仍未结束，重入的完成／退出过程却已清理原版 `ranchTarget`。此外，照料资格和行为判断也曾直接清空仍在运行的目标引用，站点失效通知则被一个范围过大的补丁抑制。这些路径不能保持状态参数、监视器目标和站点队列的一致生命周期。

## 修改

- 陆生站的到站回调只登记待结算对象，后续独立 `ISim200ms` 回调确认站点可用、动物仍为当前对象、已真正进入工作状态后，再执行原版完成回调。水生站保持已有工作计时与产物回调。
- `FindRanchable` 不再直接结算仍处于入站／预约阶段的对象。完成后先停止旧任务，由原版 `root.Exit` 清理目标与队列，取消“先清引用、后退状态”的顺序。
- 照料资格与行为判断只安排延后释放；恢复回调再次核对引用和队列归属，先停止仍在运行的任务，再清除仍属于旧站点的残留。动物已归属新站点时不清除新引用。
- 删除对 `TriggerRanchStationNoLongerAvailable` 的宽泛抑制，恢复原版的失效队列通知。
- 历史方案曾在照料实例继承的 `GoTo` 入口增加校验。**2026-11-02 已确认该入口实际来自共享泛型基类，Harmony 生成的封装会在进入 Prefix 之前错误转换其它状态机实例，导致读档崩溃。`load21` 已完整撤销该补丁；不能将 Prefix 内的类型判断当作泛型共享隔离。** 前述延后结算、先退状态再清目标与失效通知仍保留。详见 `load-crash-fix.md`。

恢复与延后结算组件均不写入存档，不复制、销毁动物，不补发已可能结算的产物。日志限频，正常有效任务保持原流程。

## 验证与边界

按当前游戏程序集核对动物到站、完成、队列退出以及状态转移注册顺序。Release 编译为 0 警告、0 错误，`git diff --check` 通过。遵照用户要求，没有新增／运行测试、没有启动或操作游戏；新版本的防崩效果需要后续用户正常游玩确认。

部署前保全 `fetch18` DLL、PDB、两个 YAML、最新原存档、此次第 248 周期恢复档、崩溃日志及修改前源码；部署后逐文件核验 SHA-256，交付清单记录位置与校验值。已运行的游戏进程要退出并重新启动后才会载入新 DLL。


交付：`ranch19` 已安装，DLL、PDB、两个 YAML 与安装包逐项校验通过，旧版与原存档校验通过。配套回退及崩溃证据目录：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-02-ranch19-110653`。DLL SHA-256：`85650986780083ff66d59083b90d375d8b66f5aa61f2bae149f0e355a33c461a`。第 248 周期应急恢复档已保全；新版本实际运行稳定性尚待用户正常游玩确认。
