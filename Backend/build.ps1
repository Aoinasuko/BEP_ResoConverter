param(
    [string]$ResonitePath = 'D:\Game\SteamLibrary\steamapps\common\Resonite'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$nativeRoot = Join-Path $PSScriptRoot 'Native'
$editorRoot = Join-Path $repoRoot 'Assets\BEPFairyTech\ResoConverter\Editor'
$stagingRoot = Join-Path $PSScriptRoot ('build\' + [Guid]::NewGuid().ToString('N'))
if (-not (Test-Path -LiteralPath (Join-Path $ResonitePath 'FrooxEngine.dll'))) {
    throw "FrooxEngine.dll was not found in $ResonitePath"
}
New-Item -ItemType Directory -Force -Path $stagingRoot, (Join-Path $editorRoot 'Plugins') | Out-Null
function Build-DotNet([string[]]$BuildArguments) {
    & dotnet @BuildArguments
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed' }
}
Build-DotNet @('build', (Join-Path $nativeRoot 'Schema\Schema.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet')
Copy-Item -LiteralPath (Join-Path $nativeRoot 'Schema\bin\Release\netstandard2.0\BEP.ResoConverter.Schema.dll') -Destination (Join-Path $editorRoot 'Plugins') -Force
# The dedicated Unity build uses only .NET Standard 2.1 APIs so projects without
# VRC/NDMF also work, without System.Memory/Unsafe assembly dependency collisions.
Build-DotNet @('build', (Join-Path $PSScriptRoot 'UnityProtobuf\UnityProtobuf.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityProtobuf\bin\Release\netstandard2.1\Google.Protobuf.dll') -Destination (Join-Path $editorRoot 'Plugins') -Force
Build-DotNet @('publish', (Join-Path $nativeRoot 'Launcher\Launcher.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $stagingRoot, '--nologo', '-v', 'quiet')
Build-DotNet @('publish', (Join-Path $nativeRoot 'Puppeteer\Puppeteer.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', "-p:ResoniteDir=$ResonitePath", '-o', $stagingRoot, '--nologo', '-v', 'quiet')
foreach ($pattern in @('FrooxEngine*.dll', 'Elements*.dll', 'SkyFrost*.dll', 'ProtoFlux*.dll', 'Renderite*.dll')) {
    if (Get-ChildItem -LiteralPath $stagingRoot -Filter $pattern) { throw "Proprietary Resonite assemblies must not be redistributed: $pattern" }
}
Copy-Item -LiteralPath (Join-Path $nativeRoot 'THIRD-PARTY-LICENSE.md') -Destination $stagingRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $stagingRoot
[IO.File]::WriteAllText((Join-Path $stagingRoot 'bep-features.json'), '{"facialExpressions":3,"avatarEyeLook":1}', [Text.UTF8Encoding]::new($false))
$runtimePackage = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
    if ($runtimePackage -and (Test-Path -LiteralPath (Join-Path $runtimePackage.FullName $notice))) {
        Copy-Item -LiteralPath (Join-Path $runtimePackage.FullName $notice) -Destination $stagingRoot
    }
}
$payloadZip = Join-Path $stagingRoot '..\BackendPayload.zip'
Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $payloadZip -Force
Copy-Item -LiteralPath $payloadZip -Destination (Join-Path $editorRoot 'BackendPayload.bytes') -Force
Write-Output "Built self-contained backend: $stagingRoot"
Write-Output "Updated: $(Join-Path $editorRoot 'BackendPayload.bytes')"
