$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
$inventory=Get-Content analysis/inventory-files.json -Raw | ConvertFrom-Json
$forbidden=@{}
foreach($file in $inventory.files){$forbidden[$file.sha256.ToLowerInvariant()]=$file.relativePath}
$checked=0
foreach($file in Get-ChildItem artifacts -Recurse -File){
 $digest=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 if($forbidden.ContainsKey($digest)){throw "Original artifact in release: $($file.FullName) matches $($forbidden[$digest])"}
 $checked++
}
$changed=@()
foreach($file in $inventory.files){
 $digest=(Get-FileHash -LiteralPath $file.path -Algorithm SHA256).Hash.ToLowerInvariant()
 if($digest -ne $file.sha256){$changed+=$file.relativePath}
}
if($changed.Count){throw "Original files changed: $($changed -join ', ')"}
[pscustomobject]@{CheckedReleaseFiles=$checked;OriginalFilesVerified=$inventory.files.Count;OriginalHashesInRelease=0;ChangedOriginalFiles=$changed.Count;Timestamp=(Get-Date -Format o)} | ConvertTo-Json | Tee-Object -FilePath test-output/release-verification.json
