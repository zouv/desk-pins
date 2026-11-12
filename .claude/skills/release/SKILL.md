---
name: release
description: 一条命令发布 DeskPins：校验并更新版本号、把 CHANGELOG 的 [未发布] 归档为 Release Notes、构建回归与打包、提交并推送 main 与 vX.Y.Z 标签，并通过 GitHub CLI 上传 GitHub Release 资产。仅在用户明确输入 /release X.Y.Z 或明确要求整套发版时使用，不要自动触发。
---

# release · 发布 DeskPins

> **输入**：`$ARGUMENTS` 必须是版本号 `X.Y.Z`，例如 `/release 0.4.0`。缺少版本号、格式不对或版本不高于当前版本时停止，不要猜。下文命令中的 `$VERSION` 均指 `$ARGUMENTS`；执行前可先 `VERSION="$ARGUMENTS"`。
>
> **upload 的目标**：默认是 `origin` 指向的 GitHub 仓库的 GitHub Releases（当前为 `zouv/desk-pins`），使用已登录的 `gh`。如果用户要上传到其他服务器，先修改本节的上传步骤，不要擅自猜测地址。
>
> 用户输入本命令即表示授权：修改版本与 CHANGELOG、提交并推送、创建标签、打包并上传。仍然**禁止**强推、改写已推送历史、提交凭据 / 用户数据 / 本机路径，或结束用户未确认的 DeskPins 进程。

## 0. 前置检查（失败即停，不改文件）

1. 必须位于项目根，当前分支为 `main`：
   ```bash
   git rev-parse --show-toplevel
   git branch --show-current
   ```
   不是 `main` 则停止，让用户先合并 / 切换。
2. 校验版本：`$ARGUMENTS` 匹配 `^[0-9]+\.[0-9]+\.[0-9]+$`；读取 `Directory.Build.props` 当前 `<Version>`，新版本必须更高。
3. 校验上传身份与仓库：
   ```bash
   git remote get-url origin
   gh auth status
   gh repo view --json nameWithOwner -q .nameWithOwner
   ```
   确认 origin 与 `gh` 当前仓库一致；不一致停止。
4. 检查工作树：
   ```bash
   git status --porcelain
   ```
   只应包含本次发布预期文件。出现 `.env`、`.runtime/`、`dist/`、`bin/`、`obj/`、`*.db`、`*.log`、用户数据、本机绝对路径或凭据 → 停止，先清理，不得提交。
5. 检查运行实例：
   ```bash
   powershell.exe -NoProfile -Command "Get-Process DeskPins -ErrorAction SilentlyContinue | Select-Object Id,Path"
   ```
   有实例则提示用户自行关闭后重跑；**不要** `Stop-Process`，也不要调用 `dev.bat` / 强制关闭脚本。
6. 确认 `CHANGELOG.md` 存在 `## [未发布]` 且正文非空。为空则停止并向用户确认本次发布内容。

## 1. 更新版本与归档 CHANGELOG

1. 用 Edit 把 `Directory.Build.props` 的
   `<Version>旧版本</Version>` 改为 `<Version>$ARGUMENTS</Version>`。
2. 用 Edit 把 `CHANGELOG.md` 的
   `## [未发布]` 改为
   `## [$ARGUMENTS] - YYYY-MM-DD`（日期用当天本地日期）。
   不要保留空 `[未发布]` 标题；下一次改动再新建。
3. 确认版本被 MSBuild 读到：
   ```bash
   dotnet msbuild src/DeskPins.App/DeskPins.App.csproj -getProperty:Version -nologo
   ```
4. 从新版本小节提取 GitHub Release Notes 到忽略目录（用 sed，避免 awk 的 `$0` 被技能参数替换）：
   ```bash
   VERSION="$ARGUMENTS"
   mkdir -p .runtime
   sed -n "/^## \\[$VERSION\\]/,/^## \\[/p" CHANGELOG.md | sed '$d' > .runtime/release-notes.md
   ```
   检查文件非空。
