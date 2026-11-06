param([string]$Output = "$PSScriptRoot\ui-main.json")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$process = Get-Process IDMan | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (!$process) { throw 'IDM main window not found' }
$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$rows = foreach ($e in $items) {
    try {
        $c=$e.Current
        [pscustomobject]@{Name=$c.Name;Id=$c.AutomationId;Type=$c.ControlType.ProgrammaticName;Class=$c.ClassName;Enabled=$c.IsEnabled;Offscreen=$c.IsOffscreen;AccessKey=$c.AccessKey;Patterns=@($e.GetSupportedPatterns() | ForEach-Object ProgrammaticName)}
    } catch { }
}
[pscustomobject]@{Captured=(Get-Date -Format o);ProcessId=$process.Id;Window=$root.Current.Name;Controls=@($rows)} | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $Output
$rows | Select-Object Name,Id,Type,AccessKey | Format-Table -AutoSize
