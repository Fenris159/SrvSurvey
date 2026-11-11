#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $GameDirectory,
    [string] $BridgeDirectory = $PSScriptRoot,
    [string] $CaptureProfile,
    [string] $ProfileCachePath,
    [switch] $Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$legacyOwnedNames = @('SrvSurveyCameraBridge.dll', 'SrvSurveyCameraBridge.ini')
$ownedNames = @($legacyOwnedNames + 'SrvSurveyCameraBridge.paths.ini')
$manifestName = '.srvsurvey-camera-bridge-install.json'

function Get-CameraHash([byte[]] $Bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Get-CameraFileBytes([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or $null -ne $item.LinkTarget) {
        throw "Expected an ordinary file, not a directory or symbolic link: $Path"
    }
    ,([IO.File]::ReadAllBytes($Path))
}

function Get-CameraProfileCachePath {
    if ($ProfileCachePath) {
        $cache = [IO.Path]::GetFullPath($ProfileCachePath)
    }
    elseif ($IsWindows) {
        $cache = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SrvSurvey' 'cache' 'cockpit-camera' 'profile.ini'
    }
    else {
        $root = $env:XDG_CACHE_HOME
        if (-not $root -or -not [IO.Path]::IsPathRooted($root)) {
            $root = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cache'
        }
        $cache = Join-Path $root 'SrvSurvey' 'cockpit-camera' 'profile.ini'
    }
    if ($cache.IndexOfAny([char[]]"`r`n") -ge 0) { throw 'ProfileCachePath must not contain line breaks.' }
    if ($IsWindows) { return $cache }
    # Proton exposes the host filesystem through its standard Z: mapping.
    return 'Z:' + $cache.Replace('/', '\')
}

function Get-CameraSourceBytes([string] $Name) {
    if ($Name -ceq 'SrvSurveyCameraBridge.paths.ini') {
        # Windows' INI APIs require UTF16 with a BOM for non-ANSI user/cache paths.
        $encoding = [Text.UnicodeEncoding]::new($false, $true)
        return ,([byte[]]($encoding.GetPreamble() + $encoding.GetBytes("[Updates]`r`nCachedProfilePath=$(Get-CameraProfileCachePath)`r`n")))
    }
    $source = if ($Name -ceq 'SrvSurveyCameraBridge.ini' -and $CaptureProfile) { $CaptureProfile } else { Join-Path $BridgeDirectory $Name }
    return ,(Get-CameraFileBytes $source)
}

function Write-CameraAtomic([string] $Path, [byte[]] $Bytes, [bool] $Replace) {
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($Bytes); $stream.Flush($true) }
        finally { $stream.Dispose() }
        [IO.File]::Move($temporary, $Path, $Replace)
    }
    finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Get-CameraText([byte[]] $Bytes) {
    $offset = 0
    $preamble = [byte[]]@()
    if ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xef -and $Bytes[1] -eq 0xbb -and $Bytes[2] -eq 0xbf) {
        $encoding = [Text.UTF8Encoding]::new($false, $true)
        $offset = 3
    }
    elseif ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xff -and $Bytes[1] -eq 0xfe) {
        $encoding = [Text.UnicodeEncoding]::new($false, $false, $true)
        $offset = 2
    }
    elseif ($Bytes.Length -ge 2 -and $Bytes[0] -eq 0xfe -and $Bytes[1] -eq 0xff) {
        $encoding = [Text.UnicodeEncoding]::new($true, $false, $true)
        $offset = 2
    }
    else {
        $encoding = [Text.UTF8Encoding]::new($false, $true)
        try { $null = $encoding.GetString($Bytes) }
        catch [Text.DecoderFallbackException] {
            # The edit only inserts ASCII. Latin1 round-trips legacy ANSI bytes
            # without pretending to identify the comments' original code page.
            $encoding = [Text.Encoding]::Latin1
        }
    }
    if ($offset -gt 0) { $preamble = $Bytes[0..($offset - 1)] }
    $text = $encoding.GetString($Bytes, $offset, $Bytes.Length - $offset)
    $roundTrip = [byte[]]($preamble + $encoding.GetBytes($text))
    if ((Get-CameraHash $roundTrip) -ne (Get-CameraHash $Bytes)) {
        throw 'The existing d3dx.ini encoding could not be preserved; no files were changed.'
    }
    @{ Text = $text; Encoding = $encoding; Preamble = $preamble }
}

