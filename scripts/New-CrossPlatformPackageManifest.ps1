[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?(-rc\.[1-9]\d*(\.(0|[1-9]\d*))?)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'linux-x64')]
    [string] $RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$publishRoot = (Resolve-Path -LiteralPath $PublishDirectory).Path
$publishPrefix = $publishRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$manifestPath = Join-Path $publishRoot 'release-package.json'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    Remove-Item -LiteralPath $manifestPath -Force
}

$entryPoint = if ($RuntimeIdentifier -eq 'win-x64') {
    'SrvSurvey.Desktop.exe'
} else {
    'SrvSurvey.Desktop'
}

$files = @(
    Get-ChildItem -LiteralPath $publishRoot -File -Recurse |
        ForEach-Object {
            if (-not $_.FullName.StartsWith(
                    $publishPrefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Package file escaped the publish directory: '$($_.FullName)'."
            }

            $relativePath = $_.FullName.Substring($publishPrefix.Length).
                Replace('\', '/')

            [ordered]@{
                path = $relativePath
                size = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).
                    Hash.ToLowerInvariant()
            }
        } |
        Sort-Object { $_.path }
)

if ($files.Count -eq 0) {
    throw "The publish directory '$publishRoot' contains no files."
}

if ($files.path -notcontains $entryPoint) {
    throw "The expected entry point '$entryPoint' was not published."
}

$manifest = [ordered]@{
    schemaVersion = 1
    product = 'SrvSurvey.XP'
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    entryPoint = $entryPoint
    files = $files
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$json = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    $manifestPath,
    $json + [Environment]::NewLine,
    $utf8NoBom)

Write-Host "Created $manifestPath with $($files.Count) hashed files."
