<p align="center">
  <img alt="ReLiveWallpaper" src="resources/figma_promo_16x9.jpg" width="450" />
  <h2 align="center">ReLiveWallpaper</h2>
</p>

<img src="/resources/main_preview.gif" width="600" height="338"/>

## 关于

ReLiveWallpaper 是基于 **[Lively Wallpaper](https://github.com/rocksdanister/lively)** 的持续维护分支（fork）。

上游是一个开源的 Windows 动态桌面壁纸引擎：网页、视频、GIF、图片乃至应用程序都能直接作为壁纸运行。本分支保留 Lively 原有的内核架构（多进程播放器 + gRPC 控制 + WinUI 3 界面），在其之上围绕 **壁纸管理与个性化** 持续加入新功能。

* 上游项目：<https://github.com/rocksdanister/lively>
* 本仓库：<https://github.com/712123846456zcj/ReLiveWallpaper>
* 许可证：GPL-v3（与上游一致）

## 新增功能

### 收藏选项卡
* 导航栏新增独立的「收藏」页。
* 当前为占位页（敬请期待），收藏列表与筛选功能在后续版本实现。

### 图库文件夹分类管理
* 导入按钮旁新增「新建文件夹」，把图库中的壁纸归类整理。
* 壁纸右键菜单新增「分类到文件夹」。
* 新建文件夹弹窗可设置名称与封面；不指定封面时自动取文件夹内图片作为预览。
* 文件夹卡片为轮播预览（多图时依次切换，只占一个格子），未指定封面时自动采用均色封面。

### 背景填充与多图拼接
* 自定义壁纸新增背景色设置：调色板取色或「智能均色」，竖屏图片两侧的填充区域不再是死黑。
* 新增多图拼接模板：把多张竖屏图片按水平或垂直顺序拼成一张壁纸。

### 图片壁纸几何调整
* 上/下/左/右位移滑块（像素级）。
* 旋转滑块 0–180°（顺/逆时针）。
* 水平翻转、垂直翻转。
* 一键还原默认。

### 特效选项卡（壁纸叠加层）
* 新增「特效」页，水平卡片布局，每款特效带独立开关与子参数。
* 特效运行在 **壁纸之上的独立叠加层进程**（`Lively.Player.Overlay`），因此网页壁纸、视频壁纸，以及 Wallpaper Engine / Live2D EX 之类的第三方动态壁纸都能叠加使用。
* 首个特效 **雨滴**：雨量、下落速度、风力、水珠大小、不透明度、帧率，以及屏幕水珠 / 水珠滑落 / 涟漪开关，并可一键还原默认。
* 叠加层均为点击穿透、不抢焦点，桌面被全屏应用遮挡或壁纸暂停时自动停止渲染以节省资源。
* 烟雾、星火特效已列入计划（界面中占位显示）。

## 构建

必须使用 **Visual Studio 的 MSBuild**（部分播放器工程是 net472 老式 csproj，dotnet CLI 无法正确还原），且使用 **Release** 配置：

```powershell
# 用 vswhere 自动定位任意版本 Visual Studio 的 MSBuild
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -property installationPath
& "$msbuild\MSBuild\Current\Bin\MSBuild.exe" `
    src\Lively\Lively.sln /t:Build /p:Configuration=Release /p:Platform=x64 /v:m /nologo
```

Lively 是多进程架构，运行时依赖仓库中不存在的 `plugins\` 与 `bundle\` 目录。用仓库自带的脚本把构建产物组装成可直接运行的目录：

```powershell
pwsh -File tools\deploy-dev.ps1 -Configuration Release
```

### 项目结构（本分支新增/改动）

| 路径 | 说明 |
|--|--|
| `src/Lively/Lively.Player.Overlay/` | 叠加层特效播放器（独立进程，分层窗口 + UpdateLayeredWindow 渲染） |
| `src/Lively/Lively/Core/Effects/` | 内核侧特效服务：启停、参数持久化、随显示器变化重建 |
| `src/Lively/Lively/Assets/Plugins/Overlay/<id>/` | 各特效的参数模板（`LivelyProperties.json` 与本地化文件） |
| `src/Lively/Lively.UI.Shared/ViewModels/EffectsViewModel.cs` | 特效页视图模型 |
| `src/Lively/Lively.UI.WinUI/Views/Pages/EffectsView.xaml` | 特效页界面 |

## 待办

* 幻灯片模式（定时轮播壁纸）。
* 通用特效与「模板导入图片 / 视频」。
* 烟雾、星火特效实装。

## 许可证

沿用上游 [GPL-v3](LICENSE)。原项目版权归 [rocksdanister](https://github.com/rocksdanister) 所有。
