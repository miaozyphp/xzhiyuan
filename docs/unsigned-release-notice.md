# 未签名预览版

## 推荐下载

- `XZhiYuan-Setup-<version>-win-x64.exe`：Windows 安装版，推荐首次体验使用。
- `XZhiYuan-<version>-win-x64-portable.zip`：免安装便携包，解压后运行。

Apple Silicon Mac 候选版本仍在真实设备验收阶段，本次 Release 不提供 macOS 安装包。

## 0.1.21 更新内容

- 修复视频主题较多时，点击批量管理“全选”可能导致界面黑屏或假死的问题。
- 批量选择只更新复选框与选中样式，不再销毁并重建全部视频缩略图。
- 视频封面改为逐个提取静态首帧，完成后立即释放解码资源。
- 刷新主题列表与背景配置前主动释放旧视频节点，降低 WebView2 内存占用。
- 增加 WebView2 GPU 进程异常恢复，意外退出时自动重新加载工作台。

## 核心功能

- 图片与视频双模式背景，可调整可见度、模糊、填充方式和画面位置。
- 标准模式与深度模式，可分别控制配色、透明表面、组件、角标和首页布局。
- 首页、任务页、设置页可视化预览，无需反复启动 Codex 试效果。
- 自定义主题支持新建、复制、修改、快捷删除、批量删除、设为默认以及图片/视频批量拖入。
- 可选后台应用代理，只对已建立调试连接的 Codex 加载默认主题；普通启动不会被关闭。
- 安全模式只加载图片、配色和基础表面，适合低内存设备或排查兼容性问题。
- 后台代理不再关闭或强制结束普通启动的 Codex；显式重连只请求正常关闭。
- 限制媒体大小、分片处理 Blob、隐藏窗口暂停 WebView 和视频，并增加渲染进程恢复与诊断包导出。

![x纸鸢工作台](https://raw.githubusercontent.com/miaozyphp/xzhiyuan/main/docs/screenshots/workbench-overview.png)

此版本没有 Authenticode 代码签名。Windows 可能显示“未知发布者”或 Microsoft Defender SmartScreen 提示，这是当前发布方式的预期行为，不代表警告已被绕过。

下载时请同时取得 `SHA256SUMS.txt`，按照仓库中的 `docs/verify-download.md` 比对文件哈希。GitHub 还会为 Release 文件生成构建来源证明，用于确认文件由本仓库对应标签的 GitHub Actions 工作流生成。

只有以下位置属于项目发布渠道：

- 本仓库的 GitHub Releases；
- 本仓库公开记录的后续官方镜像。

请勿运行哈希不一致、来源不明或由第三方重新打包的安装程序。
