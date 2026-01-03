param (
    [string]$ExePath,
    [string]$CertPath,
    [string]$CertPassword
)

$ErrorActionPreference = "Stop"

Write-Host "== Sign Executable =="

& signtool sign `
    /f $CertPath `
    /p $CertPassword `
    /tr http://timestamp.digicert.com `
    /td sha256 `
    /fd sha256 `
    $ExePath

Write-Host "Signing finished."
