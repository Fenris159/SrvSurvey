param(
    [switch]$Uninstall
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
$hooksPath = ".githooks"
$absoluteHooksPath = Join-Path $repositoryRoot $hooksPath
$preCommitPath = Join-Path $absoluteHooksPath "pre-commit"

if (-not (Test-Path -LiteralPath $preCommitPath)) {
    throw "Missing versioned hook at $preCommitPath."
}

Push-Location $repositoryRoot
try {
    if ($Uninstall) {
        $current = (& git config --local --get core.hooksPath) 2>$null
        if ($current -eq $hooksPath -or $current -eq $absoluteHooksPath) {
            Invoke-Git config --local --unset core.hooksPath
            Write-Output "Removed local core.hooksPath ($hooksPath)."
        }
        else {
            Write-Output "Local core.hooksPath is not set to $hooksPath; nothing to uninstall."
        }

        return
    }

    if ($IsLinux -or $IsMacOS) {
        & chmod +x $preCommitPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to mark $preCommitPath executable."
        }
    }

    Invoke-Git config --local core.hooksPath $hooksPath
    Write-Output "Configured local git hooks path: $hooksPath"
    Write-Output "pre-commit now runs tools/Test-ChangedCodeQuality.ps1 before every commit."
}
finally {
    Pop-Location
}
