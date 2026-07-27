# Contributing to x纸鸢

感谢你帮助改进 x纸鸢。提交代码或主题前，请先阅读本文件以及 [SECURITY.md](SECURITY.md)。

## 开发环境

- Windows 10 2004 或更高版本
- .NET SDK 8.0.423
- Microsoft Edge WebView2 Runtime
- Inno Setup 6.7.1，仅构建安装包时需要

```powershell
dotnet restore ThemeStudio.sln --locked-mode
dotnet test ThemeStudio.sln --no-restore
dotnet run --project src/ThemeStudio.App
```

如果需要更新 NuGet 依赖，请显式执行 `dotnet restore ThemeStudio.sln --force-evaluate`，检查并提交对应的 `packages.lock.json` 变更。

## 提交规范

1. 一个 Pull Request 只解决一个清晰问题，避免顺带重构无关模块。
2. 行为变更必须增加或更新测试；界面变更应附修改前后截图。
3. 提交前运行完整测试，并说明测试所用的 Windows、x纸鸢和 Codex 版本。
4. 不提交构建产物、日志、主题数据、个人路径或凭据。
5. Commit 信息建议使用 `feat:`、`fix:`、`docs:`、`test:`、`build:` 等清晰前缀。

## 主题与素材

只提交你有权按 MIT 许可证再分发的素材。不得提交未经授权的动漫角色、游戏素材、品牌徽标、摄影作品或其他受保护内容。主题示例应使用项目原创素材或来源清楚、许可证兼容的第三方素材，并在 Pull Request 中标明来源与许可证。

## 兼容性修改

深度模式依赖 Codex 页面结构。修改选择器或注入逻辑时，请同时验证：

- 标准模式仍可独立工作；
- 缺失目标只停用对应图层；
- 移除主题后 Codex 可恢复原始界面；
- 侧栏、首页、任务页、设置页和输入框没有重叠或不可读文本；
- 图片和视频背景均可加载并释放资源。

## 安全问题

不要通过公开 Issue 报告漏洞、利用步骤或用户数据。请按照 [SECURITY.md](SECURITY.md) 的私密报告流程提交。
