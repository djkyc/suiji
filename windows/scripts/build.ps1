$ErrorActionPreference = "Stop"

Write-Host "== Build EasyNoteVault =="

$root = Resolve-Path "$PSScriptRoot\.."
$project = Join-Path $root "EasyNoteVault/EasyNoteVault.csproj"

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

dotnet restore $project
dotnet build $project -c Release --no-restore

Write-Host "Build finished successfully."
