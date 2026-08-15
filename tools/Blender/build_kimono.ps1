# Прогоняет Blender headless и падает, если скрипт упал.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = Join-Path $PSScriptRoot "..\.."
$out = Join-Path $root "Assets\Fight\character\kimono"
& $blender --background --factory-startup --python (Join-Path $PSScriptRoot "kimono_fit.py") -- `
    --body (Join-Path $root "Assets\Fight\character\body\Fighter_Body.fbx") `
    --kimono (Join-Path $root "Assets\Characters\Clothes\kimono.glb") `
    --out $out @args
exit $LASTEXITCODE
