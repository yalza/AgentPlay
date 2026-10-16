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

$Marker = '# AgentPlay: local define, created by agentplay-adapt'
$Block = @('# AgentPlay begin', '/Packages/com.agentplay.core', '/AgentPlay/', '/Assets/AgentPlayAdapter/', '/Assets/AgentPlayAdapter.meta', '/Assets/csc.rsp', '/Assets/csc.rsp.meta', '# AgentPlay end')
$Utf8 = New-Object System.Text.UTF8Encoding($false)

function Get-ExcludePath($project) {
    # The common dir, so a linked worktree uses the exclude file git really reads.
    $gitDir = (& git -C $project rev-parse --git-common-dir 2>$null)
    if (-not $gitDir) { throw "$project is not a git repository; -LocalOnly and -Unlink need git" }
    if (-not [System.IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $project $gitDir }
    $info = Join-Path $gitDir 'info'
    New-Item -ItemType Directory -Force $info | Out-Null
    return (Join-Path $info 'exclude')
}

function Remove-ExcludeBlock($path) {
    if (-not (Test-Path $path)) { return }
    $out = New-Object System.Collections.Generic.List[string]
    $inside = $false
    foreach ($l in (Get-Content $path)) {
        if ($l -eq '# AgentPlay begin') { $inside = $true; continue }
        if ($l -eq '# AgentPlay end') { $inside = $false; continue }
        if (-not $inside) { $out.Add($l) }
    }
    [System.IO.File]::WriteAllLines($path, $out, $Utf8)
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
    foreach ($p in @('Assets\AgentPlayAdapter', 'Assets\AgentPlayAdapter.meta')) {
        $full = Join-Path $project $p
        if (Test-Path $full) { Remove-Item $full -Recurse -Force; Write-Host "Removed $full" }
    }
    $rsp = Join-Path $project 'Assets\csc.rsp'
    if ((Test-Path $rsp) -and ((Get-Content $rsp -TotalCount 1) -eq $Marker)) {
        Remove-Item $rsp -Force
        if (Test-Path "$rsp.meta") { Remove-Item "$rsp.meta" -Force }
        Write-Host "Removed $rsp"
    }
    Remove-ExcludeBlock $exclude
    & git -C $project diff --quiet -- Packages/packages-lock.json 2>$null
    if ($LASTEXITCODE -eq 1) { & git -C $project checkout -- Packages/packages-lock.json; Write-Host 'Restored Packages/packages-lock.json' }
    $ap = Join-Path $project 'AgentPlay'
    if ($Purge) {
        if (Test-Path $ap) { Remove-Item $ap -Recurse -Force; Write-Host "Removed $ap" }
    } elseif (Test-Path $ap) {
        # AgentPlay/ (traces, playbook) is kept, so it stays out of git status.
        [System.IO.File]::AppendAllText($exclude, "# AgentPlay begin`n/AgentPlay/`n# AgentPlay end`n", $Utf8)
        Write-Host "Kept $ap (still excluded from git); add -Purge to delete it"
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
    [System.IO.File]::AppendAllText($exclude, (($Block -join "`n") + "`n"), $Utf8)
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
