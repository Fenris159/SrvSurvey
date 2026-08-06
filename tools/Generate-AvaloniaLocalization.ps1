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
    $paths = Get-LocalizationPaths
    $temporarySource = [IO.Path]::GetTempFileName()
    $temporaryCatalog = [IO.Path]::GetTempFileName()

    try {
        $freshSource = Invoke-SourceExtraction -Paths $paths -TemporarySource $temporarySource
        Save-OrVerifySource -Paths $paths -FreshSource $freshSource

        $sources = $freshSource | ConvertFrom-Json
        $existing = Get-ExistingCatalog -Paths $paths -TemporaryCatalog $temporaryCatalog
        $languages = Get-LocalizationLanguageMap
        $result = Build-LocalizationResult `
            -Sources $sources `
            -Existing $existing `
            -Languages $languages

        Save-OrVerifyCatalog -Paths $paths -Result $result
        Write-Output "Verified $($sources.Count) Avalonia localization sources across $($languages.Count) languages."
    }
    finally {
        Remove-Item -LiteralPath $temporarySource -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $temporaryCatalog -Force -ErrorAction SilentlyContinue
    }
}

function Get-LocalizationPaths {
    return [pscustomobject]@{
        SourcePath = Join-Path $RepositoryRoot `
            "src/SrvSurvey.Desktop/Resources/avalonia-localization-source.json"
        OutputPath = Join-Path $RepositoryRoot `
            "src/SrvSurvey.Desktop/Resources/avalonia-localization.json"
        ToolProject = Join-Path $RepositoryRoot `
            "tools/SrvSurvey.LocalizationTool/SrvSurvey.LocalizationTool.csproj"
    }
}

function Invoke-SourceExtraction {
    param($Paths, [string]$TemporarySource)

    dotnet run --project $Paths.ToolProject --configuration Release -- `
        $RepositoryRoot $TemporarySource
    if ($LASTEXITCODE -ne 0) {
        throw "The localization source extractor failed."
    }

    return [IO.File]::ReadAllText($TemporarySource, [Text.Encoding]::UTF8)
}

function Save-OrVerifySource {
    param($Paths, [string]$FreshSource)

    if ($Verify) {
        if (-not (Test-Path -LiteralPath $Paths.SourcePath) -or
            $FreshSource -cne [IO.File]::ReadAllText(
                $Paths.SourcePath,
                [Text.Encoding]::UTF8)) {
            throw "Avalonia localization sources are stale. Run tools/Generate-AvaloniaLocalization.ps1."
        }
        return
    }

    [IO.File]::WriteAllText(
        $Paths.SourcePath,
        $FreshSource,
        [Text.UTF8Encoding]::new($false))
}

function Get-ExistingCatalog {
    param($Paths, [string]$TemporaryCatalog)

    $existing = @{}
    if (-not (Test-Path -LiteralPath $Paths.OutputPath)) {
        return $existing
    }

    dotnet run --project $Paths.ToolProject --configuration Release -- `
        normalize-catalog $Paths.OutputPath $TemporaryCatalog
    if ($LASTEXITCODE -ne 0) {
        throw "The localization catalog normalizer failed."
    }

    $existingDocument = [IO.File]::ReadAllText(
        $TemporaryCatalog,
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

    return $existing
}

function Get-LocalizationLanguageMap {
    return [ordered]@{
        "de" = "de"
        "es" = "es"
        "fr" = "fr"
        "pt-BR" = "pt"
        "ru" = "ru"
        "zh-Hans" = "zh-CN"
        "ps" = "ps"
    }
}

function Build-LocalizationResult {
    param($Sources, $Existing, $Languages)

    $result = [ordered]@{}
    foreach ($language in $Languages.Keys) {
        $result[$language] = Build-LanguageTranslations `
            -Language $language `
            -TargetLanguage $Languages[$language] `
            -Sources $Sources `
            -Prior $(if ($Existing.ContainsKey($language)) { $Existing[$language] } else { @{} })
    }

    return $result
}

function Build-LanguageTranslations {
    param(
        [string]$Language,
        [string]$TargetLanguage,
        $Sources,
        $Prior
    )

    $translations = [Collections.Specialized.OrderedDictionary]::new(
        [StringComparer]::Ordinal)
    foreach ($source in $Sources) {
        if (-not $RegenerateAll -and $Language -ne "ps" -and
            $Prior.ContainsKey($source.Text) -and
            (Test-ProtectedTokens `
                $source.Text `
                ([string]$Prior[$source.Text]))) {
            $translations[$source.Text] = [string]$Prior[$source.Text]
        }
    }

    Fill-MissingTranslations `
        -Language $Language `
        -TargetLanguage $TargetLanguage `
        -Sources $Sources `
        -Translations $translations

    Assert-LanguageTranslationsValid `
        -Language $Language `
        -Sources $Sources `
        -Translations $translations

    return Convert-TranslationsToSortedArray -Translations $translations
}

function Fill-MissingTranslations {
    param(
        [string]$Language,
        [string]$TargetLanguage,
        $Sources,
        $Translations
    )

    $missing = @($Sources | Where-Object {
        -not $Translations.Contains($_.Text)
    })
    if ($Language -eq "ps") {
        foreach ($source in $missing) {
            $Translations[$source.Text] = `
                "* $($source.Text.ToUpperInvariant()) >>>!"
        }
        return
    }

    if ($missing.Count -eq 0) {
        return
    }

    if (-not $TranslateMissing) {
        throw "$Language is missing $($missing.Count) translation(s). Run with -TranslateMissing."
    }

    Write-Output "Translating $($missing.Count) missing $Language strings..."
    $translated = Invoke-GoogleTranslationBatch `
        -Sources $missing.Text `
        -TargetLanguage $TargetLanguage
    foreach ($source in $missing) {
        $Translations[$source.Text] = $translated[$source.Text]
    }
}

