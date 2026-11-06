param([string]$Name='IDMan',[string]$Label='main',[int]$Command=0)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('HarborObserve' -as [type])) {
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class HarborObserve {
 [DllImport("user32.dll")] public static extern IntPtr GetMenu(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr h,uint i,StringBuilder b,int n,uint flags);
 [DllImport("user32.dll")] public static extern uint GetMenuItemID(IntPtr h,int i);
 [DllImport("user32.dll")] public static extern IntPtr GetSubMenu(IntPtr h,int i);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public struct RECT {public int Left,Top,Right,Bottom;}
}
'@
}
$p=Get-Process $Name | Where-Object MainWindowHandle -ne 0 | Select-Object -First 1
if (!$p) {throw "No window: $Name"}
$h=$p.MainWindowHandle
function Read-Menu([IntPtr]$menu,[string]$prefix) {
 for($i=0;$i -lt [HarborObserve]::GetMenuItemCount($menu);$i++) {
  $text=New-Object System.Text.StringBuilder 512
  [void][HarborObserve]::GetMenuString($menu,$i,$text,512,0x400)
  $path="$prefix/$text"
  [pscustomobject]@{Path=$path;Id=[HarborObserve]::GetMenuItemID($menu,$i)}
  $child=[HarborObserve]::GetSubMenu($menu,$i)
  if($child -ne [IntPtr]::Zero) {Read-Menu $child $path}
 }
}
$menus=@(Read-Menu ([HarborObserve]::GetMenu($h)) '')
$menus | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 "$PSScriptRoot\ui-$Label-menus.json"
$menus | Format-Table -AutoSize
if($Command -ne 0) {[void][HarborObserve]::PostMessage($h,0x111,[IntPtr]$Command,[IntPtr]::Zero); return}
[void][HarborObserve]::ShowWindow($h,9)
[void][HarborObserve]::SetForegroundWindow($h)
$r=New-Object HarborObserve+RECT
[void][HarborObserve]::GetWindowRect($h,[ref]$r)
$bitmap=New-Object System.Drawing.Bitmap ($r.Right-$r.Left),($r.Bottom-$r.Top)
$graphics=[System.Drawing.Graphics]::FromImage($bitmap)
$dc=$graphics.GetHdc()
try {[void][HarborObserve]::PrintWindow($h,$dc,2)} finally {$graphics.ReleaseHdc($dc)}
$bitmap.Save("$PSScriptRoot\ui-$Label.png")
$graphics.Dispose();$bitmap.Dispose()
