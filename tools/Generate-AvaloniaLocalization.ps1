param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$TranslateMissing,
    [switch]$RegenerateAll,
    [switch]$Verify,
    [string[]]$RefreshSourceFiles = @()
)

$ErrorActionPreference = "Stop"
$script:SurfaceMiningCommandPattern =
    '(?<!\w)(?:' +
    '\.alignment' +
    '|\.mining\s+(?:center\s+here|(?:<heading>|\d{1,3})\s+(?:<radius km>|<border radius km>|\d+(?:\.\d+)?)\s+(?:<number>|<location number>|\d+))' +
    '|\.mine\s+(?:delete\s+here|move\s+(?:<commodity>|[A-Za-z]+(?:[ -][A-Za-z]+)*?)\s+here|(?:<heading>|\d{1,3})\s+(?:<commodity>|[A-Za-z]+(?:[ -][A-Za-z]+)*?)\s+(?:<distance km>|\d+(?:\.\d+)?)\s+(?:<low\|medium\|high>|low|medium|high)/(?:<low\|medium\|high>|low|medium|high)|(?:<commodity>|[A-Za-z]+(?:[ -][A-Za-z]+)*?)\s+(?:<low\|medium\|high>|low|medium|high)/(?:<low\|medium\|high>|low|medium|high)\s+here)' +
    ')'
$script:TechnicalTokenPattern = [regex]::new(
    '(?:' + $script:SurfaceMiningCommandPattern +
    '|\b(?:Alt|Ctrl|Shift)(?:\s*\+\s*[A-Z0-9]+)+' +
    '|(?<!\w)\.[A-Za-z][A-Za-z0-9_-]*' +
    '|(?<!\w)\+[A-Za-z][A-Za-z0-9_-]*' +
    '|(?<!-)---(?!-)' +
    '|\b[A-Za-z0-9_{}-]+\.(?:json|zip|txt|csv|png|jpe?g|gif|exe|dll|axaml|xml|lock|log|tmp|bak|db|toml|md|html?|svg)\b' +
    '|\b(?:SrvSurvey|Spansh|EDSM|Canonn|Bioforge|Inara|Raven Colonial|Frontier|Elite Dangerous|Discord|VoxStellar|EDMC|EDDN|HMAC-SHA256|GPL-3\.0)\b)',
    [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
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
        $RepositoryRoot $TemporarySource | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "The localization source extractor failed."
    }

    if ($RefreshSourceFiles.Count -gt 0 -and
        (Test-Path -LiteralPath $Paths.SourcePath)) {
        $temporaryMergedSource = [IO.Path]::GetTempFileName()
        try {
            dotnet run --project $Paths.ToolProject --configuration Release -- `
                merge-source `
                $Paths.SourcePath `
                $TemporarySource `
                $temporaryMergedSource `
                $RefreshSourceFiles | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw "The targeted localization source merge failed."
            }

            return [IO.File]::ReadAllText(
                $temporaryMergedSource,
                [Text.Encoding]::UTF8)
        }
        finally {
            Remove-Item -LiteralPath $temporaryMergedSource `
                -Force `
                -ErrorAction SilentlyContinue
        }
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
        normalize-catalog $Paths.OutputPath $TemporaryCatalog | Out-Host
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
            -not [string]::IsNullOrWhiteSpace([string]$Prior[$source.Text]) -and
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

    Write-Host "Translating $($missing.Count) missing $Language strings..."
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
        if ([string]::IsNullOrWhiteSpace($translation)) {
            throw "$Language contains a blank translation for: $($source.Text)"
        }

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

    $json = ($Result | ConvertTo-Json -Depth 5).
        Replace("`r`n", "`n").Replace("`r", "`n")
    if ($Verify) {
        $expectedJson = $json + "`n"
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
        $json + "`n",
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

    $query = ($Batch | ForEach-Object {
        "&q=" + [uri]::EscapeDataString((Protect-TranslationText $_))
    }) -join ""
    $uri = "https://clients5.google.com/translate_a/t" +
        "?client=dict-chrome-ex&sl=en&tl=$TargetLanguage$query"
    $parts = @(Invoke-TranslationRequestWithRetry -Uri $uri)
    if ($parts.Count -ne $Batch.Count) {
        throw "Translator returned $($parts.Count) rows for a $($Batch.Count)-row batch."
    }

    for ($index = 0; $index -lt $Batch.Count; $index++) {
        $source = $Batch[$index]
        $value = Restore-TranslationText $parts[$index].Trim() $source
        if (-not (Test-Placeholders $source $value) -or
            -not (Test-ProtectedTokens $source $value)) {
            Write-Warning "Retrying one translation whose protected tokens changed: $source"
            $value = Invoke-ValidatedSingleTranslation $source $TargetLanguage
        }

        $Destination[$source] = $value
    }

    Start-Sleep -Milliseconds 120
}

function Invoke-ValidatedSingleTranslation(
    [string]$Source,
    [string]$TargetLanguage
) {
    $singleUri = "https://clients5.google.com/translate_a/t" +
        "?client=dict-chrome-ex&sl=en&tl=$TargetLanguage&q=" +
        [uri]::EscapeDataString((Protect-TranslationText $Source))
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $single = @(Invoke-TranslationRequestWithRetry -Uri $singleUri)
        if ($single.Count -ne 1) {
            throw "Translator returned $($single.Count) rows for a single-string retry."
        }

        $value = Restore-TranslationText $single[0].Trim() $Source
        if ((Test-Placeholders $Source $value) -and
            (Test-ProtectedTokens $Source $value)) {
            return $value
        }

        if ($attempt -lt 3) {
            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }

    throw "Translator did not preserve protected tokens for: $Source"
}

function Invoke-TranslationRequestWithRetry([string]$Uri) {
    $maximumAttempts = 4
    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        try {
            $response = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 45
            return @($response | ForEach-Object { [string]$_ })
        }
        catch {
            if ($attempt -eq $maximumAttempts) {
                throw
            }

            $delaySeconds = [Math]::Pow(2, $attempt)
            Write-Warning "Translation request failed; retrying in $delaySeconds seconds."
            Start-Sleep -Seconds $delaySeconds
        }
    }
}

function Protect-TranslationText([string]$Value) {
    $protected = [regex]::Replace(
        $Value,
        '\{(\d+)\}',
        'ZXQSRVARG$1QXZ')
    $tokenMatches = $script:TechnicalTokenPattern.Matches($protected)
    for ($index = $tokenMatches.Count - 1; $index -ge 0; $index--) {
        $tokenMatch = $tokenMatches[$index]
        $protected = $protected.Remove($tokenMatch.Index, $tokenMatch.Length).Insert(
            $tokenMatch.Index,
            "(ZXQSRVTECH${index}QXZ)")
    }

    return $protected
}

function Restore-TranslationText([string]$Value, [string]$Source) {
    $restored = [regex]::Replace(
        $Value,
        'ZXQSRVARG(\d+)QXZ',
        '{$1}')
    $tokens = $script:TechnicalTokenPattern.Matches($Source)
    for ($index = 0; $index -lt $tokens.Count; $index++) {
        $token = $tokens[$index].Value
        $restored = [regex]::Replace(
            $restored,
            "[（(]ZXQSRVTECH${index}QXZ[）)]",
            [Text.RegularExpressions.MatchEvaluator] { param($match) $token })
    }

    if ($restored.Contains('ZXQSRVTECH')) {
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
