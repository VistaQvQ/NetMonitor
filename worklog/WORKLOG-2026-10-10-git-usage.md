# Git 历史清理操作手册（2026-10-10）

> 记录今天仓库清理用到的全部 git 命令用法、场景与踩坑经验，供后续参考。
> 目标：删除 `61f19f9` 之前的提交，彻底清理为「单分支 + 30 提交 + 无 tag + 无老对象」。

---

## 一、场景概述

| 步骤 | 操作 | 关键命令 |
|---|---|---|
| 1 | 备份旧历史 | `git branch backup-xxx` |
| 2 | 删除某提交之前的全部提交 | `checkout --orphan` + `cherry-pick` |
| 3 | 删除多余分支 | `git branch -D` / `push --delete` |
| 4 | 强制推送新历史 | `git push --force` |
| 5 | 删除 tags | `push --delete tag` |
| 6 | 物理清理对象 | `reflog expire` + `gc --prune=now` |

---

## 二、命令用法整理

### 1. 查看与探查

```bash
git status                          # 工作区状态
git branch --show-current           # 当前分支名
git branch -v                       # 各分支指向（含提交摘要）
git branch -a                       # 所有分支（含远程跟踪）
git remote -v                       # 远程配置
git log --oneline -N                # 最近 N 条提交（简洁）
git rev-list --count HEAD           # 当前分支提交总数
git rev-list --count <c>~1          # <c> 之前（更早）的提交数
git rev-list --count <c>..HEAD      # <c> 之后到 HEAD 的提交数
git ls-remote --heads origin        # 远程所有分支
git ls-remote --tags origin         # 远程所有 tags
git tag -l                          # 本地所有 tags
```

### 2. 历史重写：删除某提交之前的全部提交

核心思路：**把「目标提交 + 之后的提交」重放到一个全新的根上**（orphan + cherry-pick），比 `rebase --root` 删除前 N 条更干净。

```bash
# ① 备份旧历史（重要：重写不可逆）
git branch backup-pre-cleanup

# ② 从目标提交创建孤儿分支（无父提交）
git checkout --orphan tmp-cleanup <目标commit>

# ③ 用原提交信息创建新的根提交（-C 复用提交信息）
git commit -C <目标commit>

# ④ 重放目标提交之后的所有提交（range 起点不包含、终点包含）
git cherry-pick <目标commit>..backup-pre-cleanup

# ⑤ 验证后替换原分支（-M 强制覆盖旧分支）
git branch -M <原分支名>
```

**关键点**：
- `checkout --orphan` 会清空历史但保留工作区内容，`commit -C` 复用原提交信息（作者/日期/消息）。
- `cherry-pick <A>..<B>` 重放 A 之后到 B 的提交（不含 A）。
- **重写后所有提交哈希都会变**（相同内容也生成新哈希）。

### 3. 删除分支

```bash
git branch -D <分支名>              # 强制删除本地分支（即使"未合并"）
git push origin --delete <分支名>   # 删除远程分支
```

**踩坑**：远程**默认分支（受保护分支）**无法用 `push --delete` 删除，报 `GH006: Protected branch`。需先在 github 网页 Settings→Branches 改默认分支，或删除保护规则。

### 4. 远程与推送

```bash
git remote add origin <url>         # 关联远程
git remote remove origin            # 删除远程配置（解绑）
git push origin <分支> --force      # 强制推送（重写远程历史，不可逆）
git fetch --prune origin            # 拉取并清理已删除的远程跟踪引用
```

**踩坑**：`--force` **只重写分支，不动 tags**。若远程 tags 还钉在旧提交上，老提交仍通过 tags 可达，fetch 时会重新拉回本地。

### 5. tags 清理

```bash
git push origin --delete tag <名1> <名2> ...   # 删除远程 tag（可一次多个）
git tag -d <名>                                # 删除本地 tag
git ls-remote --tags origin                    # 确认远程 tag 已清空
```

**踩坑**：历史重写后，旧 tag 仍指向旧哈希（旧提交），指向的提交对象会随 fetch 重新拉回本地，导致 GUI「所有分支」视图仍显示老提交。删除 tag 后需再 `gc` 物理清理。

### 6. 对象物理清理（gc）

```bash
git reflog expire --expire=now --all   # 清空 reflog（否则旧提交通过 reflog 引用存活）
git gc --prune=now                     # 物理删除不可达对象
git count-objects -v                   # 查看对象数量（清理前后对比）
```

**关键点**：重写历史后，旧提交对象不会自动消失——`reflog` 的引用会保护它们。必须 `reflog expire` + `gc --prune=now` 才能真正删除。

### 7. 验证命令

```bash
git cat-file -t <hash>                          # 对象是否存在（Not a valid object = 已删）
git fsck --unreachable --no-reflogs             # 列出不可达对象
git merge-base --is-ancestor <c> HEAD           # 判断 <c> 是否在当前历史（祖先）
git diff HEAD origin/<分支> --stat              # 本地 vs 远程差异
```

---

## 三、踩坑经验（今天核心教训）

1. **`--force` 只重写分支，不动 tags** —— 老提交通过 tags 仍可达，这是"删了又出现"的根源。清理历史必须连带处理 tags。

2. **orphan + cherry-pick 改变所有提交哈希** —— 所有旧 tag 都会"失效"（指向旧哈希，而旧哈希对应的提交已被重写）。判断 tag 是否指向新历史，要用 `merge-base --is-ancestor`，不能凭肉眼猜哈希。

3. **受保护分支（默认分支）无法命令行删除** —— GH006 错误，需先在网页改默认分支或取消保护。

4. **`git reflog expire` + `git gc --prune=now` 缺一不可** —— 只 gc 不清 reflog，旧对象仍被 reflog 引用而存活。

5. **fetch 会"复活"已删除的对象** —— 只要远程还有引用（tag/分支）指向旧提交，fetch 就会把旧对象拉回本地。删本地对象前，先确认远程引用已清干净。

---

## 四、一键参考（本次完整流程）

```bash
# 1. 备份
git branch backup-pre-cleanup

# 2. 重写历史（让 <目标> 成为根）
git checkout --orphan tmp <目标commit>
git commit -C <目标commit>
git cherry-pick <目标commit>..backup-pre-cleanup
git branch -M NetMonitor-for-Cat

# 3. 删多余分支（本地 + 远程）
git branch -D backup-pre-cleanup
git push origin --delete <旧分支>

# 4. 推送
git push origin NetMonitor-for-Cat --force

# 5. 删 tags（本地 + 远程）
git push origin --delete tag <t1> <t2> ...
git fetch --prune origin

# 6. 物理清理
git reflog expire --expire=now --all
git gc --prune=now
```
