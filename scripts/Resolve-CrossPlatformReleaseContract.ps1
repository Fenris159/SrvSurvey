[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?(-rc\.[1-9]\d*(\.(0|[1-9]\d*))?)?$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

# This tag is a permanent compatibility anchor for clients through rc.50.
# Never assign xp-v to a later version: legacy clients select the highest xp-v
# release before reading its index and cannot recover from an incompatible one.
$legacyBridgeVersion = '2.1.3.0-rc.51'
$isLegacyBridge = $Version -ceq $legacyBridgeVersion
$tagPrefix = if ($isLegacyBridge) { 'xp-v' } else { 'xp2-v' }
$schemaVersion = if ($isLegacyBridge) { 1 } else { 2 }

[ordered]@{
    releaseTag = "$tagPrefix$Version"
    tagPrefix = $tagPrefix
    indexSchemaVersion = $schemaVersion
} | ConvertTo-Json -Compress
