# Прогоняет Blender headless и падает, если скрипт упал.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$out = Join-Path $PSScriptRoot "..\..\Assets\Fight\Arena\BridgeKit"
& $blender --background --factory-startup --python (Join-Path $PSScriptRoot "bridge_kit.py") -- --out $out
exit $LASTEXITCODE
