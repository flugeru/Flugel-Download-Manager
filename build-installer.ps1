param([string]$Version = '0.2.0')
# Builds the Windows installer locally. Requires Inno Setup 6 (ISCC.exe) and the .NET 10 SDK.
# Output: artifacts/installer/FlugelDownloadManager-Setup-<version>.exe (unsigned).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$iscc = @(
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found. Install it, then rerun.' }

$payload = 'artifacts/payload'
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
foreach ($project in @(
    @{ Path = 'app/Flugel.Desktop/Flugel.Desktop.csproj'; Out = "$payload/Flugel.Desktop" },
    @{ Path = 'app/Flugel.NativeHost/Flugel.NativeHost.csproj'; Out = "$payload/Flugel.NativeHost" })) {
  & dotnet publish $project.Path -c Release -r win-x64 --self-contained true -o $project.Out --nologo
  if ($LASTEXITCODE -ne 0) { throw "publish failed: $($project.Path)" }
}
Remove-Item "$payload/Flugel.Desktop/*.pdb","$payload/Flugel.NativeHost/*.pdb" -Force -ErrorAction SilentlyContinue

& $iscc "/DMyAppVersion=$Version" 'installer/FlugelDownloadManager.iss'
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Get-Item "artifacts/installer/FlugelDownloadManager-Setup-$Version.exe"
$hash = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($setup.Name)" | Set-Content -Encoding UTF8 "artifacts/installer/SHA256SUMS.txt"
Write-Output ("Installer: {0} ({1:N1} MB, sha256 {2}) - unsigned" -f $setup.FullName, ($setup.Length / 1MB), $hash)
