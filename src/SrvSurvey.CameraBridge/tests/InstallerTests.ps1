#requires -Version 7.2
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot '..' 'Install-CameraBridge.ps1'
$fixtures = [Collections.Generic.List[string]]::new()
$global:CameraInstallerTestProcesses = @()
$global:CameraInstallerConflictPath = $null
$global:CameraInstallerConflictReads = 0
$global:CameraInstallerEditConfig = $false
$global:CameraInstallerEditAt = 3

# Process and filesystem boundaries are replaced only in this test process.
# Production has no skip/force-process-check option.
function global:Get-Process { param([string] $Name, [string] $ErrorAction) $global:CameraInstallerTestProcesses }
function global:Get-Item {
    [CmdletBinding()]
    param([string] $LiteralPath, [switch] $Force)
    if ($global:CameraInstallerConflictPath -and $LiteralPath -ceq $global:CameraInstallerConflictPath) {
        $global:CameraInstallerConflictReads++
        if ($global:CameraInstallerEditConfig -and $global:CameraInstallerConflictReads -eq $global:CameraInstallerEditAt) {
            $reference = if ($global:CameraInstallerEditAt -eq 2) { "proxy_d3d11=SrvSurveyCameraBridge.dll`r`n" } else { '' }
            [IO.File]::AppendAllText($LiteralPath, "`r`n; external edit preserved`r`n$reference")
        }
        elseif (-not $global:CameraInstallerEditConfig -and $global:CameraInstallerConflictReads -eq 2) {
            [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetDirectoryName($LiteralPath)) '.srvsurvey-camera-bridge-install.json'), 'foreign-file')
        }
    }
    Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force:$Force -ErrorAction $ErrorActionPreference
}

