# 读档崩溃及禁用后的舱内接口报错（load21）

2026-11-02；ONI U59-744825-SCRPAN；构建 `fix-20261102-load21`。

## 两次启动的不同故障

已保全原 `Player-prev.log`（本轮 `Player-crashed.log`，约 104 MB）和原 `Player.log`（本轮 `Player-restart.log`）。第一次进程在 15:36 启动并加载 critter20；读档最早的 Mod 异常是 `SaveGame.OnPrefabInit` 抛出的 `InvalidCastException`，调用栈为 `StateMachine<...RanchedStates...>.GenericInstance.GoTo_Patch1`。同一补丁随后干扰 `Klei.AI.SicknessInstance` 的反序列化及大量其它建筑／物品状态，累计约 35,000 条 ERROR 日志。Steam 初始化时的独立错误早于读档，本轮不把它认作 Mod 读档崩溃起点。

第二次进程 15:39 启动，没有加载 OmniMod；当前 `mods.json` 中它的 `enabledForDlc` 已为空。该进程出现 `RocketInteriorPowerPlugComplete.WireUtilitySemiVirtualNetworkLink.OnSpawn` 和大量 `RocketConduitStorageAccess.OnSpawn` 空引用。原版电力和液体接口会在 OnSpawn 中直接通过 GetMyWorld 查找火箭组件；生态舱世界与兼容处理来自 OmniMod，禁用后无法恢复这些自定义世界及接口。本轮不通过忽略接口异常掩盖缺失的模组。

两次日志加载同一个原存档：`auto_save/示例殖民地 Cycle 248 (CrashRecovery).sav`，日志中实际存档头为第 256 周期、39 人；文件名中的 248 已不代表内容周期。原存档不重写、不替换。

## 根因与修改

ranch19 加入的 `RanchedStates_ValidateTransitionTarget` 通过反射取得的 GoTo 并非 RanchedStates 自己声明的方法，而是共享泛型基类的实现。Mono 在多个状态机类型间共享这段代码，Harmony 动态封装仍使用闭合 RanchedStates 接收者，因此其它状态机在进入 Prefix 之前就发生类型转换失败。即使 Prefix 参数使用 object、内部先做类型判断，也无法隔离封装入口的强制转换。

load21 完整删除该补丁类及挂载入口，不再对游戏共用的泛型状态切换添加拦截。保留照料站专用的延后结算、先退状态再清目标、成对释放过期预约及原版失效通知；保留 critter20 的颚鱼食谱与出雏／到站捕捉修复。本次源码修改范围为 RanchStateLifecycle、构建标识及文档，没有改动原版电力接口流程。

安装时先备份四个旧 Mod 文件、当前模组配置、最新原存档、修改前源码和两份日志。仅把 OmniMod 加回当前 Spaced Out DLC 的启用列表，其它模组的开关、版本与顺序保持原配置。

## 核验与边界

本地 Release 编译 0 警告、0 错误，git diff --check 通过。静态核对新 DLL 已不存在 `RanchedStates_ValidateTransitionTarget`，颚鱼食谱、延后捕捉、延后照料和目标恢复组件仍存在。遵照用户要求不新增或运行测试、不启动或操作游戏。安装后对四个产物、配置和备份逐项校验；实际读档恢复需用户重新启动游戏确认。

交付：load21 已安装，四个文件、ZIP、配置及原存档备份校验通过；OmniMod 的 EXPANSION1_ID 启用已恢复，其它模组设置未变。原存档内容与备份完全一致。备份目录：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-02-load21-160602`；DLL SHA-256：`498f86afa691277f8f94b4aeeb880a1ae7752029c892069fba121e9849ad2019`。
