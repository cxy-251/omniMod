# sc2Mod

离线打星际2 —— 真人 vs 内置AI，不登录、不开战网。

## 用法

```bash
uv run python play.py          # 弹窗逐项选：地图 / 你的种族 / 电脑种族 / 难度 / 是否 cheat
uv run python play.py --last    # 用上次的选择，不弹窗
uv run python play.py --map AbyssalReefAIE --race T --enemy-race Z --difficulty cheatinsane --cheat
```

选择记在 `~/.config/sc2mod/last.json`。

## cheat（可选，每局可开关）

不改游戏文件，全靠 SC2 AI-API 的 debug 指令：
- 开局主基地旁 +12 农民
- `fast_build`：建造 / 训练瞬间完成
- 每 8 秒补满矿和气（“采不完” + 抹平残酷电脑的资源加成）

## 原理

`play.py` 用 Proton 起 `SC2_x64.exe -listen 127.0.0.1 -port N -displayMode 1`，
连本地 websocket 做 `CreateGame`（本地图 + 一个 Computer 对手）+ `JoinGame`（你的种族），
渲染窗口出来你正常操作，脚本只维持连接 + 按需喂 cheat。

详见 `RECON.md`。
