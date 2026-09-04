# HANDOFF — 给 Steam Deck 上接手的 Claude

> 这份文档是一次性的交接说明。你（Deck 上的 Claude）读完、把关键信息写进自己的
> memory、并确认构建环境可用之后，**删掉这个文件**。

用户把 Windows 上 `C:\02Programmer\02Proj\claude\` 整个工程拷到了 Deck。
memory 目录不会一起过来，所以这份文档里带了工程现状摘要——请提炼成 memory。

---

## 1. 这个工程是什么

**OmniMod** —— 一个《缺氧 / Oxygen Not Included》的 C#/Harmony mod。用户是首次做游戏 mod，
在学，希望你**一边做一边用中文讲清楚每步在干什么、为什么**（这是他明确要求的工作方式）。
mod 内文字**中英都要写**。

核心愿景：全星系无限聚合仓储 + 免人力自动化。已经实现的（详见 `src/` 和 `README.md`）：

- **杂物箱 / 食物箱**：每星球限一个，建造时阻止第二个。
- **全星系共享杂物池**（`OmniPool`，挂在 `SaveGame` 上、随存档持久化）：散装固体元素
  聚合成纯数据；自动收集全星球散落碎片（严格白名单，绝不碰小人/动物——早期有过"把小人
  收进箱子拆箱殉爆"的事故，`IsLooseInertPickupable` / `IsLivingThing` 是保命逻辑，
  **任何动世界物体的代码都必须用正向白名单，先确认小人/动物是否共用该组件**）。
- **离散物品**（种子/电池/蛋/衣物）：收进"离散物品之家"箱子，星系共享。蛋/衣物默认不勾。
- **按需物化**：`RestockFromPool` 扫描 `GlobalChoreProvider.fetchMap` 的消耗型搬运任务，
  把池里材料变成箱子里的真实碎块。
- **隔空取物**：`RemoteFetchPatch` 让箱内物品对任何搬运者"就在身边"（0 距离）。
  **小人/机器人**：改不了走路（`FetchAreaChore` 的接近子状态忽略移动代价，深层泛型状态机
  没法安全动）——用户已接受小人走到箱子。**机械臂**：`SolidTransferArm.AsyncUpdate`
  postfix 把箱内可传送物品无视距离塞进候选列表，机械臂真·隔空取（含泥土/种子送砖块）。
- **工业机械自动运行**（`AutoMachinePatch`）：`ComplexFabricator.duplicantOperated = false`
  覆盖碎石机/窑/精炼/所有烹饪站/等等；`ResearchCenter.Sim200ms` Prefix 拦截、取消小人任务、
  转化器自转出研究点。
- **农作物自动收获**（`AutoHarvestPatch`）：`Harvestable.SetCanBeHarvested` + `HarvestDesignatable.OnSpawn`。
- **移植进来的 workshop mod**：更大视野、传送管穿墙+背景层、更多空气净化器（6 个：CO2/氯/氧/
  天然气/氢/污氧，复用香草动画、按气体颜色上色）、背景灯、Falling Sand（自动清扫那半）、
  Deselect New Materials。
- 加载入口 `src/ModEntry.cs`：**逐个 `[HarmonyPatch]` 类单独挂 + try/catch**（不用 `PatchAll`，
  否则一个坏补丁拖垮全部）；日志 `[OmniMod] Harmony 补丁完成：成功 X，失败 Y`。

## 2. 当前待确认 / 进行中

- 用户上次报"农作物没自动收获""研究站小人还去操作"——但他测的是**旧 build**（日志里没有
  `[OmniMod] 自动收获/自动研究/机械自动运行` 这些行，因为那些日志是最新一版才加的）。
  最新 build 已部署，让他**完全重启游戏**后看这几行日志。
- 食物箱防腐：`Preserve` 修饰打 `GameTags.Preserved` → `Rottable` 进 `Preserved` 状态、
  不再腐烂。"未冷藏"只是温度显示项，无害。如用户报新鲜度真在掉再加强制补丁。

## 3. 环境搭建（Deck / SteamOS，无 flatpak / pacman）

用户习惯把软件装在 `~/Applications`（AppImage / 手动解压那种）。全部装用户目录、不要 root。

### 3.1 .NET SDK（必须，用来 `dotnet build`；不需要 VS、不需要 Mono）
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel LTS --install-dir ~/Applications/dotnet
```
把这两行加进 `~/.bashrc`（然后 `source ~/.bashrc`）：
```bash
export DOTNET_ROOT="$HOME/Applications/dotnet"
export PATH="$DOTNET_ROOT:$HOME/.dotnet/tools:$PATH"
```
验证：`dotnet --info`

