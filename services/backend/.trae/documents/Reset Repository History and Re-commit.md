此操作将清除所有历史提交记录，并将当前文件状态作为全新的起点。

### 1. 创建无历史的全新分支
创建一个名为 `temp_new_branch` 的孤儿分支（orphan branch），该分支不包含任何过往历史。
```bash
git checkout --orphan temp_new_branch
```

### 2. 重新提交所有文件
将当前工作目录下的所有文件添加到暂存区，并进行全新的初始化提交。
```bash
git add .
git commit -m "全新提交 (Initial Commit)"
```

### 3. 删除旧分支
强制删除原有的本地分支，彻底清理旧的历史记录。
- 删除 `master`
- 删除 `feature/bootstrap-blazor-migration`
```bash
git branch -D master
git branch -D feature/bootstrap-blazor-migration
```

### 4. 重命名为 Master
将当前的全新分支重命名回 `master`，使其成为新的主分支。
```bash
git branch -m master
```

### 5. (可选) 强制推送到远程
如果需要将这个全新的历史覆盖到远程仓库（如 GitHub），需要执行强制推送。
```bash
# git push -f origin master
```
（目前仅执行本地操作，确认后执行）。
