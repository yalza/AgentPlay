<#
.SYNOPSIS
  Embeds the AgentPlay package into a Unity project through a junction, without editing manifest.json.
.DESCRIPTION
  Default: adds the junction to the project's .gitignore (for your own projects).
  -LocalOnly: changes no tracked file; the junction, AgentPlay/, Assets/AgentPlayAdapter and an AgentPlay-made
  Assets/csc.rsp are excluded through .git/info/exclude instead (for team repos).
  -Unlink: removes the junction, Assets/AgentPlayAdapter, an AgentPlay-made Assets/csc.rsp and the exclude block,
  and restores Packages/packages-lock.json. AgentPlay/ (traces, playbook) stays, still excluded, unless -Purge is given.
.EXAMPLE
  powershell -File Tools/link-agentplay.ps1 -ProjectPath E:\Unity-WorkSpace\MyGame
.EXAMPLE
  powershell -File Tools/link-agentplay.ps1 -ProjectPath E:\TeamGame -LocalOnly
.EXAMPLE
  powershell -File Tools/link-agentplay.ps1 -ProjectPath E:\TeamGame -Unlink -Purge
#>
param([Parameter(Mandatory = $true)][string]$ProjectPath, [switch]$LocalOnly, [switch]$Unlink, [switch]$Purge)
$ErrorActionPreference = 'Stop'

# Unity's csc.rsp has no comment syntax, so AgentPlay's file is recognised by its exact content.
$OurRsp = '-define:AGENTPLAY'
# Paths relative to the Unity project; Get-Block prefixes them with the project's folder inside the repository.
$Paths = @('Packages/com.agentplay.core', 'AgentPlay/', 'Assets/AgentPlayAdapter/', 'Assets/AgentPlayAdapter.meta', 'Assets/csc.rsp', 'Assets/csc.rsp.meta')
$Utf8 = New-Object System.Text.UTF8Encoding($false)

# Runs git without letting its stderr warnings (CRLF notices, ...) become terminating errors under 'Stop' in PowerShell 5.1.
function Invoke-Git {
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git @args 2>$null
        $script:GitExit = $LASTEXITCODE
        return $out
    } finally {
        $ErrorActionPreference = $saved
    }
}

