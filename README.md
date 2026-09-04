# OmniMod — 缺氧「全能聚合存储」Mod

一个为《缺氧 / Oxygen Not Included》做的聚合存储 + 自动化 Mod。

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
