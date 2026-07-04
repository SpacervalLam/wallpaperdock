# WallpaperDock

WallpaperDock 是一个专为 Wallpaper Engine 设计的辅助工具，提供了直观、高效的壁纸管理和切换功能。它以右侧停靠的半透明窗口形式存在，让您可以快速访问和管理所有 Wallpaper Engine 壁纸。

## 功能特性

### 🎨 界面设计
- 右侧停靠式半透明窗口，不干扰桌面工作
- 平滑的滚动和惯性效果
- 响应式布局，支持不同屏幕尺寸

### 📁 壁纸管理
- 自动扫描 Steam 库中的所有 Wallpaper Engine 壁纸
- 自定义壁纸分类和分组
- 为壁纸设置别名
- R18 内容标记和过滤

### 🖥️ 多显示器支持
- 自动检测系统中的所有显示器
- 可为特定显示器单独设置壁纸
- 支持同时应用壁纸到所有显示器

### 🔍 智能搜索和过滤
- 按壁纸标题搜索
- 按分类、分组过滤
- R18 内容过滤开关

### 🚀 性能优化
- 异步加载壁纸信息
- 图片缓存机制
- 平滑的界面动画

## 程序截图

![WallpaperDock 程序截图](docs/shot.png)

## 系统要求

- Windows 10 版本 19041.0 或更高版本
- Windows 11
- .NET 10.0 或更高版本（仅精简版安装包需要，完整版内置运行时）
- Wallpaper Engine 已安装

## 安装说明

### 方法一：从源代码构建
1. 克隆仓库到本地
   ```bash
   git clone https://github.com/yourusername/WallpaperDock.git
   cd WallpaperDock
   ```

2. 使用 Visual Studio 2022 或更高版本打开解决方案
   - 确保已安装 "Universal Windows Platform development" 和 ".NET Desktop Development" 工作负载
   - 确保已安装 "Windows SDK 10.0.19041.0" 或更高版本

3. 构建解决方案
   - 选择 "Release" 配置
   - 构建 > 构建解决方案

4. 运行应用程序
   - 调试 > 开始执行（不调试）

### 方法二：使用发布版本

本项目提供两种安装包，请根据使用场景选择。

#### 两种安装包对比

| 项目 | Full（完整版） | Lite（精简版） |
|------|---------------|---------------|
| 文件名 | `WallpaperDockSetup_Full.exe` | `WallpaperDockSetup_Lite.exe` |
| 体积 | 约 57 MB | 约 8 MB |
| 部署模式 | Self-contained（自包含） | Framework-dependent（依赖框架） |
| 用户机器要求 | 无任何运行时依赖 | 需预装 .NET 10 Desktop Runtime + Windows App SDK 运行时 |

#### 推荐选择

- **普通用户**：下载 **Full 完整版**，双击即可安装运行，无需关心任何环境配置。
- **企业批量部署**：若已通过组策略统一推送 .NET 10 Desktop Runtime，可下载 **Lite 精简版**，节省带宽。
- **开发者自用**：本机已装 .NET SDK，使用 **Lite 精简版** 即可。

#### Lite 版运行时安装指南

如果选择 Lite 精简版，首次运行时若弹出 ".NET Desktop Runtime 不可用" 对话框，请按以下顺序安装：

1. **.NET 10 Desktop Runtime**

   下载地址：<https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0>

   选择 `Windows x64 Desktop Runtime` 安装程序（`dotnet-runtime-10.x.x-win-x64.exe`）。

2. **Windows App SDK 运行时**

   下载地址：<https://learn.microsoft.com/zh-cn/windows/apps/windows-app-sdk/downloads>

   在 "Latest downloads" 中选择 `Windows App Runtime 1.x` 下的 `x64` 安装包（文件名形如 `WindowsAppRuntimeInstall-x64.exe`）。

> 安装完上述两个运行时后重启系统，再启动 Wallpaper Dock 即可。

#### 安装步骤

1. 下载对应版本的安装程序
2. 运行 `WallpaperDockSetup_Full_x64_v*.exe` 或 `WallpaperDockSetup_Lite_x64_v*.exe` 并按照提示完成安装
3. 安装完成后，启动应用

> 注：完整版与精简版共用相同的 `AppId`，因此**两个安装包互斥**，安装其中一个会覆盖另一个，不会同时存在两个 Wallpaper Dock。

## 使用方法

### 基本操作
1. **启动应用**：运行 WallpaperDockWinUI.exe，应用会以右侧停靠的形式出现
2. **浏览壁纸**：滚动浏览所有可用的壁纸
3. **搜索壁纸**：在顶部搜索框中输入关键词
4. **切换壁纸**：点击壁纸缩略图即可切换到该壁纸

### 高级操作
1. **多显示器设置**：当系统有多个显示器时，点击壁纸会弹出菜单，可选择应用到特定显示器
2. **收藏壁纸**：点击壁纸卡片上的收藏按钮
3. **分类管理**：在壁纸卡片上右键点击，可设置分类
4. **分组管理**：使用顶部的分组下拉菜单过滤不同分组的壁纸
5. **R18 过滤**：使用顶部的 R18 开关控制是否显示 R18 内容

### 系统托盘操作
- **显示/隐藏窗口**：点击托盘图标
- **退出应用**：右键点击托盘图标，选择退出

## 技术栈

- **前端框架**：WinUI 3
- **后端**：C#
- **构建工具**：MSBuild
- **依赖注入**：Microsoft.Extensions.DependencyInjection
- **UI 模式**：MVVM (Model-View-ViewModel)

## 核心服务

1. **WallpaperEngineService**：负责扫描和管理 Wallpaper Engine 壁纸
2. **SteamLibraryService**：负责定位 Steam 库路径
3. **FavoritesService**：负责管理壁纸收藏、分类和元数据
4. **MonitorService**：负责检测和管理系统显示器
5. **AutoHideService**：负责窗口自动隐藏功能
6. **TrayIconService**：负责系统托盘图标功能
7. **ImageCacheService**：负责壁纸缩略图缓存
8. **ColorService**：负责颜色管理和主题色提取

## 贡献指南

欢迎对 WallpaperDock 进行贡献！如果您有任何建议或改进，请：

1. Fork 本仓库
2. 创建一个新的分支
3. 提交您的更改
4. 发起 Pull Request

## 许可证

本项目采用 MIT 许可证。详情请参阅 [LICENSE](LICENSE) 文件。

## 鸣谢

- [Wallpaper Engine](https://store.steampowered.com/app/431960/Wallpaper_Engine/) - 优秀的动态壁纸软件
- [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/) - 现代化的 Windows UI 框架

## 联系我们

如果您有任何问题、建议或反馈，请通过以下方式联系我们：

- 创建 [Issue](https://github.com/yourusername/WallpaperDock/issues)
- 发送邮件至：spacervallam@gmail.com

---

**享受 WallpaperDock 带来的便捷壁纸管理体验！** 🎉