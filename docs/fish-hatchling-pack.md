# 鱼苗孵化后自动打包（fish22）

2026-11-02；ONI 744825；构建 `fix-20261102-fish22`。

按用户新要求，将孵化器出雏分为两类：鱼苗自动弹出并保持袋装／可搬运，其它幼体自动弹出并解袋。识别使用 CreatureBrain.species，覆盖帕库鱼物种（含各变种）、颚鱼、ParrotFish 和 PufferFish；这些物种标记均已按本地游戏创建代码核对。不使用通用 Swimmer 标签，避免矿甲龟等水生动物也被当作鱼。

处理仅发生于 EggIncubator.StoreBaby 和读档时仍在孵化器内的非蛋 Occupant。出仓前为鱼苗设置 keepWrangledNextTimeRemovedFromStorage，出仓后 SetWrangled 并保留 Deliverable，通过既有 Baggable 补丁进入袋装动物注册表，供机械臂／搬运流程取货。其它幼体清除临时袋装与可搬运标记，恢复动物绘制层并唤醒大脑；自动换蛋订单保留。

本次不批量捕捉地上已有鱼苗，不修改自然孵化流程；取出点仍需动物实际到站才捕捉。保留 load21 的读档崩溃修复，不重新挂载共享泛型状态机拦截。原存档与模组启用设置不改。

交付采用本地 Release 编译、源码差异检查、产物与备份校验；遵照用户要求不新增或运行测试、不启动或操作游戏。运行中的游戏需退出并重启，重新载入后新孵化的鱼苗按新规则处理。

交付：fish22 已安装；Release 编译 0 警告、0 错误，四个安装文件与 ZIP 内容校验通过。备份目录：`~/Library/Application Support/unity.Klei.Oxygen Not Included/recovery_backups/2026-11-02-fish22-165307`；DLL SHA-256：`57c08c8e081f6815149655e490dc23b7fb82f5089d178e605d3c7ae97424ed95`。本次仅修改出雏规则及构建标识，未修改模组启用配置和原存档。
