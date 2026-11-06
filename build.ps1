param([switch]$SkipTests)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
function Run-Dotnet([string[]]$Arguments) {
 & dotnet @Arguments
 if($LASTEXITCODE -ne 0){throw "dotnet failed with exit code $LASTEXITCODE"}
}
Run-Dotnet @('build','app/Flugel.Core/Flugel.Core.csproj','-c','Release','--nologo')
Run-Dotnet @('build','app/Flugel.Desktop/Flugel.Desktop.csproj','-c','Release','--nologo')
Run-Dotnet @('build','app/Flugel.NativeHost/Flugel.NativeHost.csproj','-c','Release','--nologo')
if(!$SkipTests){
 Run-Dotnet @('run','--project','app/Flugel.Checks/Flugel.Checks.csproj','-c','Release')
 Run-Dotnet @('run','--project','app/Flugel.NativeHost/Flugel.NativeHost.csproj','-c','Release','--','--self-test')
}
Run-Dotnet @('publish','app/Flugel.Desktop/Flugel.Desktop.csproj','-c','Release','-r','win-x64','--self-contained','false','-o','artifacts/Flugel.Desktop','--nologo')
Run-Dotnet @('publish','app/Flugel.NativeHost/Flugel.NativeHost.csproj','-c','Release','-r','win-x64','--self-contained','false','-o','artifacts/Flugel.NativeHost','--nologo')
New-Item -ItemType Directory -Force artifacts/browser | Out-Null
Copy-Item browser/* artifacts/browser -Recurse -Force
Get-ChildItem artifacts -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
 $hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 "$hash  $($_.FullName.Substring((Join-Path $PSScriptRoot 'artifacts').Length+1))"
} | Set-Content -Encoding UTF8 artifacts/SHA256SUMS.txt
Write-Output 'Build complete: artifacts/Flugel.Desktop/FlugelDownloadManager.exe'
