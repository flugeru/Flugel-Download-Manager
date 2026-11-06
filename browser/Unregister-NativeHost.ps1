[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-p]{32}$')]
    [string] $ExtensionId,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Chrome', 'Edge')]
    [string] $Browser
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows only.' }
if ($ExtensionId -cnotmatch '^[a-p]{32}$') { throw 'Extension ID must be 32 lowercase letters a-p.' }
$manifestDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StreamHarbor\NativeMessaging'
$manifestPath = Join-Path $manifestDirectory "$Browser-$ExtensionId.json"
$vendor = if ($Browser -eq 'Chrome') { 'Google\Chrome' } else { 'Microsoft\Edge' }
$keyPath = "Software\$vendor\NativeMessagingHosts\org.streamharbor.bridge"
$origin = "chrome-extension://$ExtensionId/"
$views = @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)

# Check both views before deleting anything; remove only our exact leaf key.
foreach ($view in $views) {
    $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try {
        $key = $root.OpenSubKey($keyPath)
        if ($null -ne $key) {
            try {
                if ($key.GetValue('') -ne $manifestPath -or $key.SubKeyCount -ne 0 -or
                    @($key.GetValueNames() | Where-Object { $_ -ne '' }).Count -ne 0) {
                    throw 'Registration does not match the explicit extension ID, or contains extra data. Nothing removed.'
                }
                $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
                if ($manifest.name -cne 'org.streamharbor.bridge' -or @($manifest.allowed_origins).Count -ne 1 -or
                    $manifest.allowed_origins[0] -cne $origin) {
                    throw 'Manifest ownership check failed. Nothing removed.'
                }
            } finally { $key.Dispose() }
        }
    } finally { $root.Dispose() }
}
if (-not $PSCmdlet.ShouldProcess("HKCU\$keyPath (32/64-bit views)", "Unregister $Browser native host for $ExtensionId")) { return }
foreach ($view in $views) {
    $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try { $root.DeleteSubKey($keyPath, $false) }
    finally { $root.Dispose() }
}
Write-Output "Removed exact $Browser native-host leaf keys from HKCU. Manifest retained: $manifestPath"
