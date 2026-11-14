# 历史粘贴板 / History-Clipboard

一款仅在本机保存剪贴板历史的 Windows 桌面应用。支持文字、图片、图片文字识别、搜索、置顶、删除和自动清理。

> A Windows desktop app where you can check your clipboard history and customize how many days to keep your clipboard content.

## 开始使用

当前发布包适用于 64 位 Windows 10（2004）及更高版本。下载 ZIP 并解压后，双击 `历史粘贴板.exe` 即可运行；请保留解压目录中的其他运行文件。首次启动会开启 Windows 开机自动启动；关闭窗口时软件会缩到系统托盘并继续记录。需要完全退出时，右键通知区域的图标并选择“退出”。

复制内容后打开软件，点击一张卡片即可尝试粘贴到打开软件前使用的窗口。若目标窗口不可用，内容仍会放回剪贴板，可切换到目标窗口按 `Ctrl+V`。

设置保留时长时可选择 1、3、5 天，或选择“自定义”并输入 1–30 天。置顶记录也会在过期后自动清理。图片文字识别使用 Windows 当前安装的 OCR 语言包；若没有可用语言包，请在 Windows 语言设置中安装中文或英文。

## 数据与隐私

历史内容和图片保存在本机：

`%LOCALAPPDATA%\ClipboardHistory`

应用不上传剪贴板内容。通过窗口上的“打开保存位置”可查看本地数据。请注意，剪贴板可能包含密码或其他敏感信息；过期清理及删除记录会清理对应的图片文件。

## 从源码运行

需要 Windows 和 .NET 10 SDK。在项目目录运行：

```powershell
dotnet run
```

生成可独立运行的 Windows x64 程序：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```