function Get-ExcludePath($project) {
    # The common dir, so a linked worktree uses the exclude file git really reads.
    $gitDir = (Invoke-Git -C $project rev-parse --git-common-dir)
    if (-not $gitDir) { throw "$project is not a git repository; -LocalOnly and -Unlink need git" }
    if (-not [System.IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $project $gitDir }
    $info = Join-Path $gitDir 'info'
    New-Item -ItemType Directory -Force $info | Out-Null
    return (Join-Path $info 'exclude')
}

# The exclude block for this project: patterns anchored at the repository root, so a Unity project in a subfolder
# of the repository is covered too.
function Get-Block($project, $paths) {
    $prefix = "$(Invoke-Git -C $project rev-parse --show-prefix)".Trim()
    return @('# AgentPlay begin') + ($paths | ForEach-Object { '/' + $prefix + $_ }) + @('# AgentPlay end')
}

# Removes the AgentPlay block and keeps every other byte of the file (UTF-8, the file's own line endings).
function Remove-ExcludeBlock($path) {
    if (-not (Test-Path $path)) { return }
    $text = [System.IO.File]::ReadAllText($path, $Utf8)
    $lines = $text -split "`n", -1
    $out = New-Object System.Collections.Generic.List[string]
    $inside = $false
    foreach ($l in $lines) {
        $bare = $l.TrimEnd("`r")
        if ($bare -eq '# AgentPlay begin') { $inside = $true; continue }
        if ($bare -eq '# AgentPlay end') { $inside = $false; continue }
        if (-not $inside) { $out.Add($l) }
    }
    [System.IO.File]::WriteAllText($path, ($out -join "`n"), $Utf8)
}

function Add-ExcludeBlock($path, $block) {
    $text = if (Test-Path $path) { [System.IO.File]::ReadAllText($path, $Utf8) } else { '' }
    if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { $text += "`n" }
    [System.IO.File]::WriteAllText($path, $text + (($block -join "`n") + "`n"), $Utf8)
}

# True when git tracks the path (or anything under it): such files belong to the team and are never deleted.
function Test-Tracked($project, $relative) {
    return [bool](Invoke-Git -C $project ls-files -- $relative)
}

$package = (Resolve-Path (Join-Path $PSScriptRoot '..\Packages\com.agentplay.core')).Path
$project = (Resolve-Path $ProjectPath).Path
if (-not (Test-Path (Join-Path $project 'Packages\manifest.json'))) {
    throw "$project is not a Unity project (no Packages\manifest.json)"
}
$link = Join-Path $project 'Packages\com.agentplay.core'

if ($Unlink) {
    $exclude = Get-ExcludePath $project
    if (Test-Path $link) {
        $item = Get-Item $link -Force
        if ($item.LinkType -ne 'Junction') { throw "$link is not a junction; not removing it" }
        cmd /c rmdir "$link" | Out-Null
        Write-Host "Removed junction $link"
    }
    foreach ($p in @('Assets/AgentPlayAdapter', 'Assets/AgentPlayAdapter.meta')) {
        $full = Join-Path $project $p
        if (-not (Test-Path $full)) { continue }
        if (Test-Tracked $project $p) { Write-Host "Kept ${full}: git tracks it (committed by your team)"; continue }
        Remove-Item $full -Recurse -Force
        Write-Host "Removed $full"
    }
    $rsp = Join-Path $project 'Assets\csc.rsp'
    if ((Test-Path $rsp) -and -not (Test-Tracked $project 'Assets/csc.rsp') -and (((Get-Content $rsp -Raw) -as [string]).Trim() -eq $OurRsp)) {
        Remove-Item $rsp -Force
        if (Test-Path "$rsp.meta") { Remove-Item "$rsp.meta" -Force }
        Write-Host "Removed $rsp"
    }
    Remove-ExcludeBlock $exclude
    # Restore packages-lock.json only if it was clean when we linked (Unity rewrites it, sometimes only its line endings).
    $lockClean = Join-Path $project 'AgentPlay\.packages-lock-was-clean'
    $lockChanged = [bool](Invoke-Git -C $project status --porcelain -- Packages/packages-lock.json)
    if ((Test-Path $lockClean) -and $lockChanged) {
        Invoke-Git -C $project checkout -- Packages/packages-lock.json | Out-Null
        Write-Host 'Restored Packages/packages-lock.json'
    } elseif ($lockChanged) {
        Write-Host 'Packages/packages-lock.json had changes of yours before linking; left as is (remove the com.agentplay.core entry by hand if present)'
    }
    if (Test-Path $lockClean) { Remove-Item $lockClean -Force }
    $ap = Join-Path $project 'AgentPlay'
    $apTracked = Test-Tracked $project 'AgentPlay'
    if ($Purge -and (Test-Path $ap) -and -not $apTracked) {
        Remove-Item $ap -Recurse -Force
        Write-Host "Removed $ap"
    } elseif (Test-Path $ap) {
        # AgentPlay/ (traces, playbook) is kept, so it stays out of git status.
        Add-ExcludeBlock $exclude (Get-Block $project @('AgentPlay/'))
        if ($apTracked) { Write-Host "Kept ${ap}: git tracks files in it (committed by your team); untracked output stays excluded" }
        else { Write-Host "Kept $ap (still excluded from git); add -Purge to delete it" }
    }
    exit 0
}

if (Test-Path $link) {
    $item = Get-Item $link -Force
    $target = @($item.Target) | Select-Object -First 1
    if ($item.LinkType -ne 'Junction' -or ((Resolve-Path $target).Path -ne $package)) {
        throw "$link already exists and is not a junction to $package"
    }
    Write-Host "Already linked: $link"
} else {
    New-Item -ItemType Junction -Path $link -Target $package | Out-Null
    Write-Host "Linked $link -> $package"
}

if ($LocalOnly) {
    $exclude = Get-ExcludePath $project
    Remove-ExcludeBlock $exclude
    Add-ExcludeBlock $exclude (Get-Block $project $Paths)
    # Remember whether packages-lock.json was clean, so -Unlink restores it only then.
    New-Item -ItemType Directory -Force (Join-Path $project 'AgentPlay') | Out-Null
    $lockClean = Join-Path $project 'AgentPlay\.packages-lock-was-clean'
    # A second -LocalOnly finds the file already changed by Unity and keeps the earlier marker.
    if (-not (Invoke-Git -C $project status --porcelain -- Packages/packages-lock.json) -and -not (Test-Path $lockClean)) {
        [System.IO.File]::WriteAllText($lockClean, "", $Utf8)
    }
    Write-Host "Wrote the AgentPlay block to $exclude (nothing added to .gitignore)"
    Write-Host 'Unity may edit Packages/packages-lock.json; -Unlink restores it, or run: git checkout -- Packages/packages-lock.json'
} else {
    $gitignore = Join-Path $project '.gitignore'
    $line = '/Packages/com.agentplay.core'
    $present = (Test-Path $gitignore) -and (Select-String -Path $gitignore -SimpleMatch -Pattern $line -Quiet)
    if (-not $present) {
        $text = "`n# AgentPlay: local junction created by AgentPlay/Tools/link-agentplay.ps1`n$line`n"
        [System.IO.File]::AppendAllText($gitignore, $text, $Utf8)
        Write-Host "Added $line to .gitignore"
    }
}

$briefDir = Join-Path $project 'AgentPlay'
New-Item -ItemType Directory -Force $briefDir | Out-Null
$brief = Join-Path $briefDir 'brief.md'
if (-not (Test-Path $brief)) {
    $template = "# Brief`n`nGoal: ...`nHow to win: ...`nHow to lose: ...`nTips: ...`n"
    [System.IO.File]::WriteAllText($brief, $template, $Utf8)
    Write-Host "Created $brief (fill it in: the agent reads it every session)"
}
