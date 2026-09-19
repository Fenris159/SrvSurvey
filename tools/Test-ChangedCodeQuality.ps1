param(
    [string]$BaseRef = "origin/SrvSurvey-Avalonia",
    [string]$Solution = "SrvSurvey.slnx"
)

$ErrorActionPreference = "Stop"

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $output = & git @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return $output
}

function Find-NullConditionalEventFindings {
    param(
        [string]$RepositoryRoot,
        [hashtable]$ChangedRanges
    )

    $blockPattern = [regex]::new(
        '(?ms)^(?<indent>[ \t]*)if\s*\(\s*(?<receiver>(?:this\.)?[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s+(?:is\s+not\s+null|!=\s*null)\s*\)\s*\r?\n\k<indent>\{\s*\r?\n(?<body>.*?)^\k<indent>\}',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
    )

    foreach ($relativePath in $ChangedRanges.Keys) {
        $absolutePath = Join-Path $RepositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath)) {
            continue
        }

        $content = Get-Content -LiteralPath $absolutePath -Raw
        foreach ($match in $blockPattern.Matches($content)) {
            $receiverPattern = [regex]::Escape($match.Groups['receiver'].Value)
            $body = $match.Groups['body'].Value
            if ($body -notmatch "^\s*$receiverPattern\.[A-Za-z_][A-Za-z0-9_]*\s*(?:\+=|-=)") {
                continue
            }

            $braceDepth = 0
            $topLevelSemicolons = 0
            foreach ($character in $body.ToCharArray()) {
                if ($character -eq '{') {
                    $braceDepth++
                }
                elseif ($character -eq '}') {
                    $braceDepth--
                }
                elseif ($character -eq ';' -and $braceDepth -eq 0) {
                    $topLevelSemicolons++
                }
            }

            if ($topLevelSemicolons -ne 1) {
                continue
            }

            $startLine = 1 + ([regex]::Matches($content.Substring(0, $match.Index), "\n")).Count
            $endLine = $startLine + ([regex]::Matches($match.Value, "\n")).Count
            $isChangedBlock = $ChangedRanges[$relativePath] | Where-Object {
                $_.Start -le $endLine -and $_.End -ge $startLine
            }
            if ($isChangedBlock) {
                [pscustomobject]@{
                    File = $relativePath
                    Line = $startLine
                    Column = $match.Groups['indent'].Length + 1
                    Rule = 'IDE0031'
                    Message = 'Null check can be simplified with null-conditional event assignment.'
                }
            }
        }
    }
}

function Invoke-LocalizationCatalogVerify {
    param([string]$RepositoryRoot)

    $scriptPath = Join-Path $RepositoryRoot "tools/Generate-AvaloniaLocalization.ps1"
    & $scriptPath -Verify
    if ($LASTEXITCODE -ne 0) {
        throw "Avalonia localization catalog verification failed. Run tools/Generate-AvaloniaLocalization.ps1."
    }

    Write-Output "Avalonia localization catalogs match the extracted sources."
}

function Invoke-LocalizationCatalogTests {
    param([string]$RepositoryRoot)

    # Run after the Sonar rebuild so MSBuild/test hosts do not share a poisoned
    # build-server session on Linux.
    & dotnet test `
        (Join-Path $RepositoryRoot "tests/SrvSurvey.Desktop.Tests/SrvSurvey.Desktop.Tests.csproj") `
        --configuration Release `
        --filter "FullyQualifiedName~Localization.LocalizationCatalogTests" `
        --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "Localization catalog tests failed."
    }

    Write-Output "Localization catalog tests passed."
}

function Invoke-CSharpierCheck {
    & dotnet csharpier check .
    if ($LASTEXITCODE -ne 0) {
        throw "CSharpier formatting check failed. Run 'dotnet csharpier format .' and retry."
    }

    Write-Output "CSharpier formatting check passed."
}

