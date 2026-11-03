# OmniMod — 缺氧「全能聚合存储」Mod

一个为《缺氧 / Oxygen Not Included》做的聚合存储 + 自动化 Mod。

当前开发快照为 `fix-20261103-food38`，对应游戏 `U59-744825-SCRPAN`。包含跨星球实体共享池与存取入口、机械臂供货、自动生产与照料、生态舱、展馆与孵蛋室、均匀横向辐照、按密度调节的猎杀与成功猎杀统计。本轮修复库存查询中的失效物品引用及食物热量统计空引用。

目前作为开发版维护：以本机游戏程序集做静态核对并完成 Release 编译，游戏内长期运行仍由实际游玩确认。源码可用于备份和协作，发行说明应明确标注开发版及对应游戏版本。构建需要自行安装游戏并通过项目的 `GameDir`／`GameManagedDir` 读取官方程序集，仓库不分发这些程序集；默认构建会自动部署本地 Mod，查看产物时可用 `ModDeployDir` 指定临时输出目录。

当前架构、共享规则与本轮审查记录见仓库根目录的 `docs/architecture-review.md`。下方阶段表和目录示例是早期记录，功能与验证状态以该审查文档和当前源码为准。

## 总体目标（分阶段）

| 阶段 | 内容 | 状态 |
|---|---|---|
| 1 | 可建造的「杂物箱」：无限容量、防腐、防挥发、温度隔离 | ✅ 已完成 |
| 2 | 「食物箱」；全星球自动收集（散落的杂物/食物自动进箱） | ✅ 已完成 |
| 3 | 温度归一化 / 极端温度物品处理规则 | 规划中 |
| 4 | 任意位置取用（改造 Fetch 系统，小人/机械臂就地取物） | 规划中（核心难点） |
| 5 | 点击查看分项数量的 UI 完善 | 规划中 |
| 6 | 自动化扩展：植物成熟自动收获、工业机械自动操作等 | 规划中 |

### 阶段二细节

- **食物箱**：仿香草「口粮箱」，2x2，无需供电，只收食物，靠 `Preserve` 修饰实现绝对防腐。
- **自动收集**（`src/Collection/GlobalItemCollector.cs`）：每个箱子挂一个 `GlobalItemCollector`
  组件，实现 `ISim1000ms`，每 2 秒扫描一次"箱子所在星球"的散落物并 `Storage.Store` 吸入。
  - 杂物箱只收**固体**、非食用、非活物/蛋、未被预定、温度在 -100°C ~ 400°C 之间的物品。
  - 食物箱收所有散落的可食用物（未被预定的）。
  - 已知取舍：暂不遵守箱子的资源筛选设置；暂无单箱开关。

## 目录结构

```
OmniMod.csproj              项目文件：目标 net48，引用游戏 DLL，构建后自动部署
mod.yaml / mod_info.yaml    Mod 元信息（标题、版本、兼容的游戏版本）
src/
  ModEntry.cs               Mod 入口（UserMod2.OnLoad → Harmony.PatchAll）
  ModStrings.cs             双语文本工具（按游戏语言自动切换中/英）
  Buildings/
    JunkBoxConfig.cs        杂物箱的建筑定义（IBuildingConfig）
    BuildingRegistration.cs Harmony 补丁：注册文字 + 加进建造菜单
```

## 构建

```powershell
dotnet build OmniMod.csproj -c Debug
```

构建成功后，`OmniMod.dll` + `mod.yaml` + `mod_info.yaml` 会自动拷到：
`%USERPROFILE%\Documents\Klei\OxygenNotIncluded\mods\Local\OmniMod\`

游戏装在非默认路径时：
```powershell
dotnet build OmniMod.csproj -c Debug -p:GameDir="D:\你的路径\OxygenNotIncluded"
```

## 在游戏里测试

1. 启动缺氧 → 主菜单 → Mods。
2. 找到 **OmniMod 全能聚合存储**，勾选启用，按提示重启游戏。
3. 进入任意存档，打开建造菜单 → **基础 / 储物**，应能看到「杂物箱」，就在「智能储物柜」后面。
4. 造一个，往里丢东西；点击它查看内容物。

## 日志

游戏日志位于 `%USERPROFILE%\AppData\LocalLow\Klei\Oxygen Not Included\Player.log`。
搜索 `[OmniMod]` 可看到本 Mod 的加载信息。

## 针对的游戏版本

`744825`（release 分支）。游戏更新后若行为异常，多半是 API 变动，需要重新核对。


实体共享池重构与旧档迁移说明：[entity16 架构说明](../docs/entity-pool-refactor.md)。

植物施肥跨世界缺料与材料出口修复：[material17 说明](../docs/material-supply-fix.md)。

机械臂施肥／碎石机取货任务修复：[fetch18 说明](../docs/fetch-amount-fix.md)。

自动照料空目标崩溃修复：[ranch19 说明](../docs/ranch-target-crash-fix.md)。