5. 运行 `sh check-docs.sh` 和 `git diff --check`。

## 2. 构建、回归、打包

1. 先执行直接构建与回归（不依赖会复制 `.runtime/build/dev` 的 `verify` 包装）：
   ```bash
   dotnet build DeskPins.sln -c Release --nologo
   tests/DeskPins.Tests/bin/Release/net48/DeskPins.Tests.exe --ui
   sh check-docs.sh
   ```
   任何失败都停止；不要提交。
2. 生成 debug / release 包（`package.ps1` 会再次构建并跑回归）：
   ```bash
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1
   ```
   失败时保留工作树，报告日志路径。因运行实例占用文件而失败时，请用户关闭实例后重跑，不要强杀。
3. 校验产物存在且版本一致：
   ```bash
   ls -l "dist/dev/DeskPins_${VERSION}_x64-unpack-debug.zip" \
         "dist/release/DeskPins_${VERSION}_x64-unpack.zip" \
         "dist/release/SHA256SUMS.txt"
   ```
   并检查 `SHA256SUMS.txt` 含两个 zip 的哈希。

## 3. 提交、标签与推送

1. 再次检查 `git status --porcelain`，逐项确认只包含本次发版文件（代码、文档、版本）。
2. 暂存并检查：
   ```bash
   git add -A
   git diff --cached --check
   git diff --cached --stat
   ```
   浏览 staged 内容，确认没有凭据、个人路径、用户数据、日志、二进制产物；发现即停止。
3. 提交（类别前缀沿用仓库约定；按当前会话要求附署名）：
   ```bash
   git commit -m "变更：发布 DeskPins $VERSION" \
              -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
   ```
4. 创建并推送标签：
   ```bash
   git tag -a "v$VERSION" -m "DeskPins $VERSION"
   git push origin main
   git push origin "v$VERSION"
   ```
   推送失败时停止，不要 `--force`。
5. 校验 `git status --short` 为空，且 `git rev-parse HEAD` 与 `git rev-list -n1 "v$VERSION"` 相同。

## 4. 上传 GitHub Release

1. 资产固定为三项（与历史 Release 一致）：
   - `dist/dev/DeskPins_${VERSION}_x64-unpack-debug.zip`
   - `dist/release/DeskPins_${VERSION}_x64-unpack.zip`
   - `dist/release/SHA256SUMS.txt`
2. 如果 `gh release view "v$VERSION"` 已存在：
   ```bash
   gh release upload "v$VERSION" \
     "dist/dev/DeskPins_${VERSION}_x64-unpack-debug.zip" \
     "dist/release/DeskPins_${VERSION}_x64-unpack.zip" \
     "dist/release/SHA256SUMS.txt" --clobber
   gh release edit "v$VERSION" --title "DeskPins $VERSION" --notes-file .runtime/release-notes.md
   ```
   否则新建：
   ```bash
   gh release create "v$VERSION" \
     "dist/dev/DeskPins_${VERSION}_x64-unpack-debug.zip" \
     "dist/release/DeskPins_${VERSION}_x64-unpack.zip" \
     "dist/release/SHA256SUMS.txt" \
     --title "DeskPins $VERSION" --notes-file .runtime/release-notes.md
   ```
3. 验证：
   ```bash
   gh release view "v$VERSION" --json tagName,url,assets
   ```
   确认三个资产名、大小与 Release 标题；失败时保留已推送提交与标签，报告可重试的上传命令，不要回滚或强推。

## 5. 收尾

向用户报告：

- 版本号与提交 / 标签（`v$VERSION`）；
- Release 页面 URL 与上传的三个资产；
- 构建、回归、打包结果；
- 未实测项（例如真实桌面 / DPI / 外部互通）不要写成已验证。

如任何一步失败，停在失败点，明确哪些操作已生效（尤其推送与 Release），不要声称整个发布成功。
