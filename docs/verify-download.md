# Verify a Download

x纸鸢当前以未签名预览版发布。验证哈希可以发现下载损坏或文件被替换，但不能替代 Windows Authenticode 发布者身份验证。

## SHA-256

从同一个 GitHub Release 下载安装包、便携包和 `SHA256SUMS.txt`。在下载目录打开 PowerShell：

```powershell
Get-FileHash .\XZhiYuan-Setup-*-win-x64.exe -Algorithm SHA256
Get-FileHash .\XZhiYuan-*-win-x64-portable.zip -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

macOS：

```bash
shasum -a 256 XZhiYuan-Setup-*-macos-arm64.dmg
shasum -a 256 XZhiYuan-*-macos-arm64.zip
```

`Get-FileHash` 输出的哈希必须与 `SHA256SUMS.txt` 中对应文件名的值完全一致。不一致时不要运行文件，并通过仓库 Issue 报告。

## GitHub artifact attestation

安装 GitHub CLI 后，可以进一步确认文件由本仓库的 GitHub Actions 工作流生成：

```powershell
gh attestation verify .\XZhiYuan-Setup-*-win-x64.exe --repo OWNER/REPOSITORY
```

将 `OWNER/REPOSITORY` 替换为 Release 页面显示的仓库名称。来源证明验证的是构建来源，不会消除 Windows SmartScreen 警告。

## Source comparison

每个 GitHub Release 页面都会提供对应标签的源码归档。需要独立审计时，可从该标签按 [发布流程](release.md) 重新构建并比较功能和文件清单。
