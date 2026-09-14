# omniMod

自用游戏 mod 的集中地。**一个 git 仓库管全部**——四个子项目原本各自独立的提交历史，
已经用 `git subtree` 合并进了这一个仓库（每个子项目的历史都完整保留，只是现在挂在
这一棵树下面了），子文件夹底下不再有自己的 `.git`。

| 目录 | 游戏 | 说明 |
|---|---|---|
| `omniOxygenNotIncludedMod/` | Oxygen Not Included（缺氧） | C#/Harmony mod：全能聚合存储 + 无人力自动化。mod 内部标识 `staticID: OmniMod` 不受这个文件夹改名影响，是给游戏识别用的稳定标识，跟源码文件夹叫什么名字没关系。 |
| `sc2Mod/` | StarCraft II | 离线化 + 自定义作弊 mod（快速建造/无限资源/宏速度等） |
| `omniDontStarveMod/` | Don't Starve | 离线化单机版自制 Lua mod：作弊菜单、箱子、管家、料理堆叠、环形世界生成…… |
| `omniDontStarveTogetherMod/` | Don't Starve Together | 联机版离线化 mod |

## 维护方式

跟管理任何普通仓库一样，`cd` 到 `omniMod/` 根目录直接操作 git 就行：

```bash
cd ~/Games/claude/omniMod
git add .
git commit -m "..."
```

改哪个子项目都行，`git status`/`git log` 现在能看到全部四个子项目 + 顶层文件的完整
改动和历史，不再需要分别进每个子文件夹操作。

## 历史来源

四个子项目原本是各自独立的仓库，2026-09-14 用 `git subtree add --prefix=<项目名>
<临时remote> master` 依次合并进来（未 squash，逐条提交历史都在，可以用
`git log --follow -- <项目名>/<文件>` 或直接 `git log <项目名>/` 只看某个子项目
自己的历史）。
