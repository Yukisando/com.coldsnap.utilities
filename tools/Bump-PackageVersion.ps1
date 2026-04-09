param(
    [string]$PreReleaseLabel,

    [switch]$NoTimestampSuffix
)

$packageJsonPath = Join-Path $PSScriptRoot '..\package.json'

if (-not (Test-Path $packageJsonPath)) {
    throw "Could not find package.json at '$packageJsonPath'."
}

$package = Get-Content $packageJsonPath -Raw | ConvertFrom-Json

if (-not $package.version) {
    throw 'package.json does not contain a version field.'
}

$versionPattern = '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prerelease>[0-9A-Za-z\-.]+))?$'
$match = [System.Text.RegularExpressions.Regex]::Match($package.version, $versionPattern)

if (-not $match.Success) {
    throw "Version '$($package.version)' is not valid semver for this script."
}

$currentMajor = [int]$match.Groups['major'].Value
$currentMinor = [int]$match.Groups['minor'].Value
$dateMajor = [int](Get-Date).ToUniversalTime().ToString('yyyyMMdd')

if ($currentMajor -eq $dateMajor) {
    $newVersion = "$dateMajor.$($currentMinor + 1).0"
}
else {
    $newVersion = "$dateMajor.0.0"
}

if (-not [string]::IsNullOrWhiteSpace($PreReleaseLabel)) {
    $preReleaseVersion = $PreReleaseLabel.Trim()
    if (-not $NoTimestampSuffix) {
        $timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
        $preReleaseVersion = "$preReleaseVersion.$timestamp"
    }

    $newVersion = "$newVersion-$preReleaseVersion"
}

$oldVersion = $package.version
$package.version = $newVersion

$package | ConvertTo-Json -Depth 10 | Set-Content $packageJsonPath

Write-Host "Updated package version: $oldVersion -> $newVersion"
Write-Host "Next step: commit package.json or let the GitHub workflow push the updated version."