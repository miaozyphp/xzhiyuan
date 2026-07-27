# Privacy

x纸鸢不包含遥测、行为分析、广告 SDK 或在线账户系统，也不会由应用自身将主题、媒体、Codex 对话或使用记录上传到项目维护者的服务器。

## Local data

以下内容默认保存在 `%LocalAppData%\ThemeStudioForCodex`：

- 主题 JSON 配置；
- 用户导入的图片、视频和角标；
- 默认主题、自动应用和调试端口设置；
- 当前日志与一个轮转后的历史日志。

日志用于记录启动、连接和错误信息，可能包含本机文件路径或异常文本，但设计上不读取或记录 Codex 对话内容。提交日志前请自行检查并移除个人信息。

## Network activity

x纸鸢运行时只主动连接本机回环地址，用于发现 Codex 调试端口和提供主题媒体。安装 WebView2 Runtime 时，微软官方安装程序可能连接微软服务器；Codex 与 WebView2 自身的网络行为受其各自隐私政策约束，不属于本项目控制范围。

## Startup registration

只有用户启用“自动应用”后，x纸鸢才会在当前用户的 Windows 启动项中添加 `ThemeStudioForCodex` 项。关闭自动应用会移除该项。

## Removing data

卸载程序会删除应用文件和工作台 WebView2 缓存，但默认保留主题数据，防止误删用户媒体。彻底删除时，请先退出托盘中的 x纸鸢，再删除 `%LocalAppData%\ThemeStudioForCodex`。删除前请备份需要保留的自定义主题。
