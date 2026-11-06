param([string]$Title,[long]$Handle=0,[int]$SetId=0,[string]$Text,[int]$ClickId=0,[int]$Command=0,[switch]$Close)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
if(-not ('HarborDialog' -as [type])) {Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class HarborDialog {
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll")] public static extern IntPtr GetDlgItem(IntPtr h,int id);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,string l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")] public static extern IntPtr ReadMessage(IntPtr h,uint m,IntPtr w,StringBuilder l);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h,EnumProc p,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint f);
 public struct RECT {public int Left,Top,Right,Bottom;}
}
'@}
$h=[HarborDialog]::FindWindow($null,$Title)
if($Handle){$h=[IntPtr]$Handle}
if($Title -eq '@main'){$h=(Get-Process IDMan | Select-Object -First 1).MainWindowHandle}
if($h -eq [IntPtr]::Zero) {
 Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
 $p=Get-Process IDMan | Select-Object -First 1
 $root=[System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
 $condition=New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty),$Title
 $element=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
 if($element){$h=[IntPtr]$element.Current.NativeWindowHandle}
}
if($h -eq [IntPtr]::Zero) {throw "Window not found: $Title"}
if($Command){[void][HarborDialog]::PostMessage($h,0x111,[IntPtr]$Command,[IntPtr]::Zero);return}
if($SetId) {
 $edit=[HarborDialog]::GetDlgItem($h,$SetId)
 if($edit -eq [IntPtr]::Zero){
  $script:found=[IntPtr]::Zero
  $find=[HarborDialog+EnumProc]{param($c,$l) if($script:found -eq [IntPtr]::Zero -and [HarborDialog]::GetDlgCtrlID($c) -eq $SetId){$script:found=$c};return $true}
  [void][HarborDialog]::EnumChildWindows($h,$find,[IntPtr]::Zero)
  $edit=$script:found
 }
 if($edit -eq [IntPtr]::Zero){throw 'Control not found'}
 [void][HarborDialog]::SendMessage($edit,0xC,[IntPtr]::Zero,$Text)
}
if($ClickId) {
 $script:button=[IntPtr]::Zero
 $find=[HarborDialog+EnumProc]{param($c,$l) if([HarborDialog]::GetDlgCtrlID($c) -eq $ClickId){$script:button=$c};return $true}
 [void][HarborDialog]::EnumChildWindows($h,$find,[IntPtr]::Zero)
 if($script:button -eq [IntPtr]::Zero){throw 'Button not found'}
 [void][HarborDialog]::PostMessage($script:button,0xF5,[IntPtr]::Zero,[IntPtr]::Zero);return
}
if($Close){[void][HarborDialog]::PostMessage($h,0x10,[IntPtr]::Zero,[IntPtr]::Zero);return}
$rows=New-Object System.Collections.Generic.List[object]
$cb=[HarborDialog+EnumProc]{param($c,$l) $b=New-Object System.Text.StringBuilder 1024;$class=New-Object System.Text.StringBuilder 128;[void][HarborDialog]::ReadMessage($c,0xD,[IntPtr]1024,$b);[void][HarborDialog]::GetClassName($c,$class,128);$rows.Add([pscustomobject]@{Handle=$c.ToInt64();Id=[HarborDialog]::GetDlgCtrlID($c);Class=$class.ToString();Text=$b.ToString()});return $true}
[void][HarborDialog]::EnumChildWindows($h,$cb,[IntPtr]::Zero)
$safe=($Title -replace '[^a-zA-Z0-9]','_')
$rows | ConvertTo-Json | Set-Content -Encoding UTF8 "$PSScriptRoot\dialog-$safe.json"
$rows | Format-Table -AutoSize
$r=New-Object HarborDialog+RECT;[void][HarborDialog]::GetWindowRect($h,[ref]$r)
$b=New-Object System.Drawing.Bitmap ($r.Right-$r.Left),($r.Bottom-$r.Top)
$g=[System.Drawing.Graphics]::FromImage($b);$dc=$g.GetHdc()
try{[void][HarborDialog]::PrintWindow($h,$dc,2)}finally{$g.ReleaseHdc($dc)}
$b.Save("$PSScriptRoot\dialog-$safe.png");$g.Dispose();$b.Dispose()
