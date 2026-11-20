#!/usr/bin/env bash
# Runs the import-logic tests with Mono. Pass two HEIC files to also test the decoder wrapper
# against the system libheif: ./test.sh landscape.heic portrait.heic
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p build
mcs -nologo -out:build/library-test.exe src/Model.cs src/Demo.cs src/Wpd.cs src/Ui.cs tests/LibraryTest.cs \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll
mono build/library-test.exe
if [ "$#" -eq 2 ]; then
  mcs -nologo -define:LINUXTEST -out:build/heif-test.exe src/Heif.cs tests/HeifTest.cs -r:System.Drawing.dll
  mono build/heif-test.exe "$1" "$2"
fi
