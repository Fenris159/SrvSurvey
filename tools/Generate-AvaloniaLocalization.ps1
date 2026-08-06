param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$TranslateMissing,
    [switch]$RegenerateAll,
    [switch]$Verify
)

$ErrorActionPreference = "Stop"
$script:TechnicalTokenPattern = [regex]::new(
    '(?i)(?:\b(?:Alt|Ctrl|Shift)(?:\s*\+\s*[A-Z0-9]+)+' +
    '|\b[A-Za-z0-9_-]+\.(?:json|zip|txt|csv|png|jpe?g|gif|exe|dll|axaml|xml)\b' +
    '|\b(?:SrvSurvey|Spansh|EDSM|Canonn|Bioforge|Inara|Raven Colonial|Frontier|Elite Dangerous|Discord)\b)',
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)

function Invoke-Generation {
$sourcePath = Join-Path $RepositoryRoot `
    "src/SrvSurvey.Desktop/Resources/avalonia-localization-source.json"
$outputPath = Join-Path $RepositoryRoot `
    "src/SrvSurvey.Desktop/Resources/avalonia-localization.json"
$toolProject = Join-Path $RepositoryRoot `
    "tools/SrvSurvey.LocalizationTool/SrvSurvey.LocalizationTool.csproj"
$temporarySource = [IO.Path]::GetTempFileName()
$temporaryCatalog = [IO.Path]::GetTempFileName()

try {
    dotnet run --project $toolProject --configuration Release -- `
        $RepositoryRoot $temporarySource
    if ($LASTEXITCODE -ne 0) {
        throw "The localization source extractor failed."
    }

    $freshSource = [IO.File]::ReadAllText(
        $temporarySource,
        [Text.Encoding]::UTF8)
    if ($Verify) {
        if (-not (Test-Path -LiteralPath $sourcePath) -or
            $freshSource -cne [IO.File]::ReadAllText(
                $sourcePath,
                [Text.Encoding]::UTF8)) {
            throw "Avalonia localization sources are stale. Run tools/Generate-AvaloniaLocalization.ps1."
        }
    }
    else {
        [IO.File]::WriteAllText(
            $sourcePath,
            $freshSource,
            [Text.UTF8Encoding]::new($false))
    }

    $sources = $freshSource | ConvertFrom-Json
    $existing = @{}
    if (Test-Path -LiteralPath $outputPath) {
        dotnet run --project $toolProject --configuration Release -- `
            normalize-catalog $outputPath $temporaryCatalog
        if ($LASTEXITCODE -ne 0) {
            throw "The localization catalog normalizer failed."
        }

        $existingDocument = [IO.File]::ReadAllText(
            $temporaryCatalog,
            [Text.Encoding]::UTF8) | ConvertFrom-Json
        foreach ($languageProperty in $existingDocument.PSObject.Properties) {
            $languageMap = [Collections.Generic.Dictionary[string,string]]::new(
                [StringComparer]::Ordinal)
            foreach ($translation in $languageProperty.Value) {
                $languageMap[[string]$translation.source] = `
                    [string]$translation.translation
            }

            $existing[$languageProperty.Name] = $languageMap
        }
    }

    $languages = [ordered]@{
        "de" = "de"
        "es" = "es"
        "fr" = "fr"
        "pt-BR" = "pt"
        "ru" = "ru"
        "zh-Hans" = "zh-CN"
        "ps" = "ps"
    }
    $result = [ordered]@{}
    foreach ($language in $languages.Keys) {
        $translations = [Collections.Specialized.OrderedDictionary]::new(
            [StringComparer]::Ordinal)
        $prior = if ($existing.ContainsKey($language)) {
            $existing[$language]
        }
        else {
            @{}
        }

        foreach ($source in $sources) {
            if (-not $RegenerateAll -and $language -ne "ps" -and
                $prior.ContainsKey($source.Text) -and
                (Test-ProtectedTokens `
                    $source.Text `
                    ([string]$prior[$source.Text]))) {
                $translations[$source.Text] = [string]$prior[$source.Text]
            }
        }

        $missing = @($sources | Where-Object {
            -not $translations.Contains($_.Text)
        })
        if ($language -eq "ps") {
            foreach ($source in $missing) {
                $translations[$source.Text] = `
                    "* $($source.Text.ToUpperInvariant()) >>>!"
            }
        }
        elseif ($missing.Count -gt 0 -and $TranslateMissing) {
            $target = $languages[$language]
            Write-Output "Translating $($missing.Count) missing $language strings..."
            $translated = Invoke-GoogleTranslationBatch `
                -Sources $missing.Text `
                -TargetLanguage $target
            foreach ($source in $missing) {
                $translations[$source.Text] = $translated[$source.Text]
            }
        }
        elseif ($missing.Count -gt 0) {
            throw "$language is missing $($missing.Count) translation(s). Run with -TranslateMissing."
        }

        foreach ($source in $sources) {
            $translation = [string]$translations[$source.Text]
            if (-not (Test-Placeholders $source.Text $translation)) {
                throw "$language did not preserve placeholders for: $($source.Text)"
            }

            if (-not (Test-ProtectedTokens $source.Text $translation)) {
                throw "$language did not preserve a protected token for: $($source.Text)"
            }

            if ($translation.IndexOf([char]0xfffd) -ge 0) {
                throw "$language contains a Unicode replacement character for: $($source.Text)"
            }
        }

        $keys = [string[]]@($translations.Keys)
        [Array]::Sort($keys, [StringComparer]::Ordinal)
        $languageResult = [Collections.Generic.List[object]]::new()
        foreach ($key in $keys) {
            $languageResult.Add([ordered]@{
                source = $key
                translation = $translations[$key]
            })
        }

        $result[$language] = $languageResult.ToArray()
    }

    $json = $result | ConvertTo-Json -Depth 5
    if ($Verify) {
        $expectedJson = $json + [Environment]::NewLine
        $currentJson = if (Test-Path -LiteralPath $outputPath) {
            [IO.File]::ReadAllText($outputPath)
        }
        else {
            ""
        }
        if (-not (Test-Path -LiteralPath $outputPath) -or
            $expectedJson -cne $currentJson) {
            $expectedPath = Join-Path ([IO.Path]::GetTempPath()) `
                "srv-survey-expected-localization.json"
            [IO.File]::WriteAllText(
                $expectedPath,
                $expectedJson,
                [Text.UTF8Encoding]::new($false))
            throw "Avalonia translations are stale. Run tools/Generate-AvaloniaLocalization.ps1."
        }
    }
    else {
        [IO.File]::WriteAllText(
            $outputPath,
            $json + [Environment]::NewLine,
            [Text.UTF8Encoding]::new($false))
    }

    Write-Output "Verified $($sources.Count) Avalonia localization sources across $($languages.Count) languages."
}
finally {
    Remove-Item -LiteralPath $temporarySource -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporaryCatalog -Force -ErrorAction SilentlyContinue
}
}

