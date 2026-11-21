; Installateur SEKAI UTAU.
; Compilation : bin\build-installer.ps1 (ou makensis /DPRODUCT_VERSION=1.0.23 SekaiUtau-installer.nsi)
;
; Installation par utilisateur (pas de droits administrateur) mais le dossier reste
; modifiable dans la page "Dossier d'installation".

Unicode true
ManifestDPIAware true
RequestExecutionLevel user

!include "MUI2.nsh"

!ifndef PRODUCT_VERSION
  !define PRODUCT_VERSION "1.0.23"
!endif

!define PRODUCT_NAME "SEKAI UTAU"
!define PRODUCT_SHORT_NAME "SekaiUtau"
!define PRODUCT_PUBLISHER "SEKAI UTAU"
!define PRODUCT_WEB_SITE "https://lucasperrin3010-lang.github.io/SEKAI-UTAU/"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_SHORT_NAME}"
!define PRODUCT_SOURCE "bin\win-x64"

Name "${PRODUCT_NAME} ${PRODUCT_VERSION}"
OutFile "bin\SEKAI-UTAU-Setup-${PRODUCT_VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\SekaiUtau"
InstallDirRegKey HKCU "Software\${PRODUCT_SHORT_NAME}" "InstallDir"
ShowInstDetails show
ShowUnInstDetails show
SetCompressor /SOLID lzma

!define MUI_ABORTWARNING
!define MUI_ICON "SekaiUtau\Assets\sekai-utau.ico"
!define MUI_UNICON "SekaiUtau\Assets\sekai-utau.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\SekaiUtau.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer SEKAI UTAU"
!define MUI_FINISHPAGE_LINK "Page du projet"
!define MUI_FINISHPAGE_LINK_LOCATION "${PRODUCT_WEB_SITE}"

!insertmacro MUI_PAGE_WELCOME
; Page de choix du dossier : l'utilisateur peut installer ou il veut.
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"
!insertmacro MUI_LANGUAGE "English"

Section "SEKAI UTAU" SEC_MAIN
  SetOutPath "$INSTDIR"
  SetOverwrite ifnewer
  ; Les voix telechargees, le cache, les journaux et les preferences de la machine
  ; de build ne partent pas dans l'installateur.
  File /r /x Singers /x Cache /x Logs /x Backups /x UCache /x prefs.json /x WhatsNewSeen.txt /x "*.log" /x "*.pdb" "${PRODUCT_SOURCE}\*.*"

  WriteRegStr HKCU "Software\${PRODUCT_SHORT_NAME}" "InstallDir" "$INSTDIR"
SectionEnd

Section "Raccourcis" SEC_SHORTCUTS
  CreateDirectory "$SMPROGRAMS\SEKAI UTAU"
  CreateShortCut "$SMPROGRAMS\SEKAI UTAU\SEKAI UTAU.lnk" "$INSTDIR\SekaiUtau.exe"
  CreateShortCut "$SMPROGRAMS\SEKAI UTAU\Desinstaller.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortCut "$DESKTOP\SEKAI UTAU.lnk" "$INSTDIR\SekaiUtau.exe"
SectionEnd

Section "Associations de fichiers" SEC_ASSOC
  ; Projets SEKAI UTAU
  WriteRegStr HKCU "Software\Classes\.sekaiu" "" "SekaiUtau.Project"
  WriteRegStr HKCU "Software\Classes\.sekaiu" "Content Type" "application/x-sekaiu"
  WriteRegStr HKCU "Software\Classes\SekaiUtau.Project" "" "Projet SEKAI UTAU"
  WriteRegStr HKCU "Software\Classes\SekaiUtau.Project\DefaultIcon" "" "$INSTDIR\sekaiu.ico,0"
  WriteRegStr HKCU "Software\Classes\SekaiUtau.Project\shell\open\command" "" '"$INSTDIR\SekaiUtau.exe" "%1"'
  ; Projets USTX (format historique, toujours pris en charge)
  WriteRegStr HKCU "Software\Classes\.ustx" "" "SekaiUtau.Sequence"
  WriteRegStr HKCU "Software\Classes\SekaiUtau.Sequence" "" "Sequence SEKAI UTAU"
  WriteRegStr HKCU "Software\Classes\SekaiUtau.Sequence\DefaultIcon" "" "$INSTDIR\SekaiUtau.exe,0"
  WriteRegStr HKCU "Software\Classes\SekaiUtau.Sequence\shell\open\command" "" '"$INSTDIR\SekaiUtau.exe" "%1"'
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "$(^Name)"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\SekaiUtau.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1
SectionEnd

Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
  ; Desinstallation silencieuse (/S) : pas de question, sinon elle attend un clic.
  IfSilent unSilentOk
  MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 \
    "Desinstaller $(^Name) ? Vos projets et vos voix dans le dossier de donnees ne sont pas supprimes." \
    IDYES unSilentOk
  Abort
  unSilentOk:
FunctionEnd

Section "Uninstall"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR"

  Delete "$SMPROGRAMS\SEKAI UTAU\SEKAI UTAU.lnk"
  Delete "$SMPROGRAMS\SEKAI UTAU\Desinstaller.lnk"
  RMDir "$SMPROGRAMS\SEKAI UTAU"
  Delete "$DESKTOP\SEKAI UTAU.lnk"

  DeleteRegKey HKCU "Software\Classes\.sekaiu"
  DeleteRegKey HKCU "Software\Classes\SekaiUtau.Project"
  DeleteRegKey HKCU "Software\Classes\.ustx"
  DeleteRegKey HKCU "Software\Classes\SekaiUtau.Sequence"
  DeleteRegKey HKCU "Software\${PRODUCT_SHORT_NAME}"
  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
  SetAutoClose true
SectionEnd
