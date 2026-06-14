# Version Bump Helper
# Updates version in .csproj and changelog via VersionManager
# Usage: .\Bump-Version.ps1 -Version X.Y.Z -Type patch|minor|major -Message "Description"

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('patch', 'minor', 'major')]
    [string]$Type = "patch",

    [string]$Message = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Building VersionManager (Release)..." -ForegroundColor Yellow
dotnet build tools/VersionManager/VersionManager.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build VersionManager"
    exit 1
}

$versionManager = "artifacts/bin/VersionManager/release/VersionManager.dll"

Write-Host "Checking commits for recommended bump..." -ForegroundColor Yellow
dotnet $versionManager check-commits

Write-Host "Applying version bump to $Version..." -ForegroundColor Yellow
dotnet $versionManager bump --version $Version --type $Type --message "$Message"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Version bump failed"
    exit 1
}

Write-Host "Validating version consistency..." -ForegroundColor Yellow
dotnet $versionManager validate
if ($LASTEXITCODE -ne 0) {
    Write-Error "Version validation failed"
    exit 1
}

Write-Host "Version bump complete." -ForegroundColor Green
