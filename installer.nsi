Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WordFunc.nsh"

!define APP "Apple Photo Importer"
!define FULLNAME "Apple Photo Importer for Windows"
!define PUBLISHER "Magnetic WP"
!define WEBSITE "https://www.magneticwp.com/"
!define DONATE "https://ko-fi.com/magneticwp"
!define LEGACY "Photo Import"
!define STATE "Software\Photo Import"
!define VERSION "2.2"
!define EXE "PhotoImport.exe"
!define CLSID "{7C3A5B1E-92D4-4F6A-B0E8-5D2F4A9C6E31}"
!define THUMBCAT "{e357fccd-a995-4576-b01f-234630154e96}"
!define NETCAT "{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"
!define PROGID "PhotoImport.Image"
!define ARP "Software\Microsoft\Windows\CurrentVersion\Uninstall\PhotoImport"
!define CLASSES "Software\Classes"

Name "${FULLNAME}"
OutFile "dist\PhotoImportSetup.exe"
InstallDir "$PROGRAMFILES64\${APP}"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
BrandingText "${FULLNAME} ${VERSION} by ${PUBLISHER}"

!define MUI_ICON "assets\app.ico"
!define MUI_UNICON "assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Open ${APP}"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchApp

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; Starts the app as the normal user, not with the installer's administrator rights
Function LaunchApp
  Exec '"$WINDIR\explorer.exe" "$INSTDIR\${EXE}"'
FunctionEnd

; A file still in use (File Explorer keeps the thumbnail DLLs loaded) is moved aside and removed at the next restart
!macro Replace NAME
  IfFileExists "$INSTDIR\${NAME}" 0 +6
  ClearErrors
  Delete "$INSTDIR\${NAME}"
  IfErrors 0 +3
  Rename "$INSTDIR\${NAME}" "$INSTDIR\${NAME}.$R9.old"
  Delete /REBOOTOK "$INSTDIR\${NAME}.$R9.old"
  File "build\${NAME}"
!macroend

; Any thumbnail handler that was registered before is remembered so uninstalling can put it back
!macro Claim KEY NAME
  ReadRegStr $0 HKLM "${KEY}" ""
  StrCmp $0 "" +3
  StrCmp $0 "${CLSID}" +2
  WriteRegStr HKLM "${STATE}\Previous" "${NAME}" "$0"
  WriteRegStr HKLM "${KEY}" "" "${CLSID}"
!macroend

!macro Release KEY NAME
  ReadRegStr $0 HKLM "${KEY}" ""
  StrCmp $0 "${CLSID}" 0 +6
  ReadRegStr $1 HKLM "${STATE}\Previous" "${NAME}"
  StrCmp $1 "" +3
  WriteRegStr HKLM "${KEY}" "" "$1"
  Goto +2
  DeleteRegKey HKLM "${KEY}"
!macroend

!macro HeicType EXT
  !insertmacro Claim "${CLASSES}\${EXT}\ShellEx\${THUMBCAT}" "${EXT}"
  !insertmacro Claim "${CLASSES}\SystemFileAssociations\${EXT}\ShellEx\${THUMBCAT}" "sfa${EXT}"
  WriteRegStr HKLM "${CLASSES}\${EXT}\OpenWithProgids" "${PROGID}" ""
  WriteRegStr HKLM "${CLASSES}\${EXT}" "PerceivedType" "image"
  ReadRegStr $0 HKLM "${CLASSES}\${EXT}" ""
  StrCmp $0 "" 0 +2
  WriteRegStr HKLM "${CLASSES}\${EXT}" "" "${PROGID}"
!macroend

!macro UnHeicType EXT
  !insertmacro Release "${CLASSES}\${EXT}\ShellEx\${THUMBCAT}" "${EXT}"
  !insertmacro Release "${CLASSES}\SystemFileAssociations\${EXT}\ShellEx\${THUMBCAT}" "sfa${EXT}"
  DeleteRegValue HKLM "${CLASSES}\${EXT}\OpenWithProgids" "${PROGID}"
  ReadRegStr $0 HKLM "${CLASSES}\${EXT}" ""
  StrCmp $0 "${PROGID}" 0 +2
  WriteRegStr HKLM "${CLASSES}\${EXT}" "" ""
