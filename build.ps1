param([switch]$SkipTests)
$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
function Run-Dotnet([string[]]$Arguments) {
 & dotnet @Arguments
 if($LASTEXITCODE -ne 0){throw "dotnet failed with exit code $LASTEXITCODE"}
}
Run-Dotnet @('build','app/Harbor.Core/Harbor.Core.csproj','-c','Release','--nologo')
Run-Dotnet @('build','app/Harbor.Desktop/Harbor.Desktop.csproj','-c','Release','--nologo')
Run-Dotnet @('build','app/Harbor.NativeHost/Harbor.NativeHost.csproj','-c','Release','--nologo')
if(!$SkipTests){
 Run-Dotnet @('run','--project','app/Harbor.Checks/Harbor.Checks.csproj','-c','Release')
 Run-Dotnet @('run','--project','app/Harbor.NativeHost/Harbor.NativeHost.csproj','-c','Release','--','--self-test')
}
Run-Dotnet @('publish','app/Harbor.Desktop/Harbor.Desktop.csproj','-c','Release','-r','win-x64','--self-contained','false','-o','artifacts/Harbor.Desktop','--nologo')
Run-Dotnet @('publish','app/Harbor.NativeHost/Harbor.NativeHost.csproj','-c','Release','-r','win-x64','--self-contained','false','-o','artifacts/Harbor.NativeHost','--nologo')
New-Item -ItemType Directory -Force artifacts/browser | Out-Null
Copy-Item browser/* artifacts/browser -Recurse -Force
Get-ChildItem artifacts -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
 $hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
 "$hash  $($_.FullName.Substring((Join-Path $PSScriptRoot 'artifacts').Length+1))"
} | Set-Content -Encoding UTF8 artifacts/SHA256SUMS.txt
Write-Output 'Build complete: artifacts/Harbor.Desktop/StreamHarbor.exe'