### 3.2 ilspycmd（反编译游戏 DLL 查 API，跨平台）
```bash
dotnet tool install -g ilspycmd
```

### 3.3 缺氧路径（**务必实测确认，别假设**）
优先用**原生 Linux 版**（Steam 里 ONI → 属性 → 兼容性 → 取消勾选强制 Proton）。
可能的位置，挨个查：
```bash
ls ~/.local/share/Steam/steamapps/common/OxygenNotIncluded/
ls /run/media/*/steamapps/common/OxygenNotIncluded/    # SD 卡
```
- 有 `OxygenNotIncluded.x86_64` → 原生 Linux 版（推荐）。
- 只有 `OxygenNotIncluded.exe` → 在跑 Proton；建议切原生，否则 mod 目录在 Proton 前缀里
  （`.../compatdata/457140/pfx/drive_c/users/steamuser/Documents/Klei/...`）。
- 关键子目录：`OxygenNotIncluded_Data/Managed/`（游戏 DLL 都在这）。

### 3.4 本地 mod 目录
`OmniMod.csproj` 已经是跨平台的：非 Windows 默认
`GameDir = ~/.local/share/Steam/steamapps/common/OxygenNotIncluded`，
`ModDeployDir = ~/.config/unity3d/Klei/Oxygen Not Included/mods/Local/OmniMod`。
如果游戏在 SD 卡 / 别处，**改 csproj 里那两个 `Condition="... != 'Windows_NT'"` 的默认值**，
或每次 `dotnet build -p:GameDir="/实际路径"`。

### 3.5 验证
```bash
cd <工程目录>
dotnet build OmniMod.csproj -c Debug
```
成功的话末尾会打印 `[OmniMod] 已部署到: .../mods/Local/OmniMod`。
去那个目录确认有 `OmniMod.dll` `OmniMod.pdb` `mod.yaml` `mod_info.yaml`。

## 4. 开发循环

1. 改 `src/**/*.cs`。
2. `dotnet build OmniMod.csproj -c Debug`（自动部署到 Local mod 目录）。
3. 用户**完全退出游戏再重开**（DLL 不能热重载），在 Mods 里启用 OmniMod。
4. 出问题看 `~/.config/unity3d/Klei/Oxygen Not Included/Player.log`，搜 `[OmniMod]`。
   （你可以直接读这个日志文件帮他诊断。）
5. 查游戏 API：`ilspycmd "<Managed>/Assembly-CSharp.dll" -t 类名`。核心逻辑在
   `Assembly-CSharp.dll`，少数在 `Assembly-CSharp-firstpass.dll`。构建目标游戏版本见
   `KleiVersion.ChangeList`（写这份文档时是 744825）。

## 5. 关键约定

- 目标框架 `net48`（游戏 DLL 是针对 .NET 4.8 编的，net472 会让 MSBuild 丢引用）。
- 引用的游戏 DLL 都 `<Private>false</Private>`（只编译期用，运行时游戏自带）。
- Mod 入口 `KMod.UserMod2`，`mod_info.yaml` `APIVersion: 2`。
- 加建筑：`IBuildingConfig` 会被自动发现；字符串 + 建造菜单入口在
  `src/Buildings/BuildingRegistration.cs`（Harmony 补 `GeneratedBuildings.LoadGeneratedBuildings` 的 Prefix）。

---

**做完 3.5 验证 + 把 1/2/5 节提炼进 memory 后，删掉本文件。**
