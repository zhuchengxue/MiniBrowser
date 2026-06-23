# MiniBrowser

Windows 极简小窗浏览器原型，目标是接近 MenubarX 的常驻、小窗、轻量体验。

## 当前版本

正式工程使用：

- WPF
- Microsoft WebView2
- 系统托盘 `NotifyIcon`
- 本地 JSON 设置
- 简易广告域名拦截

当前目标是 portable 小工具浏览器：发布包里的设置会保存到程序旁边的 `Data/settings.json`。

## 打包 Portable 版

```powershell
.\scripts\Build-Portable.ps1
```

输出目录：

```text
.\dist\MiniBrowser-Portable
```

把整个 `MiniBrowser-Portable` 文件夹发给别人即可。对方电脑需要安装 Microsoft Edge WebView2 Runtime 和 .NET 8 Desktop Runtime x64。

## 免编译运行

最简单的方式是双击：

```text
Open-MiniBrowser.cmd
```

如果双击后没有看到窗口，先看右下角系统托盘是否有 MiniBrowser 图标；点托盘图标里的 `Show` 可以重新显示。

```powershell
.\scripts\Start-TinyWindow.ps1
```

可传入网址：

```powershell
.\scripts\Start-TinyWindow.ps1 -Url "https://chat.openai.com"
```

这个脚本会尝试用 Microsoft Edge 的 `--app` 模式打开 390 x 844 的小窗。

## 构建正式版

安装 .NET 8 SDK 后运行：

```powershell
dotnet restore
dotnet build .\MiniBrowser.sln -c Release
dotnet run --project .\src\MiniBrowser.App\MiniBrowser.App.csproj
```

本仓库也提供开发启动脚本，会优先使用 `C:\tmp\dotnet8sdk`：

```powershell
.\scripts\Run-Dev.ps1
```

如果系统没有 WebView2 Runtime，请安装：

https://developer.microsoft.com/microsoft-edge/webview2/

## 第一版功能

- 手机尺寸默认窗口：390 x 844
- 地址栏
- 返回、前进、刷新
- 置顶切换
- 移动端 User-Agent 切换
- 常用网站按钮
- 关闭到托盘
- 托盘打开、退出
- 简易广告请求拦截

## 当前快捷键

- `Ctrl+Shift+Space`：唤出/隐藏主窗口
- `Ctrl+L` / `Ctrl+Shift+L`：显示顶部栏并聚焦地址栏
- `Ctrl+T`：从当前页面新建小窗
- `Ctrl+W`：关闭当前小窗
- `F5`：刷新
- `F8`：Clean mode / Show controls
- `F9` / `Ctrl+Shift+F`：无边框 / 有边框

## 三横菜单

顶部右侧三横菜单里包含常用网站、新建当前页面小窗、手机/桌面模式、置顶、尺寸、透明度、窗口边框、当前网站广告屏蔽、重置布局、设置和关闭当前小窗。

## 后续路线

- 全局快捷键唤出/隐藏
- 多窗口和站点配置
- EasyList 规则解析
- 白名单与每站点拦截开关
- 安装包和自动更新
