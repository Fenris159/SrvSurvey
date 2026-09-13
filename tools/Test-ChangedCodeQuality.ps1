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

$repositoryRoot = (Invoke-Git rev-parse --show-toplevel | Select-Object -First 1).Trim()
$mergeBase = (Invoke-Git merge-base $BaseRef HEAD | Select-Object -First 1).Trim()
$changedRanges = @{}
$currentFile = $null

Push-Location $repositoryRoot
try {
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
        exit 0
    }

    $runId = [guid]::NewGuid().ToString("N")
    $reportPath = Join-Path ([System.IO.Path]::GetTempPath()) "srvsurvey-style-$runId.json"
    $formatLogPath = Join-Path ([System.IO.Path]::GetTempPath()) "srvsurvey-style-$runId.log"
    $sonarLogPath = Join-Path ([System.IO.Path]::GetTempPath()) "srvsurvey-sonar-$runId.log"

    try {
        & dotnet format $Solution style `
            --diagnostics IDE0007 IDE0008 `
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
            if (
                $line -notmatch
                '^(?<Path>[A-Za-z]:\\.+?)\((?<Line>\d+),(?<Column>\d+)\): warning (?<Rule>S\d+): (?<Message>.+?) \['
            ) {
                continue
            }

            $relativePath = [System.IO.Path]::GetRelativePath($repositoryRoot, $Matches.Path).Replace('\', '/')
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
                    Message = $Matches.Message
                }
            }
        }

        $findings = @($formatFindings) + @($sonarFindings.Values)
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
