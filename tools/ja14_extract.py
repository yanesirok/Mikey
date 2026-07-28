"""Pull the maps and the crown mesh out of the JA14 UNITY .unitypackage.

The pack is 86 MB of HDRP shadergraphs and 4K sheets the arena does not use. A .unitypackage is a
gzipped tar of <guid>/{pathname,asset}; this reads the pathnames and copies only what is needed,
downsampled to the point where the error is below the source's own noise. The bark strip at
256x2048 costs RMSE 1.997 against an inter-row noise floor of 2.80, and no atlas cell is bigger
than 256 px, so a 1024 leaf sheet is already four times oversampled.

Everything it writes lands in Assets/Fight/Arena/Ref/, which nothing but the editor scripts
reference — so none of it ships in the player build.

Usage:  python tools/ja14_extract.py <JA14_PhyllostachysNigraHenonis_UNITY.zip>
"""
import io
import os
import sys
import tarfile
import zipfile

from PIL import Image

Image.MAX_IMAGE_PIXELS = None
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..",
                   "Assets", "Fight", "Arena", "Ref")

# source basename -> (destination, size or None to copy verbatim)
WANTED = {
    "T_PhyllostachysNigraHenonisBark01_BC.png": ("JA14_Bark_BC.png", (256, 2048)),
    "T_PhyllostachysNigraHenonisBark01_N.png": ("JA14_Bark_N.png", (256, 2048)),
    "T_PhyllostachysNigraHenonisBark01_R.png": ("JA14_Bark_R.png", (256, 2048)),
    "T_PhyllostachysNigraHenonisLeaves01_BC.png": ("JA14_Leaves_BC.png", (1024, 1024)),
    "T_PhyllostachysNigraHenonisLeaves01_M.png": ("JA14_Leaves_M.png", (1024, 1024)),
    "MESH_JA14_PhyllostachysNigraHenonis_A_LOD2.fbx": ("JA14_Bamboo.fbx", None),
}


def members(package_bytes):
    """Yield (basename, bytes) for every asset inside the .unitypackage."""
    tar = tarfile.open(fileobj=io.BytesIO(package_bytes), mode="r:gz")
    paths, blobs = {}, {}
    for entry in tar.getmembers():
        if not entry.isfile():
            continue
        guid, _, leaf = entry.name.partition("/")
        if leaf == "pathname":
            paths[guid] = tar.extractfile(entry).read().decode().strip()
        elif leaf == "asset":
            blobs[guid] = tar.extractfile(entry).read()
    for guid, path in paths.items():
        if guid in blobs:
            yield os.path.basename(path), blobs[guid]


def main(zip_path):
    os.makedirs(OUT, exist_ok=True)
    with zipfile.ZipFile(zip_path) as archive:
        inner = next(n for n in archive.namelist() if n.endswith(".unitypackage"))
        package = archive.read(inner)

    written = 0
    for name, blob in members(package):
        if name not in WANTED:
            continue
        dest, size = WANTED[name]
        target = os.path.normpath(os.path.join(OUT, dest))
        if size is None:
            with open(target, "wb") as handle:
                handle.write(blob)
        else:
            image = Image.open(io.BytesIO(blob))
            # Alpha on Bark01_BC is an export border artefact — two rows of 242 — not a mask.
            image = image.convert("RGB").resize(size, Image.LANCZOS)
            image.save(target, optimize=True)
        print(f"{name} -> {dest} {size or 'verbatim'}")
        written += 1

    if written != len(WANTED):
        sys.exit(f"expected {len(WANTED)} assets, wrote {written}")


if __name__ == "__main__":
    main(sys.argv[1])
