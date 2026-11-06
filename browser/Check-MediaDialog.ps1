param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), 'Add a download'
$deadline = [DateTime]::UtcNow.AddSeconds(15)
do {
    $dialog = $null
    foreach ($process in @(Get-Process FlugelDownloadManager -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })) {
        try {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $dialog = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        } catch { $dialog = $null }
        if ($dialog) { break }
    }
    if ($dialog) { break }
    Start-Sleep -Milliseconds 100
} while ([DateTime]::UtcNow -lt $deadline)
if (-not $dialog) { throw 'Browser capture did not open the desktop Add dialog.' }
function ById([string]$Id) {
    $c = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::AutomationIdProperty), $Id
    $element = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
    if (-not $element) { throw "Missing control: $Id" }
    return $element
}
$url = (ById 'SourceUrl').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
if ($url -notmatch '^http://127\.0\.0\.1:\d+/588340\.mp4$') { throw 'Refusing to confirm anything except the controlled local MP4 fixture.' }
(ById 'DestinationDirectory').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Destination)
$radios = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ControlTypeProperty), ([System.Windows.Automation.ControlType]::RadioButton)))
$start = $radios | Where-Object { $_.Current.Name -replace '_','' -eq 'Start now' } | Select-Object -First 1
if (-not $start) { throw 'Start now radio button missing.' }
$start.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$c = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), 'Add download'
$button = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
if (-not $button) { throw 'Add download button missing.' }
$button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
'PASS local browser MP4 reaches desktop confirmation and Start now'

