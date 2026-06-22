<#
.SYNOPSIS
    一键构建 TaskbarLyrics 安装包：dotnet publish + Inno Setup 编译。

.DESCRIPTION
    1. 发布项目到 publish/win-x64（self-contained，不依赖 .NET Runtime）
    2. 从编译输出的 exe 中读取版本号
    3. 调用 ISCC.exe 编译安装包
    4. 输出到 dist/TaskbarLyrics-{version}-Setup.exe

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER SkipPublish
    跳过 dotnet publish 步骤，直接使用已有的 publish/win-x64 目录。

.PARAMETER IsccPath
    Inno Setup 编译器路径。不指定时自动从默认位置查找。

.PARAMETER AppVersion
    手动指定安装包版本号。不指定时自动从发布后的 exe 读取。

.EXAMPLE
    .\scripts\build-installer.ps1

.EXAMPLE
    .\scripts\build-installer.ps1 -SkipPublish
#>

param(
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [string]$IsccPath = "",
    [string]$AppVersion = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "publish\win-x64"
$distDir = Join-Path $repoRoot "dist"

# ── 1. dotnet publish ──────────────────────────────────────────────────────
if (-not $SkipPublish) {
    Write-Host ">>> dotnet publish (Self-Contained, win-x64)..." -ForegroundColor Cyan
    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    dotnet publish "$repoRoot\TaskbarLyrics.App\TaskbarLyrics.App.csproj" `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:SatelliteResourceLanguages=zh-Hans `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }

    Write-Host "    Published to: $publishDir" -ForegroundColor Green
}
else {
    Write-Host ">>> Skipping publish, using existing: $publishDir" -ForegroundColor Yellow
    if (-not (Test-Path "$publishDir\TaskbarLyrics.exe")) {
        throw "Published exe not found in $publishDir. Remove -SkipPublish or build first."
    }
}

# ── 2. 读取版本号 ──────────────────────────────────────────────────────────
$appVersion = $AppVersion.Trim()
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    $exePath = Join-Path $publishDir "TaskbarLyrics.exe"
    try {
        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
        $appVersion = $versionInfo.FileVersion
        if ([string]::IsNullOrWhiteSpace($appVersion)) {
            $appVersion = $versionInfo.ProductVersion
        }
    }
    catch {
        $appVersion = "2.0.0.0"
        Write-Warning "    Could not read version from exe, using $appVersion"
    }
}

if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "Could not determine app version. Pass -AppVersion explicitly."
}

Write-Host ">>> Installer version: $appVersion" -ForegroundColor Cyan

# ── 3. 查找 ISCC.exe ───────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $possiblePaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $possiblePaths) {
        if (Test-Path $p) {
            $IsccPath = $p
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or -not (Test-Path $IsccPath)) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup from https://jrsoftware.org/isdl.php"
}

Write-Host ">>> ISCC: $IsccPath" -ForegroundColor Cyan

# ── 4. 编译安装包 ──────────────────────────────────────────────────────────
$issFile = Join-Path $repoRoot "scripts\setup.iss"
Push-Location $repoRoot
try {
    & $IsccPath "/dMyAppVersion=$appVersion" $issFile
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC compilation failed (exit code $LASTEXITCODE)."
    }
}
finally {
    Pop-Location
}

# ── 5. 完成 ────────────────────────────────────────────────────────────────
$outputExe = Join-Path $distDir "TaskbarLyrics-$appVersion-Setup.exe"
if (Test-Path $outputExe) {
    Write-Host ">>> Installer created: $outputExe" -ForegroundColor Green
    Write-Host "    Size: $([math]::Round((Get-Item $outputExe).Length / 1MB, 2)) MB" -ForegroundColor Green
}
else {
    Write-Warning "    Installer not found at expected path: $outputExe"
}
