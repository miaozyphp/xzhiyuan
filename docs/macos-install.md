# macOS 安装说明

x纸鸢 Mac 预览版适用于 M1、M2、M3、M4 及后续 Apple Silicon 芯片，暂不支持 Intel Mac。

## 安装

1. 从项目构建页面下载 Apple Silicon 候选 DMG。
2. 打开 DMG，把“x纸鸢”拖入“应用程序”文件夹。
3. 在 Finder 的“应用程序”中找到 x纸鸢。
4. 首次启动时按住 Control 点击应用，选择“打开”，再确认一次“打开”。

当前预览版没有 Apple Developer ID 签名和公证，因此直接双击时 macOS 可能阻止启动。请不要关闭 Gatekeeper，也不要从第三方网盘下载安装包。

## 使用

- 把 Codex.app 放在系统或当前用户的“应用程序”文件夹。
- x纸鸢关闭窗口后会留在菜单栏，菜单栏图标可重新打开或真正退出。
- 开启“自动应用”后，x纸鸢会注册为登录项，并在 Codex 启动时加载默认主题。
- 更新下载完成后，x纸鸢会打开新的 DMG；把新版应用拖入“应用程序”并覆盖旧版即可。

## 校验

Release 页面同时提供 `SHA256SUMS.txt`。可在终端中运行：

```bash
shasum -a 256 XZhiYuan-Setup-0.2.0-macos-arm64.dmg
```

输出应与 `SHA256SUMS.txt` 中对应文件的值完全一致。
