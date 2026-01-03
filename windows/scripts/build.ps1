$ErrorActionPreference = "Stop"

Write-Host "== Build EasyNoteVault =="

dotnet restore EasyNoteVault/EasyNoteVault.csproj

dotnet build EasyNoteVault/EasyNoteVault.csproj `
    -c Release `
    --no-restore

Write-Host "Build finished successfully."