function Test-IsCoverableProductionPath {
    param(
        [string]$RepositoryRoot,
        [string]$RelativePath
    )

    $normalized = $RelativePath.Replace('\', '/')
    if ($normalized -notmatch '^src/') {
        return $false
    }

    if (
        $normalized -match '^src/ThirdParty/' -or
        $normalized -match '\.(axaml|Designer|g)\.cs$' -or
        $normalized -match '/Program\.cs$' -or
        $normalized -match 'DesktopRuntime\.Composition\.cs$' -or
        $normalized -match 'ControllerInputBackend\.cs$' -or
        $normalized -match '/Platform/Overlay/(GameScreenCapture|GameWindowSwitcher|OverlayPlatformService|X11GameWindowTracker|X11Native|X11OverlayPlatformService)\.cs$' -or
        $normalized -match '/Platform/Overlay/.+Coordinator\.cs$' -or
        $normalized -match '/Platform/(ApplicationInstanceManager|ApplicationProcessPathResolver|WindowsRestartManagerProcessFinder)\.cs$'
    ) {
        return $false
    }

    $absolutePath = Join-Path $RepositoryRoot $normalized
    if (Test-Path -LiteralPath $absolutePath) {
        $source = Get-Content -LiteralPath $absolutePath -Raw
        if ($source -match '\[ExcludeFromCodeCoverage') {
            return $false
        }
    }

    return $true
}

function Get-CoverageTestProjects {
    param(
        [string]$RepositoryRoot,
        [string[]]$RelativePaths
    )

    $projects = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $RelativePaths) {
        $normalized = $relativePath.Replace('\', '/')
        if ($normalized -like 'src/SrvSurvey.Core/*') {
            [void]$projects.Add((Join-Path $RepositoryRoot "tests/SrvSurvey.Core.Tests/SrvSurvey.Core.Tests.csproj"))
        }
        elseif ($normalized -like 'src/SrvSurvey.Desktop/*') {
            [void]$projects.Add((Join-Path $RepositoryRoot "tests/SrvSurvey.Desktop.Tests/SrvSurvey.Desktop.Tests.csproj"))
        }
        elseif ($normalized -like 'src/SrvSurvey.ReplayController/*') {
            [void]$projects.Add(
                (Join-Path $RepositoryRoot "tests/SrvSurvey.ReplayController.Tests/SrvSurvey.ReplayController.Tests.csproj")
            )
        }
    }

    return @($projects)
}

function Invoke-ChangedCoverageCheck {
    param(
        [string]$RepositoryRoot,
        [hashtable]$ChangedRanges,
        [int]$MinimumPercent = 80
    )

    $coverable = @{}
    foreach ($relativePath in $ChangedRanges.Keys) {
        if (Test-IsCoverableProductionPath -RepositoryRoot $RepositoryRoot -RelativePath $relativePath) {
            $coverable[$relativePath.Replace('\', '/')] = $ChangedRanges[$relativePath]
        }
    }

    if ($coverable.Count -eq 0) {
        Write-Output "No coverable changed production C# to measure against the 80% new-code coverage gate."
        return
    }

    $testProjects = Get-CoverageTestProjects -RepositoryRoot $RepositoryRoot -RelativePaths @($coverable.Keys)
    if ($testProjects.Count -eq 0) {
        throw "Changed coverable production files have no mapped test project for the new-code coverage gate."
    }

    $resultsDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("srvsurvey-coverage-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $resultsDirectory | Out-Null
    try {
        foreach ($project in $testProjects) {
            & dotnet test $project `
                --configuration Release `
                --no-build `
                --no-restore `
                --collect:"XPlat Code Coverage" `
                --results-directory $resultsDirectory `
                -- `
                DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
            if ($LASTEXITCODE -ne 0) {
                throw "Coverage test run failed for $project."
            }
        }

        $reports = Get-ChildItem -Path $resultsDirectory -Filter "coverage.opencover.xml" -Recurse
        if ($reports.Count -eq 0) {
            throw "Coverlet did not produce an OpenCover report for the new-code coverage gate."
        }

        $points = @{}
        foreach ($report in $reports) {
            [xml]$document = Get-Content -LiteralPath $report.FullName
            foreach ($file in @($document.SelectNodes("//*[local-name()='File'][@fullPath]"))) {
                $relativePath = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.fullPath).Replace('\', '/')
                if (-not $coverable.ContainsKey($relativePath)) {
                    continue
                }

                $uid = $file.uid
                $methods = @(
                    $document.SelectNodes(
                        "//*[local-name()='Method'][*[local-name()='FileRef' and @uid='$uid']]"
                    )
                )
                foreach ($method in $methods) {
                    foreach ($sequence in @($method.SelectNodes(".//*[local-name()='SequencePoint']"))) {
                        $lineNumber = [int]$sequence.sl
                        if ($lineNumber -lt 1) {
                            continue
                        }

                        $isChangedLine = $coverable[$relativePath] | Where-Object {
                            $lineNumber -ge $_.Start -and $lineNumber -le $_.End
                        }
                        if (-not $isChangedLine) {
                            continue
                        }

                        $key = "$relativePath|line|$lineNumber"
                        $visited = [int]$sequence.vc -gt 0
                        if (-not $points.ContainsKey($key)) {
                            $points[$key] = $visited
                        }
                        elseif ($visited) {
                            $points[$key] = $true
                        }
                    }

                    foreach ($branch in @($method.SelectNodes(".//*[local-name()='BranchPoint']"))) {
                        $lineNumber = [int]$branch.sl
                        if ($lineNumber -lt 1) {
                            continue
                        }

                        $isChangedLine = $coverable[$relativePath] | Where-Object {
                            $lineNumber -ge $_.Start -and $lineNumber -le $_.End
                        }
                        if (-not $isChangedLine) {
                            continue
                        }

                        $key = "$relativePath|branch|$lineNumber|$($branch.offset)|$($branch.path)"
                        $visited = [int]$branch.vc -gt 0
                        if (-not $points.ContainsKey($key)) {
                            $points[$key] = $visited
                        }
                        elseif ($visited) {
                            $points[$key] = $true
                        }
                    }
                }
            }
        }

        if ($points.Count -eq 0) {
            Write-Output "Changed coverable lines have no executable coverage points; skipping the numeric gate."
            return
        }

        $covered = @($points.Values | Where-Object { $_ }).Count
        $rawPercent = 100.0 * $covered / $points.Count
        $percent = [math]::Round($rawPercent, 1)
        Write-Output "New-code coverage on changed production lines: $percent% ($covered/$($points.Count)); minimum is $MinimumPercent%."
        if ($rawPercent -lt $MinimumPercent) {
            $uncovered = $points.GetEnumerator() |
                Where-Object { -not $_.Value } |
                ForEach-Object { $_.Key } |
                Sort-Object
            foreach ($key in $uncovered) {
                [Console]::Error.WriteLine("Uncovered new code: $key")
            }

            throw "New-code coverage $percent% is below the SonarCloud $MinimumPercent% gate."
        }
    }
    finally {
        if (Test-Path -LiteralPath $resultsDirectory) {
            Remove-Item -LiteralPath $resultsDirectory -Recurse -Force
        }
    }
}

$repositoryRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
$mergeBase = (Invoke-Git merge-base $BaseRef HEAD | Select-Object -First 1).Trim()
$changedRanges = @{}
$currentFile = $null

Push-Location $repositoryRoot
try {
    # Match the CI pre-build gates that fail independently of changed-line Sonar scope.
    Invoke-CSharpierCheck
    Invoke-LocalizationCatalogVerify -RepositoryRoot $repositoryRoot

    $diffLines = Invoke-Git -c core.quotepath=false diff --unified=0 --no-color $mergeBase -- "*.cs"
    foreach ($line in $diffLines) {
        if ($line -match '^\+\+\+ b/(.+)$') {
            $currentFile = $Matches[1].Replace('\', '/')
            if (-not $changedRanges.ContainsKey($currentFile)) {
                $changedRanges[$currentFile] = [System.Collections.Generic.List[object]]::new()
            }
            continue
        }

        if ($null -eq $currentFile -or $line -notmatch '^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@') {
            continue
        }

        $start = [int]$Matches[1]
        $count = if ($Matches[2]) { [int]$Matches[2] } else { 1 }
        if ($count -gt 0) {
            $changedRanges[$currentFile].Add([pscustomobject]@{ Start = $start; End = $start + $count - 1 })
        }
    }

    if ($changedRanges.Count -eq 0) {
        Write-Output "No changed C# lines to check."
        Invoke-LocalizationCatalogTests -RepositoryRoot $repositoryRoot
        exit 0
    }

    $runId = [guid]::NewGuid().ToString("N")
    $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) "srvsurvey-style-$runId.json"
    $formatLogPath = Join-Path ([System.IO.Path]::GetTempPath()) "srvsurvey-style-$runId.log"
    $sonarLogPath = Join-Path ([System.IO.Path]::GetTempPath()) "srvsurvey-sonar-$runId.log"

    try {
        & dotnet format $Solution style `
            --diagnostics IDE0007 IDE0008 IDE0031 `
            --severity info `
            --no-restore `
            --verify-no-changes `
            --report $reportPath `
            --verbosity quiet *> $formatLogPath

        if (-not (Test-Path -LiteralPath $reportPath)) {
            Get-Content -LiteralPath $formatLogPath -Tail 40
            throw "dotnet format did not create its diagnostic report."
        }

        $formatFindings = foreach ($document in Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json) {
            $relativePath = [System.IO.Path]::GetRelativePath($repositoryRoot, $document.FilePath).Replace('\', '/')
            if (-not $changedRanges.ContainsKey($relativePath)) {
                continue
            }

            foreach ($change in $document.FileChanges) {
                $isChangedLine = $changedRanges[$relativePath] | Where-Object {
                    $change.LineNumber -ge $_.Start -and $change.LineNumber -le $_.End
                }
                if ($isChangedLine) {
                    [pscustomobject]@{
                        File = $relativePath
                        Line = $change.LineNumber
                        Column = $change.CharNumber
                        Rule = $change.DiagnosticId
                        Message = $change.FormatDescription
                    }
                }
            }
        }

        $ruleSetPath = Join-Path $repositoryRoot "tools/SonarCloud.ruleset"
        & dotnet build-server shutdown *> $null
        & dotnet build $Solution `
            --configuration Release `
            --no-restore `
            -t:Rebuild `
            "-p:TreatWarningsAsErrors=false" `
            "-p:WarningsAsErrors=" `
            "-p:CodeAnalysisRuleSet=$ruleSetPath" `
            "-clp:NoSummary" *> $sonarLogPath
        if ($LASTEXITCODE -ne 0) {
            Get-Content -LiteralPath $sonarLogPath -Tail 60
            throw "The local SonarCloud-profile build failed."
        }

        $sonarFindings = @{}
        foreach ($line in Get-Content -LiteralPath $sonarLogPath) {
            $normalized = $line -replace '\x1B\[[0-9;]*m', ''
            if (
                $normalized -notmatch
                '(?<Path>.+?)\((?<Line>\d+),(?<Column>\d+)\): warning (?<Rule>(?:S|CA|IDE|SYSLIB)\d+): (?<Message>.+?)(?: \[|$)'
            ) {
                continue
            }

            $absolutePath = $Matches.Path
            if (-not [System.IO.Path]::IsPathRooted($absolutePath)) {
                $absolutePath = Join-Path $repositoryRoot $absolutePath
            }

            $relativePath = [System.IO.Path]::GetRelativePath($repositoryRoot, $absolutePath).Replace('\', '/')
            $lineNumber = [int]$Matches.Line
            if (-not $changedRanges.ContainsKey($relativePath)) {
                continue
            }

            $isChangedLine = $changedRanges[$relativePath] | Where-Object {
                $lineNumber -ge $_.Start -and $lineNumber -le $_.End
            }
            if ($isChangedLine) {
                $key = "$relativePath|$lineNumber|$($Matches.Column)|$($Matches.Rule)"
                $sonarFindings[$key] = [pscustomobject]@{
                    File = $relativePath
                    Line = $lineNumber
                    Column = [int]$Matches.Column
                    Rule = $Matches.Rule
                    Message = $Matches.Message.Trim()
                }
            }
        }

        $nullConditionalEventFindings = Find-NullConditionalEventFindings $repositoryRoot $changedRanges
        $findings =
            @($formatFindings) + @($sonarFindings.Values) + @($nullConditionalEventFindings)
        if ($findings.Count -gt 0) {
            $findings |
                Sort-Object File, Line, Column |
                ForEach-Object { "{0}({1},{2}): {3} {4}" -f $_.File, $_.Line, $_.Column, $_.Rule, $_.Message }
            [Console]::Error.WriteLine(
                "Changed C# lines contain $($findings.Count) local style or SonarCloud-profile violation(s)."
            )
            exit 1
        }

        Write-Output "Changed C# lines match the .editorconfig style and local SonarCloud profile."
        Invoke-ChangedCoverageCheck -RepositoryRoot $repositoryRoot -ChangedRanges $changedRanges
        Invoke-LocalizationCatalogTests -RepositoryRoot $repositoryRoot
    }
    finally {
        if (Test-Path -LiteralPath $reportPath) {
            Remove-Item -LiteralPath $reportPath -Force
        }
        if (Test-Path -LiteralPath $formatLogPath) {
            Remove-Item -LiteralPath $formatLogPath -Force
        }
        if (Test-Path -LiteralPath $sonarLogPath) {
            Remove-Item -LiteralPath $sonarLogPath -Force
        }
    }
}
finally {
    Pop-Location
}
