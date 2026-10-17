<#
.SYNOPSIS
  M4b PetRPG check: compares the user's Farm Pal save (LocalStorage), PlayerPrefs and git status with the baseline
  in .e2e/petrpg-m4b (save-before.csv, prefs-before.reg, git-before.txt). Prints SAME/DIFFERENT per part; exit 0 when all are the same.
#>
$ErrorActionPreference = 'Stop'
$save = "C:\Users\Admin\AppData\LocalLow\3HP\Farm Pal"
$b = Join-Path $PSScriptRoot '..\.e2e\petrpg-m4b'
$now = Get-ChildItem "$save\LocalStorage" -Recurse -File | Get-FileHash | Select-Object Hash, @{n='Rel';e={$_.Path.Substring($save.Length)}}
$saveDiff = Compare-Object (Import-Csv "$b\save-before.csv") $now -Property Hash, Rel
reg export "HKCU\Software\Unity\UnityEditor\3HP\Farm Pal" "$b\prefs-now.reg" /y | Out-Null
$prefsSame = (Get-FileHash "$b\prefs-before.reg").Hash -eq (Get-FileHash "$b\prefs-now.reg").Hash
$gitNow = @(git -C E:\PetRPG status --porcelain | Where-Object { $_ -notmatch 'Packages/packages-lock.json' })
$gitBefore = @(Get-Content "$b\git-before.txt" | Where-Object { $_.Trim() -ne '' })
$gitSame = ($gitNow -join "`n") -eq ($gitBefore -join "`n")
"save:  " + $(if ($saveDiff) { 'DIFFERENT'; $saveDiff | Out-String } else { 'SAME' })
"prefs: " + $(if ($prefsSame) { 'SAME' } else { 'DIFFERENT' })
"git:   " + $(if ($gitSame) { 'SAME (packages-lock.json ignored while linked)' } else { 'DIFFERENT: ' + ($gitNow -join '; ') })
"parked leftovers: " + (@(Get-ChildItem $save -Directory | Where-Object Name -match 'agentplay-user').Count)
if ($saveDiff -or -not $prefsSame -or -not $gitSame) { exit 1 }
exit 0