function Assert-LanguageTranslationsValid {
    param(
        [string]$Language,
        $Sources,
        $Translations
    )

    foreach ($source in $Sources) {
        $translation = [string]$Translations[$source.Text]
        if (-not (Test-Placeholders $source.Text $translation)) {
            throw "$Language did not preserve placeholders for: $($source.Text)"
        }

        if (-not (Test-ProtectedTokens $source.Text $translation)) {
            throw "$Language did not preserve a protected token for: $($source.Text)"
        }

        if ($translation.IndexOf([char]0xfffd) -ge 0) {
            throw "$Language contains a Unicode replacement character for: $($source.Text)"
        }
    }
}

function Convert-TranslationsToSortedArray {
    param($Translations)

    $keys = [string[]]@($Translations.Keys)
    [Array]::Sort($keys, [StringComparer]::Ordinal)
    $languageResult = [Collections.Generic.List[object]]::new()
    foreach ($key in $keys) {
        $languageResult.Add([ordered]@{
            source = $key
            translation = $Translations[$key]
        })
    }

    return $languageResult.ToArray()
}

function Save-OrVerifyCatalog {
    param($Paths, $Result)

    $json = $Result | ConvertTo-Json -Depth 5
    if ($Verify) {
        $expectedJson = $json + [Environment]::NewLine
        $currentJson = if (Test-Path -LiteralPath $Paths.OutputPath) {
            [IO.File]::ReadAllText($Paths.OutputPath)
        }
        else {
            ""
        }
        if (-not (Test-Path -LiteralPath $Paths.OutputPath) -or
            $expectedJson -cne $currentJson) {
            $expectedPath = Join-Path ([IO.Path]::GetTempPath()) `
                "srv-survey-expected-localization.json"
            [IO.File]::WriteAllText(
                $expectedPath,
                $expectedJson,
                [Text.UTF8Encoding]::new($false))
            throw "Avalonia translations are stale. Run tools/Generate-AvaloniaLocalization.ps1."
        }
        return
    }

    [IO.File]::WriteAllText(
        $Paths.OutputPath,
        $json + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false))
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
