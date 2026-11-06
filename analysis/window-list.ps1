$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public class HarborWindows {
 public delegate bool Callback(IntPtr h,IntPtr l);
 [DllImport("user32.dll")] public static extern bool EnumWindows(Callback c,IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
$target=(Get-Process IDMan).Id
$rows=New-Object System.Collections.Generic.List[object]
$cb=[HarborWindows+Callback]{param($h,$l) $p=0;[void][HarborWindows]::GetWindowThreadProcessId($h,[ref]$p);if($p -eq $target){$b=New-Object System.Text.StringBuilder 512;[void][HarborWindows]::GetWindowText($h,$b,512);$rows.Add([pscustomobject]@{Handle=$h.ToInt64();Title=$b.ToString();Visible=[HarborWindows]::IsWindowVisible($h)})};return $true}
[void][HarborWindows]::EnumWindows($cb,[IntPtr]::Zero)
$rows | ConvertTo-Json | Set-Content -Encoding UTF8 "$PSScriptRoot\runtime-windows.json"
$rows | Format-Table -AutoSize
