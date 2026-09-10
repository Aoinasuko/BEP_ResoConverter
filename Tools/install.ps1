param([string]$ProjectPath = 'E:\Avatar\Test')
$ErrorActionPreference = 'Stop'
$taskSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Assets\BEPFairyTech\ResoConverter'))
$taskProject = [IO.Path]::GetFullPath($ProjectPath)
if (!(Test-Path -LiteralPath (Join-Path $taskProject 'ProjectSettings\ProjectVersion.txt'))) {
    throw 'The destination is not a Unity project.'
}
$taskDestination = Join-Path $taskProject 'Assets\BEPFairyTech\ResoConverter'
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
Copy-Item -Path (Join-Path $taskSource '*') -Destination $taskDestination -Recurse -Force
Write-Output "Installed ResoConverter into $taskDestination"
