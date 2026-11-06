# Explicit opt-in only. Never run from a build or extension automatically.
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-p]{32}$')]
    [string] $ExtensionId,

    [Parameter(Mandatory = $true)]
    [ValidateSet('Chrome', 'Edge')]
    [string] $Browser,

    [Parameter(Mandatory = $true)]
    [string] $HostPath
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows only.' }
if ($ExtensionId -cnotmatch '^[a-p]{32}$') { throw 'Extension ID must be 32 lowercase letters a-p.' }
$hostFile = Get-Item -LiteralPath $HostPath
if ($hostFile.PSIsContainer -or $hostFile.Name -cne 'Harbor.NativeHost.exe') {
    throw 'HostPath must name the published Harbor.NativeHost.exe.'
}
$desktop = Join-Path $hostFile.Directory.Parent.FullName 'Harbor.Desktop\StreamHarbor.exe'
if (-not (Test-Path -LiteralPath $desktop -PathType Leaf)) {
    throw 'Sibling Harbor.Desktop\StreamHarbor.exe not found. Publish both applications first.'
}
$manifestDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'StreamHarbor\NativeMessaging'
$manifestPath = Join-Path $manifestDirectory "$Browser-$ExtensionId.json"
$vendor = if ($Browser -eq 'Chrome') { 'Google\Chrome' } else { 'Microsoft\Edge' }
$keyPath = "Software\$vendor\NativeMessagingHosts\org.streamharbor.bridge"
$origin = "chrome-extension://$ExtensionId/"
$views = @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)

# Refuse to replace another registration, even another StreamHarbor extension ID.
foreach ($view in $views) {
    $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try {
        $key = $root.OpenSubKey($keyPath)
        if ($null -ne $key) {
            try {
                if ($key.GetValue('') -ne $manifestPath) {
                    throw "Existing $Browser registration differs. Unregister its explicit extension ID first."
                }
            } finally { $key.Dispose() }
        }
    } finally { $root.Dispose() }
}
if (Test-Path -LiteralPath $manifestPath) {
    $existing = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($existing.name -cne 'org.streamharbor.bridge' -or @($existing.allowed_origins).Count -ne 1 -or
        $existing.allowed_origins[0] -cne $origin -or $existing.path -ne $hostFile.FullName) {
        throw 'Existing manifest differs; refusing to overwrite.'
    }
}

if (-not $PSCmdlet.ShouldProcess("HKCU\$keyPath (32/64-bit views) and $manifestPath", "Register $Browser native host for $ExtensionId")) { return }
[IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
$manifest = [ordered]@{
    name = 'org.streamharbor.bridge'
    description = 'StreamHarbor link confirmation bridge'
    path = $hostFile.FullName
    type = 'stdio'
    allowed_origins = @($origin)
}
# JSON serialization escapes spaces, backslashes and quotes; no command interpolation.
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))
foreach ($view in $views) {
    $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
    try {
        $key = $root.CreateSubKey($keyPath)
        try { $key.SetValue('', $manifestPath, [Microsoft.Win32.RegistryValueKind]::String) }
        finally { $key.Dispose() }
    } finally { $root.Dispose() }
}
Write-Output "Registered $Browser for extension $ExtensionId under HKCU only."
