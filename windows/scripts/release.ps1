$ErrorActionPreference = "Stop"

Write-Host "== Release Prepare =="

# ---------------------------------------
# 路径定义（全部明确，不猜）
# ---------------------------------------

# scripts 目录
$scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# windows 目录
$windowsDir = Resolve-Path "$scriptsDir\.."

# 项目目录
$projectDir = Resolve-Path "$windowsDir\EasyNoteVault"

# dotnet publish 输出目录（固定）
$publishDir = Join-Path $projectDir "bin\Release\net8.0-windows\win-x64\publish"

# dist 目录（仓库根下）
$distDir = Resolve-Path "$windowsDir\..\dist" -ErrorAction SilentlyContinue
if (-not $distDir) {
    $distDir = New-Item -ItemType Directory -Path "$windowsDir\..\dist"
}

# ---------------------------------------
# 校验 publish 目录
# ---------------------------------------

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

Write-Host "Publish directory:"
Write-Host "  $publishDir"

# ---------------------------------------
# 清理旧 zip（确保只生成一个）
# ---------------------------------------

Write-Host "Cleaning old release files..."

Get-ChildItem -Path $distDir -Filter "*.zip" -ErrorAction SilentlyContinue |
    Remove-Item -Force

# ---------------------------------------
# 生成 zip 文件名
# 规则：EasyNoteVault-windows.zip
# （版本号由 GitHub Release 管理）
# ---------------------------------------

$zipName = "EasyNoteVault-windows.zip"
$zipPath = Join-Path $distDir $zipName

Write-Host "Release output:"
Write-Host "  $zipPath"

# ---------------------------------------
# 打包 publish 目录
# ---------------------------------------

Compress-Archive `
    -Path (Join-Path $publishDir "*") `
    -DestinationPath $zipPath `
    -Force

# ---------------------------------------
# 最终确认
# ---------------------------------------

if (-not (Test-Path $zipPath)) {
    throw "Release zip not created!"
}

Write-Host "Release package created successfully."
Write-Host "== Done =="
