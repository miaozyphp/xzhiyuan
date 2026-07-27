# x纸鸢

x纸鸢是一款独立的 Windows Codex 主题工作台，提供图片与视频背景、配色与表面调整、标准/深度模式、主题预览、批量导入以及本地主题管理。

本项目是非官方社区项目，与 OpenAI 无隶属、合作或背书关系。Codex 和 OpenAI 是其各自权利人的商标。

## 主要能力

- 工作台优先启动，Codex 检测与主题扫描在后台完成。
- 主题和媒体保存在本机，无需在线主题服务。
- 标准模式调整背景、颜色和表面；深度模式可进一步调整首页组件与布局。
- 图片和视频可拖入主题库批量创建自定义主题。
- 主题应用失败时只卸下主题，不关闭 Codex。
- 可选的自动应用代理支持从系统直接启动 Codex 后加载默认主题。
- 不修改 Codex 安装包、`app.asar` 或 Windows 应用签名。

## 工作原理与安全边界

x纸鸢通过 Chrome DevTools Protocol 向正在运行的 Codex 注入可撤销的 CSS 和 JavaScript。由工作台启动 Codex 时，会启用仅监听 `127.0.0.1` 的本地调试端口，默认端口为 `9229`。启用调试端口意味着同一台电脑上的其他进程可能连接并控制该 Codex 实例，请只在可信设备和可信账户环境中使用。

主题媒体由随机端口上的本地回环服务提供，每次运行都会生成随机访问令牌。服务只监听 `127.0.0.1`，并且只读取主题数据目录中通过路径校验的文件。

自动应用功能是可选的。启用后，x纸鸢会在当前用户的 Windows 启动项中注册后台代理；禁用功能或卸载软件会停止使用该代理。为接管未启用调试端口的 Codex，代理可能先请求正常关闭，再仅终止已确认属于目标安装位置的进程。

详细信息见 [PRIVACY.md](PRIVACY.md) 和 [SECURITY.md](SECURITY.md)。

## 数据位置

主题、设置、媒体和轮转日志默认位于：

```text
%LocalAppData%\ThemeStudioForCodex
```

`ThemeStudioForCodex` 是为了兼容现有安装而保留的内部目录和可执行文件标识，不是对外产品名称。卸载应用不会自动删除用户主题，用户可自行备份或删除该目录。

## 开发

环境要求：Windows 10 2004 或更高版本、.NET SDK `8.0.423`、Microsoft Edge WebView2 Runtime。

```powershell
dotnet restore ThemeStudio.sln --locked-mode
dotnet test ThemeStudio.sln --no-restore
dotnet run --project src/ThemeStudio.App
```

架构和主题格式分别见 [architecture.md](docs/architecture.md) 与 [theme-contract.md](docs/theme-contract.md)。参与开发前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 构建发布包

安装 Inno Setup 6 后，从仓库根目录运行：

```powershell
.\scripts\build-release.ps1
```

脚本默认读取应用项目中的版本号，也可使用 `-Version 1.2.3` 显式覆盖。它会执行依赖恢复、测试、自包含发布、安装包构建并生成 SHA-256 校验值。完整流程见 [release.md](docs/release.md)。

## 下载与验证

当前 GitHub Releases 采用未签名预览版发布。Windows 可能显示“未知发布者”或 Microsoft Defender SmartScreen 提示；项目不会提供绕过系统警告的脚本。

每个版本同时提供安装包、便携包、`SHA256SUMS.txt`、机器可读发布清单和 GitHub 构建来源证明。运行前请按照 [verify-download.md](docs/verify-download.md) 校验 SHA-256，并只从本仓库的 GitHub Releases 下载。

## 资产与主题内容

仓库内的徽标和 Image 2.0 生成素材属于 x纸鸢项目资产，并随本仓库按 MIT 许可证分发。请勿在公开主题包中提交未获授权的角色、徽章、品牌素材或二次创作内容。

## 兼容性

Codex 更新可能改变页面结构。标准模式的兼容性通常更高；深度模式依赖具体页面结构，更新后可能临时停用不兼容的单个图层。提交兼容性问题时，请附上 x纸鸢版本、Codex 版本和脱敏后的日志。

## 许可证

源代码与仓库自有资产采用 [MIT License](LICENSE)。第三方组件及其许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
