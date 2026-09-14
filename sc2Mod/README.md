# sc2Mod

自制 StarCraft II mod 仓库 —— **只负责做 mod**，不负责拉起游戏。

游戏怎么离线打（burnysc2 组局 + Proton/SLR 垫片）、地图选择面板，都在
`~/Games/omni-deck/`（首页「⭐ 星际争霸2」卡片，`sc2_panel_service.py` +
`sc2_runner.py`）。这里只是被它 `import bake` 调用一下。

## 目录

- `mods/*.SC2Mod` —— 自制 mod 本体（银河编辑器 Extension mod）。见 `mods/README.md`。
- `bake.py` —— mod 依赖表（`MOD_DEPS`）+ UI 展示信息（`MOD_INFO`）+ `bake()`：
  把选中的 mod 焊进某张地图的副本（改 `.SC2Map` 里 `DocumentHeader` 的依赖表）。
- `_bake_inner.py` —— 实际动 MPQ 的部分：ctypes 调 `lib/libstorm.so`（StormLib）
  打开地图、替换/写回 `DocumentHeader`。宿主直接跑，不需要容器。
- `lib/libstorm.so*` —— 编译好的 StormLib（MPQ 读写，地图/mod 文件用这个格式）。
- `lib/libcasc.so` —— 编译好的 CascLib（CASC 读，游戏本体自己的数据用这个格式）。
  两个都在一次性的 distrobox Arch 容器（`sc2bake`）里编的，运行时都不需要容器——
  glibc 够新，直接能在 SteamOS host 上 `dlopen`。
- `casc_probe.py` —— 拿 CascLib 从游戏本体 CASC 包（`~/Games/StarCraft II/SC2Data`）里
  挖真实字段名/数值的小工具，做新 mod 前先用它验证格式，别对着网上零星文档瞎猜：
  `uv run python casc_probe.py find "*abildata*"` 列出匹配路径，
  `uv run python casc_probe.py dump <路径> out.xml` 导出看内容。
  已经用它验证过的关键文件：`mods\liberty.sc2mod\base.sc2data\gamedata\{unitdata,abildata}.xml`
  ——沿用至今的最底层 WoL 基础数据，比 `void.sc2mod`/`voidmulti.sc2mod`（只是增量覆盖）
  更适合当"某个字段原始值是多少"的参考。
- `make_harvest_mod.py` / `make_fastbuild_mod.py` —— 两个可重复用的 mod 生成器
  （倍数采集 / 建造加速），跑一下就出一份 `.SC2Mod`，不用每次手搓 XML。

## 做新 mod 的流程

1. 先搞清楚要改的字段到底叫什么：用 `casc_probe.py` 从游戏本体挖真实 XML 抄格式
   （别信论坛/wiki 里零星贴的例子，容易文档不全或过时——`fastBuild` mod 就因为一开始
   凭文档拼凑漏看了 index 跳号，挖了原始数据才发现改错了字段）。也可以用银河编辑器
   （Windows/Proton 里，或问 omni-deck 那边怎么起编辑器）建 Extension mod 改着玩。
2. `bake.py` 里 `MOD_DEPS` 加一条 `key -> "bnet:<mod名>/0.0/999,file:Mods/<文件名>.SC2Mod"`，
   `MOD_INFO` 加一条展示用的中文名/描述（想跟别的 mod 互斥就加个相同的 `"group"`）。
3. `uv run python bake.py <地图 stem> <mod key>` 本地测试烘焙。
4. omni-deck 那边的 mod 勾选列表会自动读到新 mod（`bake.MOD_INFO`），不用改它的代码。

## 独立测试烘焙

```bash
uv run python bake.py BlackburnAIE 5xHarvest
```

产物是 `~/Games/StarCraft II/Maps/BlackburnAIE__<hash>.SC2Map`（原图不动，副本改了依赖表）。
