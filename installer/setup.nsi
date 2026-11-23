; WiFiBandSelector — installateur NSIS 3 (Unicode, français)
; Construit par build.ps1 : dotnet publish puis makensis installer\setup.nsi

Unicode true

!define APP_NAME "WiFiBandSelector"
!ifndef APP_VERSION
!define APP_VERSION "0.1.0"
!endif
!define APP_PUBLISHER "Matthieu Perreira Da Silva"
!define APP_EXE "WiFiBandSelector.exe"
!define TASK_NAME "WiFiBandSelector"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\WiFiBandSelector"
!define RUNTIME_URL "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
!define RUNTIME_DIR "$PROGRAMFILES64\dotnet\shared\Microsoft.WindowsDesktop.App"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\dist\WiFiBandSelector-Setup-${APP_VERSION}.exe"
InstallDir "$PROGRAMFILES64\${APP_NAME}"
InstallDirRegKey HKLM "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "StrFunc.nsh"
${Using:StrFunc} StrRep

!define MUI_ABORTWARNING
!define MUI_ICON "..\assets\app.ico"
!define MUI_UNICON "..\assets\app.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Lancer WiFiBandSelector"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "French"

Var runtimeSource
Var runtimeExe
Var retryCount
Var taskXmlPath

; ---------------------------------------------------------------------------
; Paramètres : /RUNTIME=<chemin> permet d'installer depuis un fichier local.
; ---------------------------------------------------------------------------
Function .onInit
  ${GetParameters} $0
  ${GetOptions} $0 "/RUNTIME=" $runtimeSource
  StrCpy $runtimeExe "$PLUGINSDIR\windowsdesktop-runtime.exe"
FunctionEnd

; ---------------------------------------------------------------------------
; .NET 8 Desktop Runtime : déjà présent, fichier fourni, ou téléchargement.
; ---------------------------------------------------------------------------
Function EnsureRuntime
  ClearErrors
  FindFirst $1 $2 "${RUNTIME_DIR}\8.*"
  ${IfNot} ${Errors}
    FindClose $1
    DetailPrint "Runtime .NET 8 Desktop déjà présent."
    Return
  ${EndIf}

  ${If} $runtimeSource != ""
    ${IfNot} ${FileExists} $runtimeSource
      MessageBox MB_ICONSTOP|MB_OK "Le fichier runtime indiqué est introuvable :$\n$runtimeSource"
      Abort
    ${EndIf}
    DetailPrint "Installation du runtime depuis $runtimeSource"
    StrCpy $runtimeExe $runtimeSource
    Call InstallRuntime
    Return
  ${EndIf}

  ; Téléchargement avec WinINet (plugin inetc), avec deux reprises.
  InitPluginsDir
  StrCpy $retryCount 0
DownloadLoop:
  DetailPrint "Téléchargement du runtime .NET 8 Desktop…"
  inetc::get /CAPTION "WiFiBandSelector" /BANNER "Téléchargement du runtime .NET 8$\nVeuillez patienter…" "${RUNTIME_URL}" "$runtimeExe" /END
  Pop $0
  ${If} $0 == "OK"
    ${If} ${FileExists} $runtimeExe
      Call InstallRuntime
    ${EndIf}
    Return
  ${EndIf}

  IntOp $retryCount $retryCount + 1
  ${If} $retryCount < 3
    Goto DownloadLoop
  ${EndIf}

  ${If} ${Silent}
    DetailPrint "Échec du téléchargement du runtime .NET 8 ($0). Installation interrompue."
    Abort
  ${EndIf}

  MessageBox MB_YESNOCANCEL|MB_ICONEXCLAMATION "Téléchargement du runtime .NET 8 impossible ($0).$\nOui : réessayer.$\nNon : ouvrir la page de téléchargement.$\nAnnuler : abandonner." IDYES DownloadLoop IDNO OpenPage
  Abort
