param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = "src/SrvSurvey.Desktop/Resources/legacy-localization.json"
)

$ErrorActionPreference = "Stop"
$languages = @("de", "es", "fr", "pt-BR", "ru", "zh-Hans", "ps")
$readyPath = Join-Path $RepositoryRoot "SrvSurvey/loc-ready.txt"
$readyNames = Get-Content -LiteralPath $readyPath |
    Where-Object { $_ -and -not $_.StartsWith("#") -and -not $_.StartsWith(" ") }
$sourceRoot = Join-Path $RepositoryRoot "SrvSurvey"
$result = [ordered]@{}

foreach ($language in $languages) {
    $candidates = @{}
    foreach ($readyName in $readyNames) {
        $stem = [IO.Path]::GetFileNameWithoutExtension($readyName)
        $translatedName = "$stem.$language.resx"
        $files = Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
            Where-Object { $_.Name -ceq $translatedName }
        foreach ($file in $files) {
            [xml]$document = [IO.File]::ReadAllText(
                $file.FullName,
                [Text.UTF8Encoding]::new($false))
            foreach ($entry in $document.root.data) {
                if ($null -eq $entry.source -or $null -eq $entry.value) {
                    continue
                }

                $source = [string]$entry.source
                $value = [string]$entry.value
                if (-not $candidates.ContainsKey($source)) {
                    $candidates[$source] = @{}
                }

                if (-not $candidates[$source].ContainsKey($value)) {
                    $candidates[$source][$value] = 0
                }

                $candidates[$source][$value]++
            }
        }
    }

    $translations = [ordered]@{}
    foreach ($source in ($candidates.Keys | Sort-Object)) {
        $selected = $candidates[$source].GetEnumerator() |
            Sort-Object -Property @{ Expression = "Value"; Descending = $true },
                @{ Expression = "Name"; Descending = $false } |
            Select-Object -First 1
        $translations[$source] = $selected.Name
    }

    $result[$language] = $translations
}

$resolvedOutput = Join-Path $RepositoryRoot $OutputPath
$outputDirectory = Split-Path -Parent $resolvedOutput
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$json = $result | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText(
    $resolvedOutput,
    $json + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Output "Generated $resolvedOutput"
foreach ($language in $languages) {
    Write-Output "$language`: $($result[$language].Count) strings"
}
