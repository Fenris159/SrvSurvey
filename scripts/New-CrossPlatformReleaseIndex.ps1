[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$definitions = @(
    [ordered]@{
        runtimeIdentifier = 'win-x64'
        archive = "SrvSurvey-Avalonia-$Version-win-x64.zip"
        archiveType = 'zip'
    },
    [ordered]@{
        runtimeIdentifier = 'linux-x64'
        archive = "SrvSurvey-Avalonia-$Version-linux-x64.tar.gz"
        archiveType = 'tar.gz'
    }
)

$packages = @(
    foreach ($definition in $definitions) {
        $path = Join-Path $packageRoot $definition.archive
        $file = Get-Item -LiteralPath $path -ErrorAction Stop
        if (-not $file.PSIsContainer -and $file.Length -gt 0) {
            [ordered]@{
                runtimeIdentifier = $definition.runtimeIdentifier
                archive = $definition.archive
                archiveType = $definition.archiveType
                size = $file.Length
                sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).
                    Hash.ToLowerInvariant()
            }
        } else {
            throw "Release package '$path' is empty or is not a file."
        }
    }
)

$index = [ordered]@{
    schemaVersion = 1
    product = 'SrvSurvey.Avalonia'
    version = $Version
    packages = $packages
}

$outputFile = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($outputFile)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$json = $index | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText(
    $outputFile,
    $json + [Environment]::NewLine,
    $utf8NoBom)

Write-Host "Created $outputFile with $($packages.Count) packages."
