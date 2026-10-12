<#
.SYNOPSIS
  Embeds the AgentPlay package into a Unity project through a git-ignored junction, without editing manifest.json.
.EXAMPLE
  powershell -File Tools/link-agentplay.ps1 -ProjectPath E:\Unity-WorkSpace\MyGame
#>
param([Parameter(Mandatory = $true)][string]$ProjectPath)
$ErrorActionPreference = 'Stop'

$package = (Resolve-Path (Join-Path $PSScriptRoot '..\Packages\com.agentplay.core')).Path
$project = (Resolve-Path $ProjectPath).Path
if (-not (Test-Path (Join-Path $project 'Packages\manifest.json'))) {
    throw "$project is not a Unity project (no Packages\manifest.json)"
}

$link = Join-Path $project 'Packages\com.agentplay.core'
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

$gitignore = Join-Path $project '.gitignore'
$line = '/Packages/com.agentplay.core'
$present = (Test-Path $gitignore) -and (Select-String -Path $gitignore -SimpleMatch -Pattern $line -Quiet)
if (-not $present) {
    $text = "`n# AgentPlay: local junction created by AgentPlay/Tools/link-agentplay.ps1`n$line`n"
    [System.IO.File]::AppendAllText($gitignore, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Added $line to .gitignore"
}

$briefDir = Join-Path $project 'AgentPlay'
New-Item -ItemType Directory -Force $briefDir | Out-Null
$brief = Join-Path $briefDir 'brief.md'
if (-not (Test-Path $brief)) {
    $template = "# Brief`n`nGoal: ...`nHow to win: ...`nHow to lose: ...`nTips: ...`n"
    [System.IO.File]::WriteAllText($brief, $template, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Created $brief (fill it in: the agent reads it every session)"
}
