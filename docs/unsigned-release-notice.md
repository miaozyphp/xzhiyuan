# 未签名预览版

此版本没有 Authenticode 代码签名。Windows 可能显示“未知发布者”或 Microsoft Defender SmartScreen 提示，这是当前发布方式的预期行为，不代表警告已被绕过。

下载时请同时取得 `SHA256SUMS.txt`，按照仓库中的 `docs/verify-download.md` 比对文件哈希。GitHub 还会为 Release 文件生成构建来源证明，用于确认文件由本仓库对应标签的 GitHub Actions 工作流生成。

只有以下位置属于项目发布渠道：

- 本仓库的 GitHub Releases；
- 本仓库公开记录的后续官方镜像。

请勿运行哈希不一致、来源不明或由第三方重新打包的安装程序。
