# 未签名预览版

## 推荐下载

- `XZhiYuan-Setup-<version>-win-x64.exe`：Windows 安装版，推荐首次体验使用。
- `XZhiYuan-<version>-win-x64-portable.zip`：免安装便携包，解压后运行。

## 核心功能

- 图片与视频双模式背景，可调整可见度、模糊、填充方式和画面位置。
- 标准模式与深度模式，可分别控制配色、透明表面、组件、角标和首页布局。
- 首页、任务页、设置页可视化预览，无需反复启动 Codex 试效果。
- 自定义主题支持新建、复制、修改、删除、设为默认以及图片/视频批量拖入。
- 可选自动应用代理，直接启动 Codex 时也能加载默认主题。

![x纸鸢工作台](https://raw.githubusercontent.com/miaozyphp/xzhiyuan/main/docs/screenshots/workbench-overview.png)

此版本没有 Authenticode 代码签名。Windows 可能显示“未知发布者”或 Microsoft Defender SmartScreen 提示，这是当前发布方式的预期行为，不代表警告已被绕过。

下载时请同时取得 `SHA256SUMS.txt`，按照仓库中的 `docs/verify-download.md` 比对文件哈希。GitHub 还会为 Release 文件生成构建来源证明，用于确认文件由本仓库对应标签的 GitHub Actions 工作流生成。

只有以下位置属于项目发布渠道：

- 本仓库的 GitHub Releases；
- 本仓库公开记录的后续官方镜像。

请勿运行哈希不一致、来源不明或由第三方重新打包的安装程序。
