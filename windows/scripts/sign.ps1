param (
    [string]$ExePath,
    [string]$CertPath,
    [string]$CertPassword
)

$ErrorActionPreference = "Stop"

Write-Host "== Sign Executable =="

if (-not (Test-Path $ExePath)) {
    throw "Exe not found: $ExePath"
}

if (-not (Test-Path $CertPath)) {
    throw "Cert not found: $CertPath"
}

# 🔎 自动查找 signtool.exe（GitHub Actions / 本机通用）
$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$signtool = Get-ChildItem $sdkRoot -Recurse -Filter signtool.exe |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    Write-Host "signtool not found, skip signing."
    exit 0
}

Write-Host "Using signtool: $($signtool.FullName)"

& $signtool.FullName sign `
    /f $CertPath `
    /p $CertPassword `
    /fd sha256 `
    /td sha256 `
    /tr http://timestamp.digicert.com `
    $ExePath

Write-Host "Signing finished."