function Set-CameraSystemOptions([byte[]] $Bytes) {
    $document = Get-CameraText $Bytes
    $text = [string]$document.Text
    $newlineMatch = [regex]::Match($text, '\r\n|\n|\r')
    $newline = if ($newlineMatch.Success) { $newlineMatch.Value } else { "`r`n" }
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($text, '[^\r\n]*(?:\r\n|\n|\r|$)')) {
        if ($match.Length -gt 0) { $lines.Add($match.Value) }
    }
    $systemIndices = [Collections.Generic.List[int]]::new()
    $sectionPattern = '^\s*\[\s*(?<name>[^\]]+?)\s*\]\s*(?:[;#].*)?$'
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index].TrimEnd("`r", "`n")
        $section = [regex]::Match($line, $sectionPattern)
        if ($section.Success -and $section.Groups['name'].Value.Trim() -ieq 'System') { $systemIndices.Add($index) }
    }
    if ($systemIndices.Count -gt 1) { throw 'Multiple [System] sections make d3dx.ini ambiguous; no files were changed.' }
    if ($systemIndices.Count -eq 0) {
        if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -notmatch '[\r\n]$') { $lines[$lines.Count - 1] += $newline }
        $lines.Add("[System]$newline")
        $systemIndices.Add($lines.Count - 1)
    }
    $start = $systemIndices[0]
    $end = $lines.Count
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        if ([regex]::IsMatch($lines[$index].TrimEnd("`r", "`n"), $sectionPattern)) { $end = $index; break }
    }
    $missing = [Collections.Generic.List[string]]::new()
    foreach ($entry in @(@{ Key = 'proxy_d3d11'; Value = 'SrvSurveyCameraBridge.dll' }, @{ Key = 'load_library_redirect'; Value = '0' })) {
        $found = [Collections.Generic.List[int]]::new()
        $pattern = '^(?<prefix>[ \t]*' + [regex]::Escape($entry.Key) + '[ \t]*=[ \t]*)(?<value>[^;#]*?)(?<suffix>[ \t]*(?:[;#].*)?)$'
        for ($index = $start + 1; $index -lt $end; $index++) {
            if ([regex]::IsMatch($lines[$index].TrimEnd("`r", "`n"), $pattern, 'IgnoreCase')) { $found.Add($index) }
        }
        if ($found.Count -gt 1) { throw "Duplicate '$($entry.Key)' options make d3dx.ini ambiguous; no files were changed." }
        if ($found.Count -eq 0) { $missing.Add("$($entry.Key)=$($entry.Value)$newline"); continue }
        $index = $found[0]
        $line = $lines[$index].TrimEnd("`r", "`n")
        $terminator = $lines[$index].Substring($line.Length)
        $match = [regex]::Match($line, $pattern, 'IgnoreCase')
        $existing = $match.Groups['value'].Value.Trim()
        if ($entry.Key -eq 'proxy_d3d11' -and $existing.Length -gt 0) {
            throw "An existing proxy_d3d11='$existing' is not owned by this installer. No files were changed."
        }
        if ($entry.Key -eq 'load_library_redirect' -and $existing -notmatch '^[012]$') {
            throw "The existing load_library_redirect value is unsupported. No files were changed."
        }
        $lines[$index] = $match.Groups['prefix'].Value + $entry.Value + $match.Groups['suffix'].Value + $terminator
    }
    if ($missing.Count -gt 0) {
        if ($lines[$start] -notmatch '[\r\n]$') { $lines[$start] += $newline }
        $lines.InsertRange($start + 1, $missing)
    }
    $updated = [string]::Concat($lines)
    ,([byte[]]($document.Preamble + $document.Encoding.GetBytes($updated)))
}

function Get-CameraManifest([string] $Path, [string] $Directory) {
    $bytes = Get-CameraFileBytes $Path
    $manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json -AsHashtable
    foreach ($key in @('Version', 'GameDirectory', 'BackupDirectory', 'OriginalConfigSha256', 'InstalledConfigSha256', 'Files')) {
        if (-not $manifest.ContainsKey($key)) { throw "The installation manifest is incomplete: $Path" }
    }
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ($manifest.Version -notin @(1, 2) -or -not [string]::Equals([IO.Path]::GetFullPath($manifest.GameDirectory), $Directory, $comparison)) {
        throw "The installation manifest does not identify this game directory: $Path"
    }
    $backup = [IO.Path]::GetFullPath($manifest.BackupDirectory)
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($backup), $Directory, $comparison) -or
        [IO.Path]::GetFileName($backup) -notmatch '^SrvSurveyCameraBridge\.backup-\d{8}T\d{6}Z-[0-9a-f]{32}$') {
        throw "The manifest's backup directory is outside its expected scope: $Path"
    }
    $expectedNames = if ($manifest.Version -eq 1) { $legacyOwnedNames } else { $ownedNames }
    if (@($manifest.Files).Count -ne $expectedNames.Count -or @($manifest.Files.Name | Sort-Object -Unique).Count -ne $expectedNames.Count) {
        throw "The installation manifest has an unexpected file inventory: $Path"
    }
    foreach ($file in $manifest.Files) {
        if ($file.Name -cnotin $expectedNames -or $file.Sha256 -notmatch '^[a-f0-9]{64}$') {
            throw "The installation manifest has an unexpected file entry: $Path"
        }
    }
    if ($manifest.OriginalConfigSha256 -notmatch '^[a-f0-9]{64}$' -or $manifest.InstalledConfigSha256 -notmatch '^[a-f0-9]{64}$') {
        throw "The installation manifest has an invalid configuration hash: $Path"
    }
    @{ Document = $manifest; Bytes = $bytes }
}

