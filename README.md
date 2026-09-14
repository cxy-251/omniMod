# omniMod

自用游戏 mod 的集中地。四个子项目，**各自都是独立的 git 仓库**（自己管自己的提交历史，
互不关联），这一层只是物理上放在一起，不做统一版本管理。

| 目录 | 游戏 | 说明 |
|---|---|---|
| `omniOxygenNotIncludedMod/` | Oxygen Not Included（缺氧） | C#/Harmony mod：全能聚合存储 + 无人力自动化。mod 内部标识 `staticID: OmniMod` 不受这个文件夹改名影响，是给游戏识别用的稳定标识，跟源码文件夹叫什么名字没关系。 |
| `sc2Mod/` | StarCraft II | 离线化 + 自定义作弊 mod（快速建造/无限资源/宏速度等） |
| `omniDontStarveMod/` | Don't Starve | 离线化单机版自制 Lua mod：作弊菜单、箱子、管家、料理堆叠、环形世界生成…… |
| `omniDontStarveTogetherMod/` | Don't Starve Together | 联机版离线化 mod，脚手架已搭好，功能尚未全部移植 |

## 维护方式

改哪个 mod，就 `cd` 进那个 mod 自己的文件夹再操作 git，跟单独一个仓库时完全一样：

```bash
cd ~/Games/claude/omniMod/omniOxygenNotIncludedMod
git add . && git commit -m "..."
```

**不要在这一层（`omniMod/` 根目录）直接 `git add .`/`git commit`，以为能顺带把子项目
的改动也提交了——不会的**，四个子文件夹都在 `.gitignore` 里，这一层的 git 完全看不到
它们内部的任何改动，只管这一层自己的 `README.md`/`.gitignore` 这类顶层文件。

新增第 5 个 mod 项目时：把它的文件夹放进来（自己 `git init` 管自己的历史）→ 在
`.gitignore` 里加一行它的文件夹名 → 更新上面这张表。
