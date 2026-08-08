[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $ChangelogPath,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$resolvedChangelogPath = Resolve-Path -LiteralPath $ChangelogPath -ErrorAction Stop
$changelog = Get-Content -LiteralPath $resolvedChangelogPath -Raw
$escapedVersion = [regex]::Escape($Version)
$headingPattern = "(?m)^## \[$escapedVersion\] - \d{4}-\d{2}-\d{2}\r?$"
$heading = [regex]::Match($changelog, $headingPattern)

if (-not $heading.Success) {
    throw "CHANGELOG.md does not contain a release section for version $Version."
}

$sectionStart = $heading.Index + $heading.Length
$remainingContent = $changelog.Substring($sectionStart).TrimStart("`r", "`n")
$nextHeading = [regex]::Match($remainingContent, '(?m)^## \[[^\r\n]+\].*\r?$')
$releaseNotes = if ($nextHeading.Success) {
    $remainingContent.Substring(0, $nextHeading.Index)
}
else {
    $remainingContent
}

$releaseNotes = [regex]::Replace(
    $releaseNotes,
    '(?m)^\[[^\]]+\]:[ \t]+\S+[ \t]*\r?\n?',
    '')
$releaseNotes = $releaseNotes.Trim()

if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
    throw "CHANGELOG.md contains no release notes for version $Version."
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

[IO.File]::WriteAllText(
    $OutputPath,
    "$releaseNotes$([Environment]::NewLine)",
    [Text.UTF8Encoding]::new($false))

Write-Host "Resolved release notes for Prometheus $Version from CHANGELOG.md."
