# Manual semver bump of package.json. Normally not needed: .githooks/post-commit
# bumps automatically from each commit title. Use this for a pre-release label or
# to force a specific part, then commit with COLDSNAP_SKIP_BUMP=1 so the hook
# doesn't bump a second time.
param(
    [ValidateSet('major', 'minor', 'patch')]
    [string]$Part = 'patch',

    [string]$PreReleaseLabel
)

$packageJsonPath = Join-Path $PSScriptRoot '..\package.json'

if (-not (Test-Path $packageJsonPath)) {
    throw "Could not find package.json at '$packageJsonPath'."
}

$text = Get-Content $packageJsonPath -Raw
$versionLine = [System.Text.RegularExpressions.Regex]::Match($text, '"version"\s*:\s*"(?<version>[^"]*)"')
if (-not $versionLine.Success) {
    throw 'package.json does not contain a version field.'
}

$oldVersion = $versionLine.Groups['version'].Value
$match = [System.Text.RegularExpressions.Regex]::Match($oldVersion, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-[0-9A-Za-z\-.]+)?$')
if (-not $match.Success) {
    throw "Version '$oldVersion' is not MAJOR.MINOR.PATCH."
}

$major = [int]$match.Groups['major'].Value
$minor = [int]$match.Groups['minor'].Value
$patch = [int]$match.Groups['patch'].Value

switch ($Part) {
    'major' { $newVersion = "$($major + 1).0.0" }
    'minor' { $newVersion = "$major.$($minor + 1).0" }
    'patch' { $newVersion = "$major.$minor.$($patch + 1)" }
}

if (-not [string]::IsNullOrWhiteSpace($PreReleaseLabel)) {
    $newVersion = "$newVersion-$($PreReleaseLabel.Trim())"
}

# Replace only the version value so the file's formatting stays untouched.
$text = $text.Substring(0, $versionLine.Groups['version'].Index) + $newVersion + $text.Substring($versionLine.Groups['version'].Index + $versionLine.Groups['version'].Length)
[System.IO.File]::WriteAllText((Resolve-Path $packageJsonPath), $text)

Write-Host "Updated package version: $oldVersion -> $newVersion"
Write-Host 'Next step: commit with COLDSNAP_SKIP_BUMP=1 so the post-commit hook keeps this version.'
