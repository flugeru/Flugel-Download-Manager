$ErrorActionPreference = 'Stop'
$root = 'C:\Program Files (x86)\Internet Download Manager'
$installer = 'C:\Users\Flugel\Downloads\idman643build18.exe'
$records = foreach ($f in @((Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Extension -in '.exe','.dll','.sys','.cat').FullName) + @($installer) + @((Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'integration-packages') -File -Recurse | Where-Object Extension -in '.exe','.dll','.sys','.cat').FullName)) {
    $v = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($f)
    $s = Get-AuthenticodeSignature -LiteralPath $f
    [pscustomobject]@{path=$f; fileVersion=$v.FileVersion; productVersion=$v.ProductVersion; companyName=$v.CompanyName; fileDescription=$v.FileDescription; originalFilename=$v.OriginalFilename; signatureStatus=[string]$s.Status; signatureStatusMessage=$s.StatusMessage; signatureType=[string]$s.SignatureType; signerSubject=if($s.SignerCertificate){$s.SignerCertificate.Subject}else{$null}; signerIssuer=if($s.SignerCertificate){$s.SignerCertificate.Issuer}else{$null}; signerThumbprint=if($s.SignerCertificate){$s.SignerCertificate.Thumbprint}else{$null}; timestampSigner=if($s.TimeStamperCertificate){$s.TimeStamperCertificate.Subject}else{$null}}
}
@{createdUtc=[DateTime]::UtcNow.ToString('o'); note='Windows Authenticode verification depends on current machine trust and network/cache state. Target executables never launched.'; files=@($records)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'inventory-windows.json') -Encoding UTF8
$hosts = @('Software\Google\Chrome\NativeMessagingHosts\com.tonec.idm','Software\Chromium\NativeMessagingHosts\com.tonec.idm','Software\Microsoft\Edge\NativeMessagingHosts\com.tonec.idm','Software\Mozilla\NativeMessagingHosts\com.tonec.idm')
$ids = @('ngpampappnmepgilojfohadhhmbhlaek','llbjbkhnmlidjebalopleeepgdfgcpec')
$keys = @($hosts)
foreach ($browser in @('Google\Chrome','Microsoft\Edge')) { foreach ($id in $ids) {$keys += "Software\$browser\Extensions\$id"} }
$reg = foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryHive]::LocalMachine)) { foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32,[Microsoft.Win32.RegistryView]::Registry64)) {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive,$view)
    try { foreach ($key in $keys) {
        $k = $base.OpenSubKey($key,$false)
        if ($null -eq $k) { [pscustomobject]@{hive=[string]$hive;view=[string]$view;key=$key;exists=$false};continue }
        try { $values = @{}; foreach ($name in @('','path','version','update_url')) {if ($k.GetValueNames() -contains $name) {$values[$name]=$k.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}}; [pscustomobject]@{hive=[string]$hive;view=[string]$view;key=$key;exists=$true;values=$values} } finally {$k.Dispose()}
    }} finally {$base.Dispose()}
}}
@{scope='Only exact IDM native-host and known extension registration keys, selected non-secret values; no IDM settings/license key or browser profile inspected.';records=@($reg)} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'integration-registry.json') -Encoding UTF8
Write-Output "Version/signature records: $(@($records).Count); scoped registry records: $(@($reg).Count)"
