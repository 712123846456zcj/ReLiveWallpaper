<#
.SYNOPSIS
    把 ReLiveWallpaper 的构建产物组装成一个可直接运行/调试的目录。

.DESCRIPTION
    Lively 是多进程架构，运行时依赖仓库里不存在的 plugins/ 与 bundle/ 目录：

        <root>\Lively.exe                       内核（WPF 宿主）
        <root>\plugins\UI\Lively.UI.WinUI.exe   WinUI 3 前端，由内核拉起
        <root>\plugins\webview2\                网页壁纸播放器（默认）
        <root>\plugins\mpv\                     视频/图片/GIF 壁纸播放器（第三方）
        <root>\plugins\Watchdog\                子进程看门狗
        <root>\bundle\{wallpapers,themes}\      首次运行解包的默认壁纸/主题

    其中 plugins\mpv 与 bundle\ 属于官方发布的第三方资产，不在源码树内，
    通过 -ExternalAssets 指向已解包的 Lively 发布目录获取。

.NOTES
    必须用 Visual Studio 的 MSBuild 构建，不能用 dotnet CLI：
    Lively.Player.* / Lively.Utility.* 是 net472 老式 csproj，dotnet build
    无法解析其 PackageReference，会报大量假的类型缺失错误。

    本脚本会清空 -OutDir，执行前会做路径安全校验。

.EXAMPLE
    pwsh -File tools\deploy-dev.ps1
    pwsh -File tools\deploy-dev.ps1 -SkipBuild -Configuration Release -IncludeCef
