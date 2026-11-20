# Third-party notices

Photo Import's own source code is licensed under the GNU General Public License v3.0 (see `LICENSE`).

The installer (`PhotoImportSetup.exe`) also contains the components below. They are not modified. The decoder DLLs are taken unchanged from the Windows x64 wheel of [pillow-heif](https://github.com/bigcat88/pillow_heif) 1.8.1, whose own list of bundled licences is installed next to the app as `Third-party licenses.txt`.

| Component | Version | Licence | Source code |
| --- | --- | --- | --- |
| libheif | 1.23.6 | LGPL-3.0 | https://github.com/strukturag/libheif/tree/v1.23.6 |
| libde265 | 1.1.3 | LGPL-3.0 | https://github.com/strukturag/libde265/tree/v1.1.3 |
| x265 | 4.3 | GPL-2.0 | https://github.com/Multicorewareinc/x265/tree/4.3 |
| MinGW-w64 runtime (libgcc, libstdc++, libwinpthread) | as shipped in the wheel | GPL-3.0 with GCC Runtime Library Exception; MIT and BSD | https://sourceforge.net/p/mingw-w64/mingw-w64/ |

x265 is an encoder. Photo Import only decodes and never calls it; it is present because the prebuilt libheif DLL links against it.

## Font

The interface uses **Afacad Flux**, Copyright 2023 The Afacad Project Authors (https://github.com/Dicotype/Afacad), licensed under the SIL Open Font License 1.1 (see `assets/OFL.txt`).

`assets/AfacadFlux-Regular.ttf` and `assets/AfacadFlux-SemiBold.ttf` are static instances (weights 400 and 600) generated from the variable font with fontTools. They are embedded in `PhotoImport.exe`.

## Installer

The installer is built with [NSIS](https://nsis.sourceforge.io/) (zlib/libpng licence).
