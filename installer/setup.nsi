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
; Écriture directe du XML en UTF-16 avec BOM, ligne par ligne. Ne pas concaténer
; le document dans une variable NSIS : elle plafonne à 1024 caractères et le
; fichier serait tronqué (erreur « code XML mal formé » de schtasks).
; ---------------------------------------------------------------------------
Function WriteTaskXml
  InitPluginsDir
  StrCpy $taskXmlPath "$PLUGINSDIR\WiFiBandSelector-task.xml"
  ClearErrors
  FileOpen $3 "$taskXmlPath" w
  IfErrors TaskWriteFailed

  FileWriteUTF16LE /BOM $3 '<?xml version="1.0" encoding="UTF-16"?>$\r$\n'
  FileWriteUTF16LE $3 '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">$\r$\n'
  FileWriteUTF16LE $3 '  <RegistrationInfo>$\r$\n'
  FileWriteUTF16LE $3 '    <Author>${APP_PUBLISHER}</Author>$\r$\n'
  FileWriteUTF16LE $3 '    <Description>WiFiBandSelector - demarrage avec la session de chaque utilisateur</Description>$\r$\n'
  FileWriteUTF16LE $3 '  </RegistrationInfo>$\r$\n'
  FileWriteUTF16LE $3 '  <Triggers>$\r$\n'
  FileWriteUTF16LE $3 '    <LogonTrigger>$\r$\n'
  FileWriteUTF16LE $3 '      <Enabled>true</Enabled>$\r$\n'
  FileWriteUTF16LE $3 '    </LogonTrigger>$\r$\n'
  FileWriteUTF16LE $3 '  </Triggers>$\r$\n'
  FileWriteUTF16LE $3 '  <Principals>$\r$\n'
  FileWriteUTF16LE $3 '    <Principal id="Author">$\r$\n'
  FileWriteUTF16LE $3 '      <GroupId>S-1-5-32-545</GroupId>$\r$\n'
  FileWriteUTF16LE $3 '      <RunLevel>HighestAvailable</RunLevel>$\r$\n'
  FileWriteUTF16LE $3 '    </Principal>$\r$\n'
  FileWriteUTF16LE $3 '  </Principals>$\r$\n'
  FileWriteUTF16LE $3 '  <Settings>$\r$\n'
  FileWriteUTF16LE $3 '    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>$\r$\n'
  FileWriteUTF16LE $3 '    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>$\r$\n'
  FileWriteUTF16LE $3 '    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>$\r$\n'
  FileWriteUTF16LE $3 '    <AllowHardTerminate>true</AllowHardTerminate>$\r$\n'
  FileWriteUTF16LE $3 '    <StartWhenAvailable>true</StartWhenAvailable>$\r$\n'
  FileWriteUTF16LE $3 '    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>$\r$\n'
  FileWriteUTF16LE $3 '    <IdleSettings>$\r$\n'
  FileWriteUTF16LE $3 '      <StopOnIdleEnd>false</StopOnIdleEnd>$\r$\n'
  FileWriteUTF16LE $3 '      <RestartOnIdle>false</RestartOnIdle>$\r$\n'
  FileWriteUTF16LE $3 '    </IdleSettings>$\r$\n'
  FileWriteUTF16LE $3 '    <AllowStartOnDemand>true</AllowStartOnDemand>$\r$\n'
  FileWriteUTF16LE $3 '    <Enabled>true</Enabled>$\r$\n'
  FileWriteUTF16LE $3 '    <Hidden>false</Hidden>$\r$\n'
  FileWriteUTF16LE $3 '    <RunOnlyIfIdle>false</RunOnlyIfIdle>$\r$\n'
  FileWriteUTF16LE $3 '    <WakeToRun>false</WakeToRun>$\r$\n'
  FileWriteUTF16LE $3 '    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>$\r$\n'
  FileWriteUTF16LE $3 '    <Priority>7</Priority>$\r$\n'
  FileWriteUTF16LE $3 '  </Settings>$\r$\n'
  FileWriteUTF16LE $3 '  <Actions Context="Author">$\r$\n'
  FileWriteUTF16LE $3 '    <Exec>$\r$\n'
  FileWriteUTF16LE $3 '      <Command>$INSTDIR\${APP_EXE}</Command>$\r$\n'
  FileWriteUTF16LE $3 '    </Exec>$\r$\n'
  FileWriteUTF16LE $3 '  </Actions>$\r$\n'
  FileWriteUTF16LE $3 '</Task>$\r$\n'
  FileClose $3
  Return

TaskWriteFailed:
  MessageBox MB_ICONSTOP|MB_OK "Impossible de préparer la tâche planifiée."
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
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKLM "${UNINSTALL_KEY}"

  ${IfNot} ${Silent}
    MessageBox MB_YESNO|MB_ICONQUESTION "Supprimer aussi les préférences et le journal de WiFiBandSelector ?" IDNO KeepData
    RMDir /r "$LOCALAPPDATA\WiFiBandSelector"
  ${EndIf}
KeepData:
SectionEnd