OpenPage:
  ExecShell "open" "https://dotnet.microsoft.com/download/dotnet/8.0"
  Abort
FunctionEnd

Function InstallRuntime
  DetailPrint "Installation silencieuse du runtime .NET 8 Desktop…"
  nsExec::ExecToStack '"$runtimeExe" /install /quiet /norestart'
  Pop $0
  Pop $1
  ${If} $0 == "0"
  ${OrIf} $0 == "3010"
    DetailPrint "Runtime .NET 8 installé (code $0)."
  ${Else}
    MessageBox MB_ICONSTOP|MB_OK "L'installation du runtime .NET 8 a échoué (code $0).$\n$1"
    Abort
  ${EndIf}
FunctionEnd

; ---------------------------------------------------------------------------
; La tâche doit contenir le chemin réel choisi dans l'assistant. Le modèle est
; relu ligne par ligne, réécrit en UTF-16 avec BOM (format attendu par schtasks).
; ---------------------------------------------------------------------------
Function WriteTaskXml
  InitPluginsDir
  ClearErrors
  FileOpen $3 "$INSTDIR\task.xml" r
  IfErrors TaskReadFailed
  StrCpy $4 ""
TaskReadLoop:
  ClearErrors
  FileRead $3 $5
  IfErrors TaskReadDone
  StrCpy $4 "$4$5"
  Goto TaskReadLoop
TaskReadDone:
  FileClose $3
  ${StrRep} $6 $4 "__INSTALLDIR__" "$INSTDIR"

  ClearErrors
  FileOpen $3 "$PLUGINSDIR\WiFiBandSelector-task.xml" w
  IfErrors TaskWriteFailed
  FileWriteUTF16LE /BOM $3 $6
  FileClose $3
  StrCpy $taskXmlPath "$PLUGINSDIR\WiFiBandSelector-task.xml"
  Return

TaskReadFailed:
TaskWriteFailed:
  MessageBox MB_ICONSTOP|MB_OK "Impossible de préparer la tâche planifiée (modèle illisible)."
  Abort
FunctionEnd

; ---------------------------------------------------------------------------
; Installation
; ---------------------------------------------------------------------------
Section "Installation de ${APP_NAME}" SecMain
  SectionIn RO
  SetRegView 64

  Call EnsureRuntime

  CreateDirectory "$INSTDIR"
  SetOutPath "$INSTDIR"
  File "..\publish\${APP_EXE}"
  File "..\installer\task.xml"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" 128

  Call WriteTaskXml

  DetailPrint "Création de la tâche planifiée ${TASK_NAME}…"
  nsExec::ExecToStack 'schtasks /Create /TN "${TASK_NAME}" /XML "$taskXmlPath" /F'
  Pop $0
  Pop $1
  ${If} $0 != "0"
    DetailPrint "Échec de la création de la tâche (code $0) : $1"
    ${IfNot} ${Silent}
      MessageBox MB_ICONEXCLAMATION|MB_OK "La tâche de démarrage n'a pas pu être créée (code $0).$\nL'application est installée mais ne démarrera pas automatiquement.$\n$1"
    ${EndIf}
  ${EndIf}
SectionEnd

; ---------------------------------------------------------------------------
; Désinstallation
; ---------------------------------------------------------------------------
Section "Uninstall"
  SetRegView 64

  DetailPrint "Suppression de la tâche planifiée ${TASK_NAME}…"
  nsExec::ExecToStack 'schtasks /Delete /TN "${TASK_NAME}" /F'
  Pop $0
  Pop $1

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\task.xml"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKLM "${UNINSTALL_KEY}"

  ${IfNot} ${Silent}
    MessageBox MB_YESNO|MB_ICONQUESTION "Supprimer aussi les préférences et le journal de WiFiBandSelector ?" IDNO KeepData
    RMDir /r "$LOCALAPPDATA\WiFiBandSelector"
  ${EndIf}
KeepData:
SectionEnd
