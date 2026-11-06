# Real GUI regression. Uses fresh --data-dir; never submits a download or touches default profile.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $HostPath,
    [Parameter(Mandatory = $true)] [string] $DataDirectory
)
$ErrorActionPreference = 'Stop'
$HostPath = (Get-Item -LiteralPath $HostPath).FullName
$DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
if (Test-Path -LiteralPath $DataDirectory) { throw 'DataDirectory must not exist.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$env:HARBOR_CHECK_HOST = $HostPath
$env:HARBOR_CHECK_DATA = $DataDirectory
try {
    @'
const {spawnSync}=require("node:child_process");
const b=Buffer.from(JSON.stringify({version:1,action:"enqueue",url:"http://127.0.0.1:18743/sample.bin"}));
const h=Buffer.alloc(4); h.writeUInt32LE(b.length);
const t=Date.now();
const r=spawnSync(process.env.HARBOR_CHECK_HOST,["--self-test-desktop",process.env.HARBOR_CHECK_DATA],{input:Buffer.concat([h,b]),timeout:15000});
console.log(JSON.stringify({elapsedMs:Date.now()-t,error:r.error?.code,status:r.status,stdoutHex:r.stdout?.toString("hex"),stderr:r.stderr?.toString()}));
if(r.error||r.status!==0)process.exitCode=1;
else {const x=r.stdout;if(x.length<4||x.readUInt32LE(0)!==x.length-4||JSON.parse(x.subarray(4)).status!=="accepted-for-confirmation")process.exitCode=1;}
'@ | node
    $hostCheckExit = $LASTEXITCODE
    $children = @(Get-CimInstance Win32_Process -Filter "Name = 'FlugelDownloadManager.exe'" | Where-Object {
        $_.CommandLine -like '*--data-dir*' -and $_.CommandLine.Contains($DataDirectory) -and
        $_.ExecutablePath -eq (Join-Path ([IO.Directory]::GetParent([IO.Path]::GetDirectoryName($HostPath)).FullName) 'Flugel.Desktop\FlugelDownloadManager.exe')
    })
    if ($children.Count -ne 1) { throw "Expected one exact isolated desktop process; found $($children.Count). Host exit: $hostCheckExit" }
    $childId = [int]$children[0].ProcessId
    Write-Output "Exact isolated desktop PID: $childId; host check exit: $hostCheckExit"
    $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $childId)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $cancel = $null
    do {
        $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
        foreach ($window in $windows) {
            $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))
            foreach ($button in $buttons) {
                if ($button.Current.Name -match '^_?Cancel$') { $cancel = $button; break }
            }
        }
        if ($null -eq $cancel) { Start-Sleep -Milliseconds 100 }
    } while ($null -eq $cancel -and [DateTime]::UtcNow -lt $deadline)
    if ($null -eq $cancel) { throw "No confirmation Cancel button for own PID $childId; leave process for inspection." }
    if ($null -eq (Get-Process -Id $childId -ErrorAction SilentlyContinue)) { throw 'Desktop exited before normal UI close.' }
    Write-Output "PASS real desktop confirmation remains open after host check; cancelling via UIA PID $childId"
    ([System.Windows.Automation.InvokePattern]$cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 300
    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
    foreach ($window in $windows) {
        ([System.Windows.Automation.WindowPattern]$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    }
    $child = Get-Process -Id $childId -ErrorAction SilentlyContinue
    if ($null -ne $child -and -not $child.WaitForExit(15000)) { throw "Own desktop $childId did not close normally." }
    if ($hostCheckExit -ne 0) { throw 'Native host valid-frame regression failed (desktop closed normally).' }
    Write-Output "PASS host framed reply and EOF exit while real desktop remained; desktop cancelled/closed normally; isolated data retained at $DataDirectory"
} finally {
    Remove-Item Env:HARBOR_CHECK_HOST -ErrorAction SilentlyContinue
    Remove-Item Env:HARBOR_CHECK_DATA -ErrorAction SilentlyContinue
}
