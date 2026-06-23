# MiniBrowser

MiniBrowser 是一个 Windows 极简小窗浏览器，目标是接近 MenubarX 的常驻、轻量、小窗口体验。它基于 WPF + Microsoft WebView2，支持系统托盘、手机尺寸窗口、广告拦截、多窗口恢复和 portable 数据目录。

## 当前能力

- 手机尺寸默认窗口，可切换桌面/手机 User-Agent
- 顶部极简地址栏、返回、前进、刷新、菜单
- 托盘图标单击唤出/隐藏，窗口可显示在托盘图标正上方
- 全局快捷键 `Ctrl+Shift+Space` 唤出/隐藏第一个窗口
- `Ctrl+T` 新建小窗，`Ctrl+W` 关闭当前小窗
- 多窗口配置恢复，窗口大小、位置、透明度、置顶、边框状态会保存
- 轻量广告拦截，支持内置域名规则、简化 EasyList 解析、CSS cosmetic hiding
- 全局白名单与当前站点广告拦截开关
- Portable 数据目录：`Data/settings.json`、`Data/WebView2`、`Data/Logs`
- GitHub Releases 更新检查与 portable zip 自动下载/替换
- 当前用户安装脚本，可创建开始菜单、桌面快捷方式和卸载项

## 快速运行

双击：

```text
Open-MiniBrowser.cmd
```

第一次运行会自动构建 portable 版本，然后启动：

```text
dist\MiniBrowser-Portable\MiniBrowser.App.exe
```

要求：

- Windows 10/11
- Microsoft Edge WebView2 Runtime
- .NET 8 Desktop Runtime x64

## 开发运行

```powershell
.\scripts\Run-Dev.ps1
```

或手动执行：

```powershell
dotnet restore
dotnet build .\MiniBrowser.sln -c Release
dotnet run --project .\src\MiniBrowser.App\MiniBrowser.App.csproj
```

## 打包 Portable 版

```powershell
.\scripts\Build-Portable.ps1
```

输出：

```text
dist\MiniBrowser-Portable
dist\MiniBrowser-Portable.zip
```

`MiniBrowser-Portable.zip` 是 GitHub Release 自动更新默认查找的资产名。发布新版本时，请把这个 zip 上传到 GitHub Release，并用类似 `v0.4.9` 的 tag。

## 打包安装包

```powershell
.\scripts\Build-Installer.ps1
```

输出：

```text
dist\MiniBrowser-Setup.zip
```

安装包里包含 portable 程序和安装脚本。用户解压后运行：

```text
Install-MiniBrowser.cmd
```

默认安装到：

```text
%LOCALAPPDATA%\Programs\MiniBrowser
```

安装脚本会创建开始菜单快捷方式、桌面快捷方式，并在“应用和功能”里写入 MiniBrowser 卸载项。整个安装过程不需要管理员权限。

## 自动更新

应用内菜单包含 `Check for updates`。默认每天自动检查一次：

```text
https://api.github.com/repos/zhuchengxue/MiniBrowser/releases/latest
```

当发现新版本，且 release 中存在 `MiniBrowser-Portable.zip` 时，MiniBrowser 会下载 zip、启动外部更新脚本、关闭自身、替换程序文件并重新启动。更新会保留本地 `Data` 目录。

## 快捷键

- `Ctrl+Shift+Space`：唤出/隐藏第一个窗口
- `Ctrl+L`：聚焦地址栏
- `Ctrl+Shift+L`：显示顶部栏并聚焦地址栏
- `Ctrl+T`：从当前页面新建小窗
- `Ctrl+W`：关闭当前小窗
- `Alt+Left` / `Alt+Right`：返回 / 前进
- `F5` 或 `Ctrl+R`：刷新
- `F8`：Clean mode / Show controls
- `F9` 或 `Ctrl+Shift+F`：切换窗口边框

## 路线图

- 更完整的 EasyList 语法覆盖
- 自动发布 GitHub Release 的 CI workflow
- 更精细的站点级配置：窗口尺寸、UA、广告拦截、置顶等按站点保存
- 可选 MSIX/Inno Setup 安装器
