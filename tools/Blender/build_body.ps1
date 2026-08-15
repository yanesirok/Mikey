# Прогоняет Blender headless и падает, если скрипт упал.
# Без --factory-startup: он выключает MPFB2, без которого генерировать нечего.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = Join-Path $PSScriptRoot "..\.."
$out = Join-Path $root "Assets\Fight\character\body\Fighter_Body.fbx"
& $blender --background --python (Join-Path $PSScriptRoot "make_body.py") -- --out $out
exit $LASTEXITCODE
