#!/usr/bin/env bash
# Builds PhotoImport.exe, HeicThumb.dll and the Windows installer on Linux.
# Needs: mono-mcs, the Mono WinForms/Drawing libraries, nsis, python3 with pip, unzip.
set -euo pipefail
cd "$(dirname "$0")"

HEIF_WHEEL_VERSION=1.8.1
REFS="-r:System.Windows.Forms.dll -r:System.Drawing.dll"
FONTS="-resource:assets/AfacadFlux-Regular.ttf,AfacadFlux-Regular.ttf -resource:assets/AfacadFlux-SemiBold.ttf,AfacadFlux-SemiBold.ttf"
APP="src/AssemblyInfo.cs src/Model.cs src/Demo.cs src/Wpd.cs src/Ui.cs src/MainForm.cs src/Heif.cs src/Viewer.cs"

mkdir -p build dist vendor

# The HEIC decoder: prebuilt Windows x64 libheif DLLs taken from the pillow-heif wheel
if ! ls vendor/libheif-*.dll >/dev/null 2>&1; then
  python3 -m pip download -q --only-binary=:all: --platform win_amd64 --python-version 3.11 --no-deps \
    "pillow-heif==${HEIF_WHEEL_VERSION}" -d vendor/wheel
  unzip -q -o -j vendor/wheel/*.whl '*.dll' '*/LICENSES_bundled.txt' -d vendor
fi
cp vendor/*.dll build/
cp vendor/LICENSES_bundled.txt "build/Third-party licenses.txt"

mcs -nologo -target:winexe -platform:anycpu -optimize+ -win32icon:assets/app.ico -out:build/PhotoImport.exe $APP $REFS $FONTS
mcs -nologo -target:library -platform:anycpu -optimize+ -out:build/HeicThumb.dll src/Heif.cs src/Thumb.cs -r:System.Drawing.dll

makensis -V2 installer.nsi
echo "Built dist/PhotoImportSetup.exe"