function Invoke-GoogleTranslationBatch {
    param(
        [Parameter(Mandatory)]
        [string[]]$Sources,
        [Parameter(Mandatory)]
        [string]$TargetLanguage
    )

    $translated = @{}
    $batch = [Collections.Generic.List[string]]::new()
    $batchLength = 0
    foreach ($source in $Sources) {
        $protected = Protect-TranslationText $source
        if ($batch.Count -ge 18 -or $batchLength + $protected.Length -gt 3500) {
            Invoke-TranslationRequest $batch $TargetLanguage $translated
            $batch.Clear()
            $batchLength = 0
        }

        $batch.Add($source)
        $batchLength += $protected.Length
    }

    if ($batch.Count -gt 0) {
        Invoke-TranslationRequest $batch $TargetLanguage $translated
    }

    return $translated
}

function Invoke-TranslationRequest {
    param(
        [Collections.Generic.List[string]]$Batch,
        [string]$TargetLanguage,
        [hashtable]$Destination
    )

    $separator = "`n[[[SRV_SPLIT]]]`n"
    $requestText = ($Batch | ForEach-Object {
        Protect-TranslationText $_
    }) -join $separator
    $uri = "https://translate.googleapis.com/translate_a/single" +
        "?client=gtx&sl=en&tl=$TargetLanguage&dt=t&q=" +
        [uri]::EscapeDataString($requestText)
    $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 45
    $responseText = ($response[0] | ForEach-Object { $_[0] }) -join ""
    $parts = [regex]::Split(
        $responseText,
        '\s*\[\[\[SRV_SPLIT\]\]\]\s*')
    if ($parts.Count -ne $Batch.Count) {
        throw "Translator returned $($parts.Count) rows for a $($Batch.Count)-row batch."
    }

    for ($index = 0; $index -lt $Batch.Count; $index++) {
        $source = $Batch[$index]
        $value = Restore-TranslationText $parts[$index].Trim() $source
        $sourcePlaceholders = [regex]::Matches($source, '\{\d+\}').Value
        $translatedPlaceholders = [regex]::Matches($value, '\{\d+\}').Value
        if ((($sourcePlaceholders | Sort-Object) -join '|') -cne
            (($translatedPlaceholders | Sort-Object) -join '|')) {
            throw "Translator did not preserve placeholders for: $source"
        }

        $Destination[$source] = $value
    }

    Start-Sleep -Milliseconds 120
}

function Protect-TranslationText([string]$Value) {
    $protected = [regex]::Replace(
        $Value,
        '\{(\d+)\}',
        '[[[SRV_ARG_$1]]]')
    $tokenMatches = $script:TechnicalTokenPattern.Matches($protected)
    for ($index = $tokenMatches.Count - 1; $index -ge 0; $index--) {
        $tokenMatch = $tokenMatches[$index]
        $protected = $protected.Remove($tokenMatch.Index, $tokenMatch.Length).Insert(
            $tokenMatch.Index,
            "[[[SRV_TECH_$index]]]")
    }

    return $protected
}

function Restore-TranslationText([string]$Value, [string]$Source) {
    $restored = [regex]::Replace(
        $Value,
        '\[\[\[SRV_ARG_(\d+)\]\]\]',
        '{$1}')
    $tokens = $script:TechnicalTokenPattern.Matches($Source)
    for ($index = 0; $index -lt $tokens.Count; $index++) {
        $restored = $restored.Replace(
            "[[[SRV_TECH_$index]]]",
            $tokens[$index].Value)
    }

    if ($restored.Contains('[[[SRV_TECH_')) {
        throw "Translator returned an unknown protected token for: $Source"
    }

    return $restored
}

function Test-ProtectedTokens([string]$Source, [string]$Translation) {
    foreach ($token in $script:TechnicalTokenPattern.Matches($Source)) {
        if ($Translation.IndexOf(
                $token.Value,
                [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            return $false
        }
    }

    return $true
}

function Test-Placeholders([string]$Source, [string]$Translation) {
    $sourcePlaceholders = [regex]::Matches($Source, '\{\d+\}').Value |
        Sort-Object
    $translatedPlaceholders = [regex]::Matches(
        $Translation,
        '\{\d+\}').Value | Sort-Object
    return (($sourcePlaceholders -join '|') -ceq
        ($translatedPlaceholders -join '|'))
}

Invoke-Generation