function Require([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Equal-Bytes([byte[]] $Left, [byte[]] $Right) {
    [Convert]::ToHexString($Left) -ceq [Convert]::ToHexString($Right)
}
function Expect-Failure([scriptblock] $Action, [string] $Expected) {
    try { $null = & $Action }
    catch {
        Require ($_.Exception.Message -like "*$Expected*") "Expected '$Expected', received '$($_.Exception.Message)'."
        return
    }
    throw "Expected refusal containing '$Expected'."
}
function New-Fixture([byte[]] $Config) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("SrvSurveyCameraInstallerTests-$([Guid]::NewGuid().ToString('N'))")
    $fixtures.Add($root)
    $game = Join-Path $root 'game'
    $package = Join-Path $root 'package'
    $null = [IO.Directory]::CreateDirectory($game)
    $null = [IO.Directory]::CreateDirectory($package)
    $dll = [byte[]]::new(128)
    $dll[0] = 0x4d; $dll[1] = 0x5a; $dll[60] = 64; $dll[64] = 0x50; $dll[65] = 0x45
    $dll[68] = 0x64; $dll[69] = 0x86; $dll[87] = 0x20
    [IO.File]::WriteAllBytes((Join-Path $package 'SrvSurveyCameraBridge.dll'), $dll)
    [IO.File]::WriteAllText((Join-Path $package 'SrvSurveyCameraBridge.ini'), "[Capture]`r`nValidated=0`r`n")
    [IO.File]::WriteAllBytes((Join-Path $game 'd3dx.ini'), $Config)
    @{ Root = $root; Game = $game; Package = $package; Config = Join-Path $game 'd3dx.ini'; Manifest = Join-Path $game '.srvsurvey-camera-bridge-install.json' }
}

try {
    $encodings = @(
        @{ Encoding = [Text.UTF8Encoding]::new($false); Newline = "`r`n" },
        @{ Encoding = [Text.UTF8Encoding]::new($true); Newline = "`n" },
        @{ Encoding = [Text.UnicodeEncoding]::new($false, $true); Newline = "`r`n" },
        @{ Encoding = [Text.UnicodeEncoding]::new($true, $true); Newline = "`r`n" },
        @{ Encoding = [Text.Encoding]::Latin1; Newline = "`r`n" }
    )
    foreach ($case in $encodings) {
        $nl = $case.Newline
        $originalText = "; café note$nl[System]$nl load_library_redirect = 2 ; keep$nl[Other]$nl x=1$nl"
        $original = [byte[]]($case.Encoding.GetPreamble() + $case.Encoding.GetBytes($originalText))
        $fixture = New-Fixture $original
        $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package
        Require ($installed.State -eq 'Installed') 'Install did not succeed.'
        $expectedText = "; café note$nl[System]${nl}proxy_d3d11=SrvSurveyCameraBridge.dll$nl load_library_redirect = 0 ; keep$nl[Other]$nl x=1$nl"
        $expected = [byte[]]($case.Encoding.GetPreamble() + $case.Encoding.GetBytes($expectedText))
        Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Config)) $expected) 'Install changed encoding, line endings, comments or unrelated options.'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes((Join-Path $installed.BackupDirectory 'd3dx.ini'))) $original) 'Original backup is not byte-identical.'
        $again = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package
        Require ($again.State -eq 'AlreadyInstalled' -and $again.BackupDirectory -ceq $installed.BackupDirectory) 'Repeated install was not idempotent.'
        $removed = & $installer -GameDirectory $fixture.Game -Uninstall
        Require ($removed.State -eq 'Uninstalled') 'Uninstall did not succeed.'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Config)) $original) 'Uninstall did not restore exact original bytes.'
        Require (-not (Test-Path -LiteralPath $fixture.Manifest)) 'Uninstall left its active manifest.'
        Require (-not (Test-Path -LiteralPath (Join-Path $fixture.Game 'SrvSurveyCameraBridge.dll'))) 'Uninstall left its DLL.'
        Require ((& $installer -GameDirectory $fixture.Game -Uninstall).State -eq 'NotInstalled') 'Repeated uninstall was not idempotent.'
    }

    $simple = [Text.Encoding]::UTF8.GetBytes("[System]`r`nload_library_redirect=2`r`n")
    foreach ($target in @('d3dx.ini', 'SrvSurveyCameraBridge.dll', 'SrvSurveyCameraBridge.ini')) {
        $fixture = New-Fixture $simple
        $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package
        $changed = Join-Path $fixture.Game $target
        [IO.File]::AppendAllText($changed, 'user-edit')
        $before = [IO.File]::ReadAllBytes($changed)
        $configBefore = [IO.File]::ReadAllBytes($fixture.Config)
        $manifestBefore = [IO.File]::ReadAllBytes($fixture.Manifest)
        Expect-Failure { & $installer -GameDirectory $fixture.Game -Uninstall } 'Files were left untouched'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes($changed)) $before) 'Refused uninstall changed the edited file.'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Config)) $configBefore) 'Refused uninstall changed the configuration.'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Manifest)) $manifestBefore) 'Refused uninstall changed its manifest.'
    }

    foreach ($text in @("[System]`r`nproxy_d3d11=another-proxy.dll`r`n", "[System]`r`nproxy_d3d11=SrvSurveyCameraBridge.dll`r`n", "[System]`r`n[System]`r`n")) {
        $bytes = [Text.Encoding]::UTF8.GetBytes($text)
        $fixture = New-Fixture $bytes
        Expect-Failure { & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package } 'No files were changed'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Config)) $bytes) 'A refused configuration was changed.'
        Require (@(Get-ChildItem -LiteralPath $fixture.Game).Count -eq 1) 'Refusal created backup or bridge files.'
    }

    $fixture = New-Fixture $simple
    [IO.File]::WriteAllText((Join-Path $fixture.Game 'SrvSurveyCameraBridge.dll'), 'unknown-DLL')
    Expect-Failure { & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package } 'not owned by this installer'
    Require ([IO.File]::ReadAllText((Join-Path $fixture.Game 'SrvSurveyCameraBridge.dll')) -ceq 'unknown-DLL') 'An unknown DLL was overwritten.'

    $fixture = New-Fixture $simple
    $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package
    [IO.File]::AppendAllText((Join-Path $fixture.Package 'SrvSurveyCameraBridge.dll'), 'new-build')
    Expect-Failure { & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package } 'before upgrading'
    Require (Test-Path -LiteralPath $fixture.Manifest) 'Refused upgrade removed the installed manifest.'

    $fixture = New-Fixture $simple
    $global:CameraInstallerTestProcesses = @([pscustomobject]@{ ProcessName = 'EliteDangerous64' })
    Expect-Failure { & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package } 'Exit Elite'
    Require (@(Get-ChildItem -LiteralPath $fixture.Game).Count -eq 1) 'Running-game refusal changed the directory.'
    $global:CameraInstallerTestProcesses = @()

    $fixture = New-Fixture $simple
    $global:CameraInstallerConflictPath = $fixture.Config
    $global:CameraInstallerConflictReads = 0
    Expect-Failure { & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package } 'backup preserved'
    $global:CameraInstallerConflictPath = $null
    Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Config)) $simple) 'Failed installation did not restore the original configuration.'
    Require (-not (Test-Path -LiteralPath (Join-Path $fixture.Game 'SrvSurveyCameraBridge.dll'))) 'Failed installation left its copied DLL.'
    Require ([IO.File]::ReadAllText($fixture.Manifest) -ceq 'foreign-file') 'Rollback removed an unrelated conflicting file.'

    $fixture = New-Fixture $simple
    $selected = Join-Path $fixture.Package 'experimental.ini'
    [IO.File]::WriteAllText($selected, "[Capture]`nValidated=1`n")
    $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package -CaptureProfile $selected
    Require (Equal-Bytes ([IO.File]::ReadAllBytes((Join-Path $fixture.Game 'SrvSurveyCameraBridge.ini'))) ([IO.File]::ReadAllBytes($selected))) 'Selected experimental profile was not copied exactly.'
    $null = & $installer -GameDirectory $fixture.Game -Uninstall

    $fixture = New-Fixture $simple
    $cache = if ($IsWindows) { 'C:\SrvSurvey profilés\profile.ini' } else { '/tmp/SrvSurvey profilés/profile.ini' }
    $expectedCache = if ($IsWindows) { 'C:\SrvSurvey profilés\profile.ini' } else { 'Z:\tmp\SrvSurvey profilés\profile.ini' }
    $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package -ProfileCachePath $cache
    $locator = Join-Path $fixture.Game 'SrvSurveyCameraBridge.paths.ini'
    $locatorBytes = [IO.File]::ReadAllBytes($locator)
    Require ($locatorBytes[0] -eq 0xff -and $locatorBytes[1] -eq 0xfe) 'Cache locator requires a UTF16 BOM for Windows INI Unicode paths.'
    Require ([IO.File]::ReadAllText($locator) -ceq "[Updates]`r`nCachedProfilePath=$expectedCache`r`n") 'Cache locator did not preserve the explicit machine-local path.'
    Require ((& $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package -ProfileCachePath $cache).State -eq 'AlreadyInstalled') 'Cache-aware install was not idempotent.'
    $null = & $installer -GameDirectory $fixture.Game -Uninstall
    Require (-not (Test-Path -LiteralPath $locator)) 'Uninstall left the cache locator behind.'

    $fixture = New-Fixture $simple
    $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package
    [IO.File]::AppendAllText((Join-Path $fixture.Game 'SrvSurveyCameraBridge.paths.ini'), 'changed')
    Expect-Failure { & $installer -GameDirectory $fixture.Game -Uninstall } 'Files were left untouched'
    Require (Test-Path -LiteralPath $fixture.Manifest) 'A modified locator bypassed uninstall ownership checks.'

    $fixture = New-Fixture $simple
    $installed = & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package
    # Recreate the previous two-file receipt to exercise removal of pre-refresh installations.
    $legacy = [IO.File]::ReadAllText($fixture.Manifest) | ConvertFrom-Json -AsHashtable
    $legacy.Version = 1
    $legacy.Files = @($legacy.Files | Where-Object Name -ne 'SrvSurveyCameraBridge.paths.ini')
    [IO.File]::WriteAllText($fixture.Manifest, ($legacy | ConvertTo-Json -Depth 5))
    [IO.File]::Delete((Join-Path $fixture.Game 'SrvSurveyCameraBridge.paths.ini'))
    Require ((& $installer -GameDirectory $fixture.Game -Uninstall).State -eq 'Uninstalled') 'Legacy two-file installation could not be removed.'
    Require (Equal-Bytes ([IO.File]::ReadAllBytes($fixture.Config)) $simple) 'Legacy uninstall did not restore exact original configuration.'

    $fixture = New-Fixture $simple
    $selected = Join-Path $fixture.Package 'experimental.ini'
    [IO.File]::WriteAllText($selected, "[Capture]`nValidated=1`n")
    $processDirectory = [Environment]::CurrentDirectory
    Push-Location $fixture.Package
    try {
        [Environment]::CurrentDirectory = $fixture.Root
        $null = & $installer -GameDirectory '../game' -BridgeDirectory '.' -CaptureProfile './experimental.ini'
        Require (Equal-Bytes ([IO.File]::ReadAllBytes((Join-Path $fixture.Game 'SrvSurveyCameraBridge.ini'))) ([IO.File]::ReadAllBytes($selected))) 'Relative profile did not resolve against the PowerShell location.'
        $null = & $installer -GameDirectory '../game' -Uninstall
    }
    finally { [Environment]::CurrentDirectory = $processDirectory; Pop-Location }

    foreach ($editAt in @(2, 3)) {
        $global:CameraInstallerEditAt = $editAt
        $fixture = New-Fixture $simple
        $global:CameraInstallerConflictPath = $fixture.Config
        $global:CameraInstallerConflictReads = 0
        $global:CameraInstallerEditConfig = $true
        try {
            Expect-Failure { & $installer -GameDirectory $fixture.Game -BridgeDirectory $fixture.Package } 'backup preserved'
        }
        finally { $global:CameraInstallerConflictPath = $null; $global:CameraInstallerEditConfig = $false }
        Require ([IO.File]::ReadAllText($fixture.Config).Contains('external edit preserved')) 'An external configuration edit was overwritten.'
        Require ([IO.File]::ReadAllText($fixture.Config).Contains('proxy_d3d11=SrvSurveyCameraBridge.dll')) 'The reproduction did not retain the bridge reference.'
        foreach ($name in @('SrvSurveyCameraBridge.dll', 'SrvSurveyCameraBridge.ini', 'SrvSurveyCameraBridge.paths.ini', '.srvsurvey-camera-bridge-install.json')) {
            Require ([IO.File]::Exists((Join-Path $fixture.Game $name))) "Rollback removed referenced or recovery file: $name"
        }
    }

    Write-Output 'Camera installer: encoding round-trips, idempotence, ownership/refusal, running-game guard, rollback and explicit profile selection passed.'
}
finally {
    $global:CameraInstallerTestProcesses = @()
    $global:CameraInstallerConflictPath = $null
    foreach ($root in $fixtures) { if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force } }
    Remove-Item Function:\Get-Process, Function:\Get-Item
    Remove-Variable CameraInstallerTestProcesses, CameraInstallerConflictPath, CameraInstallerConflictReads, CameraInstallerEditConfig, CameraInstallerEditAt -Scope Global
}