#>
[CmdletBinding()]
param(
    # 构建配置。必须用 Release：Debug 构建的播放器走 #if DEBUG 的交互式调试分支，
    # 不向内核发送 IPC，无法作为壁纸运行。
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # 运行目录。会被清空后重建。
    [string] $OutDir = 'F:\1Prog_Cs\ReLiveWallpaper-dev\run-x64',

    # 已解包的官方 Lively 发布目录（含 Plugins\Mpv 与 Bundle）。
    # 留空或路径不存在时跳过：mpv 与默认壁纸不可用，内核与网页壁纸仍可运行。
    [string] $ExternalAssets = 'F:\1Prog_Cs\ReLiveWallpaper-dev\.cache\lively-extracted\app',

    # 跳过构建，仅重新组装。
    [switch] $SkipBuild,

    # 一并部署 CEF 网页播放器（+240MB，非默认浏览器时不需要）。
    [switch] $IncludeCef
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'src\Lively\Lively.sln'

function Write-Step([string] $Text) { Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Done([string] $Text) { Write-Host "    $Text" -ForegroundColor DarkGray }
function Write-Warn2([string] $Text) { Write-Host "!!  $Text" -ForegroundColor Yellow }

# --- 输出路径安全校验 ---------------------------------------------------
# 运行目录会被递归删除，必须先确认它是我们预期的那个目录：
# 必须是绝对路径、有父目录，且叶子名以 run- 开头。
$resolvedOut = [System.IO.Path]::GetFullPath($OutDir)
$outLeaf = Split-Path -Leaf $resolvedOut
$outParent = Split-Path -Parent $resolvedOut
if (-not [System.IO.Path]::IsPathRooted($OutDir)) { throw "OutDir 必须是绝对路径: $OutDir" }
if ($outLeaf -notlike 'run-*' -or [string]::IsNullOrWhiteSpace($outParent)) {
    throw "拒绝操作：OutDir 叶子名必须形如 run-*，当前为 '$outLeaf' ($resolvedOut)"
}

# --- 定位 MSBuild -------------------------------------------------------
function Get-MSBuildPath {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($vs) {
            $candidate = Join-Path $vs.Trim() 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    throw '未找到 Visual Studio MSBuild，请确认已安装 VS 2022/2026 的 .NET 桌面开发工作负载。'
}

# --- 构建 ---------------------------------------------------------------
if (-not $SkipBuild) {
    $msbuild = Get-MSBuildPath
    if ($Configuration -eq 'Debug') { Write-Warn2 'Debug 构建的播放器无法作为壁纸运行（#if DEBUG 分支），仅用于单机调试播放器。' }
    Write-Step "构建 solution ($Configuration|x64)"
    Write-Done $msbuild
    & $msbuild $solution /t:Build /p:Configuration=$Configuration /p:Platform=x64 /restore /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw "构建失败，退出码 $LASTEXITCODE" }
}

# --- 产物定位 -----------------------------------------------------------
# 注意：solution 的平台映射不统一（部分工程 x64 映射到 Any CPU），
# 所以这里按候选目录逐个探测，而不是硬编码单一输出路径。
function Resolve-OutputDir([string] $Probe, [string[]] $Candidates) {
    foreach ($dir in $Candidates) {
        if (Test-Path (Join-Path $dir $Probe)) { return $dir }
    }
    throw "缺少产物 '$Probe'，已探测：`n  " + ($Candidates -join "`n  ") + "`n请先不带 -SkipBuild 运行一次。"
}

$coreOut = Resolve-OutputDir 'Lively.exe' @(
    (Join-Path $repoRoot "src\Lively\Lively\bin\x64\$Configuration\net9.0-windows10.0.18362.0"),
    (Join-Path $repoRoot "src\Lively\Lively\bin\$Configuration\net9.0-windows10.0.18362.0"))

$uiOut = Resolve-OutputDir 'Lively.UI.WinUI.exe' @(
    (Join-Path $repoRoot "src\Lively\Lively.UI.WinUI\bin\x64\$Configuration\net9.0-windows10.0.22621.0"),
    (Join-Path $repoRoot "src\Lively\Lively.UI.WinUI\bin\$Configuration\net9.0-windows10.0.22621.0"))

$webView2Out = Resolve-OutputDir 'Lively.Player.WebView2.exe' @(
    (Join-Path $repoRoot "src\Lively\Lively.Player.WebView2\bin\x64\$Configuration"),
    (Join-Path $repoRoot "src\Lively\Lively.Player.WebView2\bin\$Configuration"))

$watchdogOut = Resolve-OutputDir 'Lively.Utility.Watchdog.exe' @(
    (Join-Path $repoRoot "src\Lively\Lively.Utility.Watchdog\bin\x64\$Configuration"),
    (Join-Path $repoRoot "src\Lively\Lively.Utility.Watchdog\bin\$Configuration"))

$cefOut = $null
if ($IncludeCef) {
    $cefOut = Resolve-OutputDir 'Lively.Player.CefSharp.exe' @(
        (Join-Path $repoRoot "src\Lively\Lively.Player.CefSharp\bin\x64\$Configuration"),
        (Join-Path $repoRoot "src\Lively\Lively.Player.CefSharp\bin\$Configuration"))
}

# --- 组装 ---------------------------------------------------------------
Write-Step "清空并重建 $resolvedOut"
Remove-Item -LiteralPath $resolvedOut -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $resolvedOut | Out-Null

function Copy-Tree([string] $Source, [string] $Target, [string] $Label) {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Target -Recurse -Force
    $count = (Get-ChildItem $Target -Recurse -File | Measure-Object).Count
    Write-Done ("{0} -> {1}  ({2} 个文件)" -f $Label, (Split-Path -Leaf $Target), $count)
}

Write-Step '内核 -> 根目录'
Copy-Tree $coreOut $resolvedOut $coreOut

Write-Step 'UI -> plugins\UI'
Copy-Tree $uiOut (Join-Path $resolvedOut 'plugins\UI') $uiOut

Write-Step 'WebView2 播放器 -> plugins\webview2'
Copy-Tree $webView2Out (Join-Path $resolvedOut 'plugins\webview2') $webView2Out

# WatchdogProcess 查找的是 "Lively.Watchdog"，与工程 AssemblyName 不一致，这里改名。
Write-Step 'Watchdog -> plugins\Watchdog'
$wdTarget = Join-Path $resolvedOut 'plugins\Watchdog'
Copy-Tree $watchdogOut $wdTarget $watchdogOut
foreach ($ext in @('.exe', '.pdb', '.exe.config')) {
    $src = Join-Path $wdTarget "Lively.Utility.Watchdog$ext"
    if (Test-Path $src) { Move-Item -LiteralPath $src -Destination (Join-Path $wdTarget "Lively.Watchdog$ext") -Force }
}
Write-Done '已重命名为 Lively.Watchdog.exe'

if ($IncludeCef) {
    Write-Step 'CEF 播放器 -> plugins\cef'
    Copy-Tree $cefOut (Join-Path $resolvedOut 'plugins\cef') $cefOut
}

# --- 第三方资产 ---------------------------------------------------------
if ([string]::IsNullOrWhiteSpace($ExternalAssets) -or -not (Test-Path $ExternalAssets)) {
    Write-Warn2 "未提供有效的 -ExternalAssets，跳过 mpv 与 bundle。"
    Write-Warn2 "视频/图片/GIF 壁纸将不可用，默认壁纸库为空。"
}
else {
    $mpvSource = Join-Path $ExternalAssets 'Plugins\Mpv'
    if (Test-Path $mpvSource) {
        Write-Step 'mpv -> plugins\mpv'
        Copy-Tree $mpvSource (Join-Path $resolvedOut 'plugins\mpv') $mpvSource
    }
    else { Write-Warn2 "未找到 $mpvSource" }

    $bundleSource = Join-Path $ExternalAssets 'Bundle'
    if (Test-Path $bundleSource) {
        Write-Step 'bundle -> bundle'
        Copy-Tree $bundleSource (Join-Path $resolvedOut 'bundle') $bundleSource
    }
    else { Write-Warn2 "未找到 $bundleSource" }
}

# --- 汇总 ---------------------------------------------------------------
$totalMb = (Get-ChildItem $resolvedOut -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ''
Write-Host ("完成：{0}  ({1:N0} MB)" -f $resolvedOut, $totalMb) -ForegroundColor Green
Write-Host ("启动内核：{0}" -f (Join-Path $resolvedOut 'Lively.exe')) -ForegroundColor Green
