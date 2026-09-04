# sc2Mod

在 Steam Deck 上**离线**玩星际2 + 自制 mod。

## 日常：离线打电脑

```bash
uv run python play.py
```

用 Proton 在 Steam 容器里把**正常的离线 SC2 客户端**拉起来 —— 有主菜单、大厅、选项、
快捷键设置，打完一场回大厅继续下一场。不需要登录、不碰战网。

之后全在游戏里操作：`创建自定义游戏` → 选图 → 加一个 `Computer`(残酷3) → 开打。
要 cheat 就在自定义游戏的「额外 Mod」里挂上 `cheat5x.SC2Mod`（见下）。

`uv run python play.py --editor` 起银河编辑器（做 mod 用）。

## cheat mod（做 5 倍采集等）

`mods/` 目录放自制 `.SC2Mod`。做法见 `RECON.md` 和 `mods/README.md`。

## api_match.py（bot 开发用，暂放）

用 SC2 的 AI-API（`-listen`）脚本组局的版本。这条路是给**写 bot 打残酷**用的，
不是日常玩 —— 它是单场对局、没有菜单、打完黑屏。`picker.py` 是它配套的选图器。