function Assert-CameraInstallation([hashtable] $Manifest, [string] $Directory) {
    $backupPath = Join-Path $Manifest.BackupDirectory 'd3dx.ini'
    try {
        if ((Get-CameraHash (Get-CameraFileBytes (Join-Path $Directory 'd3dx.ini'))) -ne $Manifest.InstalledConfigSha256) {
            throw 'd3dx.ini has changed since installation'
        }
        foreach ($file in $Manifest.Files) {
            if ((Get-CameraHash (Get-CameraFileBytes (Join-Path $Directory $file.Name))) -ne $file.Sha256) {
                throw "$($file.Name) has changed since installation"
            }
        }
        if ((Get-CameraHash (Get-CameraFileBytes $backupPath)) -ne $Manifest.OriginalConfigSha256) {
            throw 'the original configuration backup does not match its recorded hash'
        }
    }
    catch { throw "Cannot change this installation: $($_.Exception.Message). Files were left untouched. Backup: $backupPath" }
}

$running = @(Get-Process -Name 'EliteDangerous*' -ErrorAction SilentlyContinue | ForEach-Object ProcessName | Sort-Object -Unique)
if ($running.Count -gt 0) { throw "Exit Elite before changing the camera bridge. Running process names: $($running -join ', ')." }
$gamePath = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath((Resolve-Path -LiteralPath $GameDirectory).Path))
if (-not [IO.Directory]::Exists($gamePath)) { throw 'GameDirectory must be an existing directory.' }
$configPath = Join-Path $gamePath 'd3dx.ini'
$manifestPath = Join-Path $gamePath $manifestName
$mutexIdentity = Get-CameraHash ([Text.Encoding]::UTF8.GetBytes($gamePath.ToUpperInvariant()))
$mutex = [Threading.Mutex]::new($false, "SrvSurveyCameraBridge-$mutexIdentity")
$held = $false
try {
    try { $held = $mutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $held = $true }
    if (-not $held) { throw 'Another camera bridge installation operation is already running.' }
    $existingManifest = Get-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
    if ($existingManifest) {
        $loaded = Get-CameraManifest $manifestPath $gamePath
        $manifest = $loaded.Document
        Assert-CameraInstallation $manifest $gamePath
        if ($Uninstall) {
            $original = Get-CameraFileBytes (Join-Path $manifest.BackupDirectory 'd3dx.ini')
            $installed = Get-CameraFileBytes $configPath
            $installedFiles = @{}
            foreach ($file in $manifest.Files) { $installedFiles[$file.Name] = Get-CameraFileBytes (Join-Path $gamePath $file.Name) }
            $restored = $false
            $removed = [Collections.Generic.List[string]]::new()
            try {
                Write-CameraAtomic $configPath $original $true
                $restored = $true
                foreach ($file in $manifest.Files) {
                    [IO.File]::Delete((Join-Path $gamePath $file.Name))
                    $removed.Add($file.Name)
                }
                [IO.File]::Delete($manifestPath)
            }
            catch {
                $failure = $_.Exception.Message
                foreach ($name in $removed) {
                    $path = Join-Path $gamePath $name
                    if (-not (Test-Path -LiteralPath $path)) { Write-CameraAtomic $path $installedFiles[$name] $false }
                }
                if ($restored -and (Get-CameraHash (Get-CameraFileBytes $configPath)) -eq $manifest.OriginalConfigSha256) {
                    Write-CameraAtomic $configPath $installed $true
                }
                throw "Uninstall could not complete: $failure. Backup preserved: $($manifest.BackupDirectory)"
            }
            [pscustomobject]@{ State = 'Uninstalled'; GameDirectory = $gamePath; BackupDirectory = $manifest.BackupDirectory }
            return
        }
        foreach ($file in $manifest.Files) {
            if ((Get-CameraHash (Get-CameraSourceBytes $file.Name)) -ne $file.Sha256) {
                throw "A different bridge build is already installed. Uninstall that verified installation before upgrading. Backup: $($manifest.BackupDirectory)"
            }
        }
        [pscustomobject]@{ State = 'AlreadyInstalled'; GameDirectory = $gamePath; BackupDirectory = $manifest.BackupDirectory }
        return
    }
    foreach ($name in $ownedNames) {
        if (Test-Path -LiteralPath (Join-Path $gamePath $name)) { throw "An existing $name is not owned by this installer. No files were changed." }
    }
    if ($Uninstall) {
        [pscustomobject]@{ State = 'NotInstalled'; GameDirectory = $gamePath; BackupDirectory = $null }
        return
    }
    $original = Get-CameraFileBytes $configPath
    $updated = Set-CameraSystemOptions $original
    $sourceFiles = @{}
    $files = @()
    foreach ($name in $ownedNames) {
        $bytes = Get-CameraSourceBytes $name
        $sourceFiles[$name] = $bytes
        $files += @{ Name = $name; Sha256 = Get-CameraHash $bytes }
    }
    $dll = [byte[]]$sourceFiles['SrvSurveyCameraBridge.dll']
    if ($dll.Length -lt 64 -or $dll[0] -ne 0x4d -or $dll[1] -ne 0x5a) { throw 'The supplied bridge is not a Windows PE DLL.' }
    $pe = [BitConverter]::ToInt32($dll, 60)
    if ($pe -lt 0 -or $pe + 24 -gt $dll.Length -or [BitConverter]::ToUInt32($dll, $pe) -ne 0x00004550 -or
        [BitConverter]::ToUInt16($dll, $pe + 4) -ne 0x8664 -or ([BitConverter]::ToUInt16($dll, $pe + 22) -band 0x2000) -eq 0) {
        throw 'The supplied bridge must be a Windows x64 DLL.'
    }
    if ($sourceFiles['SrvSurveyCameraBridge.ini'].Length -eq 0) { throw 'The bridge capture profile is empty.' }
    $backupPath = Join-Path $gamePath ("SrvSurveyCameraBridge.backup-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))-$([Guid]::NewGuid().ToString('N'))")
    $manifest = @{
        Version = 2; GameDirectory = $gamePath; BackupDirectory = $backupPath
        OriginalConfigSha256 = Get-CameraHash $original; InstalledConfigSha256 = Get-CameraHash $updated; Files = $files
    }
    $manifestBytes = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 5))
    $created = [Collections.Generic.List[string]]::new()
    $configReplaced = $false
    try {
        $null = [IO.Directory]::CreateDirectory($backupPath)
        Write-CameraAtomic (Join-Path $backupPath 'd3dx.ini') $original $false
        if ((Get-CameraHash (Get-CameraFileBytes (Join-Path $backupPath 'd3dx.ini'))) -ne $manifest.OriginalConfigSha256) {
            throw 'The original configuration backup failed verification.'
        }
        Write-CameraAtomic (Join-Path $backupPath 'install-manifest.json') $manifestBytes $false
        foreach ($file in $files) {
            Write-CameraAtomic (Join-Path $gamePath $file.Name) $sourceFiles[$file.Name] $false
            $created.Add($file.Name)
        }
        if ((Get-CameraHash (Get-CameraFileBytes $configPath)) -ne $manifest.OriginalConfigSha256) {
            throw 'd3dx.ini changed during installation.'
        }
        Write-CameraAtomic $configPath $updated $true
        $configReplaced = $true
        Write-CameraAtomic $manifestPath $manifestBytes $false
        Assert-CameraInstallation $manifest $gamePath
    }
    catch {
        $failure = $_.Exception.Message
        if ($configReplaced -and (Get-CameraHash (Get-CameraFileBytes $configPath)) -eq $manifest.InstalledConfigSha256) {
            Write-CameraAtomic $configPath $original $true
        }
        foreach ($name in $created) {
            $path = Join-Path $gamePath $name
            if ([IO.File]::Exists($path) -and (Get-CameraHash (Get-CameraFileBytes $path)) -eq (Get-CameraHash $sourceFiles[$name])) {
                [IO.File]::Delete($path)
            }
        }
        if ([IO.File]::Exists($manifestPath) -and (Get-CameraHash (Get-CameraFileBytes $manifestPath)) -eq (Get-CameraHash $manifestBytes)) {
            [IO.File]::Delete($manifestPath)
        }
        throw "Installation could not complete: $failure. Original configuration backup preserved: $backupPath"
    }
    [pscustomobject]@{ State = 'Installed'; GameDirectory = $gamePath; BackupDirectory = $backupPath }
}
finally {
    if ($held) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
