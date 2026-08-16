# Прогоняет Blender headless по разу на бойца и падает, если любой прогон упал.
# Карты кимоно у обоих одинаковы: рост нормализован к общему, геометрия ткани
# та же, поэтому второй прогон перезапишет их тем же содержимым.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = Join-Path $PSScriptRoot "..\.."
$out = Join-Path $root "Assets\Fight\character\kimono"
$kimono = Join-Path $root "Assets\Characters\Clothes\kimono.glb"
$script = Join-Path $PSScriptRoot "kimono_fit.py"

$fighters = @(
    @{ Body = "Ch28_nonPBR.fbx"; Name = "KimonoFighter_Player" },
    @{ Body = "Remy.fbx";        Name = "KimonoFighter_Enemy"  }
)

foreach ($f in $fighters) {
    $body = Join-Path $root ("Assets\Fight\NewChar3d\" + $f.Body)
    & $blender --background --factory-startup --python $script -- `
        --body $body --kimono $kimono --out $out --name $f.Name @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