!macroend

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "${APP} needs 64-bit Windows 10 or 11."
    Abort
  ${EndIf}
  SetRegView 64
FunctionEnd

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Install"
  SetShellVarContext all
  SetOutPath "$INSTDIR"
  System::Call 'kernel32::GetTickCount() i .R9'

  !insertmacro Replace "${EXE}"
  !insertmacro Replace "HeicThumb.dll"
  !insertmacro Replace "libheif-18c57b017b404cb1ef96dee8a6eef027.dll"
  !insertmacro Replace "libde265-0-da04b6b2448c39c0b7f218769dedb2f1.dll"
  !insertmacro Replace "libx265-217-276e7d75772a32bd086b54d54e01fd11.dll"
  !insertmacro Replace "libstdc++-6-e4d43a335d57132a6ec3004cece76b4f.dll"
  !insertmacro Replace "libgcc_s_seh-1-60817ff87b1fa8e35c12cd1a333734a3.dll"
  !insertmacro Replace "libwinpthread-1-71c5c74678606753e0a2f79ceb8c68f9.dll"
  !insertmacro Replace "Third-party licenses.txt"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateShortcut "$DESKTOP\${APP}.lnk" "$INSTDIR\${EXE}"
  CreateDirectory "$SMPROGRAMS\${APP}"
  CreateShortcut "$SMPROGRAMS\${APP}\${APP}.lnk" "$INSTDIR\${EXE}"
  CreateShortcut "$SMPROGRAMS\${APP}\Uninstall ${APP}.lnk" "$INSTDIR\Uninstall.exe"
  WriteINIStr "$SMPROGRAMS\${APP}\${PUBLISHER} website.url" "InternetShortcut" "URL" "${WEBSITE}"
  WriteINIStr "$SMPROGRAMS\${APP}\Support this app.url" "InternetShortcut" "URL" "${DONATE}"

  ; Versions before 2.2 were installed as "Photo Import": remove their shortcuts and folder
  StrCpy $R8 "$PROGRAMFILES64\${LEGACY}"
  ${If} $INSTDIR != $R8
    Delete "$DESKTOP\${LEGACY}.lnk"
    RMDir /r "$SMPROGRAMS\${LEGACY}"
    Delete /REBOOTOK "$R8\*.exe"
    Delete /REBOOTOK "$R8\*.dll"
    Delete /REBOOTOK "$R8\*.old"
    Delete /REBOOTOK "$R8\*.txt"
    RMDir /REBOOTOK "$R8"
  ${EndIf}

  ; HEIC thumbnail handler for File Explorer (a .NET class, registered the way regasm /codebase does it)
  ${WordReplace} "$INSTDIR" "\" "/" "+" $1
  StrCpy $1 "file:///$1/HeicThumb.dll"
  StrCpy $2 "HeicThumb, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}" "" "${APP} HEIC thumbnails"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32" "" "mscoree.dll"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32" "ThreadingModel" "Both"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32" "Class" "PhotoImport.HeicThumbnailProvider"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32" "Assembly" "$2"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32" "RuntimeVersion" "v4.0.30319"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32" "CodeBase" "$1"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32\1.0.0.0" "Class" "PhotoImport.HeicThumbnailProvider"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32\1.0.0.0" "Assembly" "$2"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32\1.0.0.0" "RuntimeVersion" "v4.0.30319"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\InprocServer32\1.0.0.0" "CodeBase" "$1"
  WriteRegStr HKLM "${CLASSES}\CLSID\${CLSID}\Implemented Categories\${NETCAT}" "" ""
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" "${CLSID}" "${APP} HEIC thumbnails"

  ; Photo viewer
  WriteRegStr HKLM "${CLASSES}\${PROGID}" "" "Photo"
  WriteRegStr HKLM "${CLASSES}\${PROGID}\DefaultIcon" "" "$INSTDIR\${EXE},0"
  WriteRegStr HKLM "${CLASSES}\${PROGID}\shell\open" "FriendlyAppName" "Photo Viewer"
  WriteRegStr HKLM "${CLASSES}\${PROGID}\shell\open\command" "" '"$INSTDIR\${EXE}" "%1"'
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}" "FriendlyAppName" "Photo Viewer"
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}\shell\open\command" "" '"$INSTDIR\${EXE}" "%1"'
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}\SupportedTypes" ".heic" ""
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}\SupportedTypes" ".heif" ""
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}\SupportedTypes" ".jpg" ""
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}\SupportedTypes" ".jpeg" ""
  WriteRegStr HKLM "${CLASSES}\Applications\${EXE}\SupportedTypes" ".png" ""
  WriteRegStr HKLM "${CLASSES}\.jpg\OpenWithProgids" "${PROGID}" ""
  WriteRegStr HKLM "${CLASSES}\.jpeg\OpenWithProgids" "${PROGID}" ""
  WriteRegStr HKLM "${CLASSES}\.png\OpenWithProgids" "${PROGID}" ""
  !insertmacro HeicType ".heic"
  !insertmacro HeicType ".heif"

  WriteRegStr HKLM "${ARP}" "DisplayName" "${FULLNAME}"
  WriteRegStr HKLM "${ARP}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "${ARP}" "URLInfoAbout" "${WEBSITE}"
  WriteRegStr HKLM "${ARP}" "HelpLink" "${WEBSITE}"
  WriteRegStr HKLM "${ARP}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${ARP}" "DisplayIcon" "$INSTDIR\${EXE}"
  WriteRegStr HKLM "${ARP}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${ARP}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "${ARP}" "NoModify" 1
  WriteRegDWORD HKLM "${ARP}" "NoRepair" 1

  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  !insertmacro UnHeicType ".heic"
  !insertmacro UnHeicType ".heif"
  DeleteRegValue HKLM "${CLASSES}\.jpg\OpenWithProgids" "${PROGID}"
  DeleteRegValue HKLM "${CLASSES}\.jpeg\OpenWithProgids" "${PROGID}"
  DeleteRegValue HKLM "${CLASSES}\.png\OpenWithProgids" "${PROGID}"
  DeleteRegKey HKLM "${CLASSES}\CLSID\${CLSID}"
  DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved" "${CLSID}"
  DeleteRegKey HKLM "${CLASSES}\${PROGID}"
  DeleteRegKey HKLM "${CLASSES}\Applications\${EXE}"
  DeleteRegKey HKLM "${ARP}"
  DeleteRegKey HKLM "${STATE}"

  Delete "$DESKTOP\${APP}.lnk"
  RMDir /r "$SMPROGRAMS\${APP}"
  Delete /REBOOTOK "$INSTDIR\${EXE}"
  Delete /REBOOTOK "$INSTDIR\HeicThumb.dll"
  Delete /REBOOTOK "$INSTDIR\libheif-18c57b017b404cb1ef96dee8a6eef027.dll"
  Delete /REBOOTOK "$INSTDIR\libde265-0-da04b6b2448c39c0b7f218769dedb2f1.dll"
  Delete /REBOOTOK "$INSTDIR\libx265-217-276e7d75772a32bd086b54d54e01fd11.dll"
  Delete /REBOOTOK "$INSTDIR\libstdc++-6-e4d43a335d57132a6ec3004cece76b4f.dll"
  Delete /REBOOTOK "$INSTDIR\libgcc_s_seh-1-60817ff87b1fa8e35c12cd1a333734a3.dll"
  Delete /REBOOTOK "$INSTDIR\libwinpthread-1-71c5c74678606753e0a2f79ceb8c68f9.dll"
  Delete "$INSTDIR\Third-party licenses.txt"
  Delete "$INSTDIR\*.old"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
