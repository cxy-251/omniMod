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
- `lib/libstorm.so*` —— 编译好的 StormLib（在一次性的 distrobox Arch 容器里编的，
  运行时不需要容器 —— glibc 2.41 够新，直接能在 SteamOS host 上 `dlopen`）。

## 做新 mod 的流程

1. 银河编辑器（在 Windows/Proton 里，或问 omni-deck 那边怎么起编辑器）建一个
   Extension mod，改想改的字段，导出 `.SC2Mod` 放进 `mods/`。
2. `bake.py` 里 `MOD_DEPS` 加一条 `key -> "bnet:<mod名>/0.0/999,file:Mods/<文件名>.SC2Mod"`，
   `MOD_INFO` 加一条展示用的中文名/描述。
3. `uv run python bake.py <地图 stem> <mod key>` 本地测试烘焙。
4. omni-deck 那边的 mod 勾选列表会自动读到新 mod（`bake.MOD_INFO`），不用改它的代码。

## 独立测试烘焙

```bash
uv run python bake.py BlackburnAIE 5xHarvest
```

产物是 `~/Games/StarCraft II/Maps/BlackburnAIE__<hash>.SC2Map`（原图不动，副本改了依赖表）。
