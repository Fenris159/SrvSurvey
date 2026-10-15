param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'Register the OpenXR layer on the Windows PC running Elite Dangerous.'
}
if (-not [Environment]::Is64BitProcess) {
    throw 'Use 64-bit PowerShell to register the Windows x64 OpenXR layer.'
}
$manifest = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'SrvSurveyOpenXrLayer.json'))
$baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey('CurrentUser', 'Registry64')
try {
    $key = $baseKey.CreateSubKey('SOFTWARE\Khronos\OpenXR\1\ApiLayers\Implicit')
    try {
        if ($Uninstall) {
            $key.DeleteValue($manifest, $false)
            Write-Output 'SrvSurvey OpenXR layer unregistered. Restart Elite to unload it.'
        }
        else {
            foreach ($file in @($manifest, (Join-Path $PSScriptRoot 'SrvSurveyOpenXrLayer.dll'))) {
                if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing OpenXR layer file: $file" }
            }
            # Retire registrations from older SrvSurvey package folders, leaving other layers alone.
            foreach ($name in $key.GetValueNames()) {
                if ([IO.Path]::GetFileName($name) -eq 'SrvSurveyOpenXrLayer.json') { $key.DeleteValue($name, $false) }
            }
            $key.SetValue($manifest, 0, 'DWord')
            Write-Output 'SrvSurvey OpenXR layer registered for this Windows account. Restart Elite, select VDXR in SrvSurvey, and enable VR overlays.'
        }
    }
    finally { $key.Dispose() }
}
finally { $baseKey.Dispose() }
