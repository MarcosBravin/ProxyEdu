; ProxyEdu - Instalador NSIS (Cliente / Servidor)
; Requisitos para compilar:
; - Publish atualizado em artifacts\publish\client e artifacts\publish\server
; - NSIS 3.x + MUI2

Unicode true
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "Sections.nsh"
!include "x64.nsh"

!define APP_NAME "ProxyEdu"
!ifndef APP_VERSION
!define APP_VERSION "1.1.7"
!endif
!define APP_PUBLISHER "ProxyEdu"
!define APP_EXE "ProxyEduInstaller.exe"
!define APP_ICON "..\Focus_Proxy.ico"

!define ROOT_INSTALL_DIR "$PROGRAMFILES64\ProxyEdu"
!define CLIENT_BASE_DIR "$PROGRAMFILES64\ProxyEdu\Client"
!define SERVER_BASE_DIR "$PROGRAMFILES64\ProxyEdu\Server"
!define CLIENT_INSTALL_DIR "${CLIENT_BASE_DIR}\${APP_VERSION}"
!define SERVER_INSTALL_DIR "${SERVER_BASE_DIR}\${APP_VERSION}"

!define CLIENT_SERVICE_NAME "ProxyEduClient"
!define SERVER_SERVICE_NAME "ProxyEduServer"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "..\artifacts\installer\${APP_EXE}"
InstallDir "${ROOT_INSTALL_DIR}"
BrandingText "ProxyEdu - Instalador Profissional"

Icon "${APP_ICON}"
UninstallIcon "${APP_ICON}"

ShowInstDetails show
ShowUninstDetails show

!define MUI_ABORTWARNING
!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!insertmacro MUI_PAGE_WELCOME
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ComponentsLeave
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "PortugueseBR"

!macro RunChecked CMD STEP
  DetailPrint "${STEP}"
  ClearErrors
  ExecWait '${CMD}' $0
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "${STEP}$\r$\nFalha ao executar comando do sistema."
    Abort
  ${EndIf}
  ${If} $0 <> 0
    MessageBox MB_ICONSTOP "${STEP}$\r$\nCodigo de saida: $0"
    Abort
  ${EndIf}
!macroend

Section /o "Limpeza antiga (opcional)" SEC_LEGACY_CLEAN
  ; para e remove servicos legados (se existirem)
  ExecWait 'sc.exe failure "${CLIENT_SERVICE_NAME}" reset= 0 actions= ""'
  ExecWait 'sc.exe stop "${CLIENT_SERVICE_NAME}"'
  ExecWait 'taskkill /F /IM ProxyEdu.Client.exe'
  ExecWait 'sc.exe delete "${CLIENT_SERVICE_NAME}"'

  ExecWait 'sc.exe failure "${SERVER_SERVICE_NAME}" reset= 0 actions= ""'
  ExecWait 'sc.exe stop "${SERVER_SERVICE_NAME}"'
  ExecWait 'taskkill /F /IM ProxyEdu.Server.exe'
  ExecWait 'sc.exe delete "${SERVER_SERVICE_NAME}"'

  ; limpa proxy do usuario atual
  ExecWait 'reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f'
  ExecWait 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f'
  ExecWait 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f'
  ExecWait 'netsh winhttp reset proxy'

  ; remove caminhos legados comuns (apenas instalacao)
  RMDir /r "$PROGRAMFILES\ProxyEdu.Client"
  RMDir /r "$PROGRAMFILES\ProxyEdu.Server"
  RMDir /r "$PROGRAMFILES64\ProxyEdu.Client"
  RMDir /r "$PROGRAMFILES64\ProxyEdu.Server"
  RMDir /r "${CLIENT_BASE_DIR}"
  RMDir /r "${SERVER_BASE_DIR}"
SectionEnd

Section "Cliente (Windows Service)" SEC_CLIENT
  ; para servico existente e remove antes de copiar arquivos
  ExecWait 'sc.exe failure "${CLIENT_SERVICE_NAME}" reset= 0 actions= ""'
  ExecWait 'sc.exe stop "${CLIENT_SERVICE_NAME}"'
  Sleep 1500
  ExecWait 'taskkill /F /IM ProxyEdu.Client.exe'
  ExecWait 'sc.exe delete "${CLIENT_SERVICE_NAME}"'
  Sleep 2500

  SetOutPath "${CLIENT_INSTALL_DIR}"
  File /r "..\artifacts\publish\client\*.*"

  ; cria/atualiza servico do cliente
  !insertmacro RunChecked 'sc.exe create "${CLIENT_SERVICE_NAME}" binPath= "\"${CLIENT_INSTALL_DIR}\ProxyEdu.Client.exe\"" start= auto type= own DisplayName= "ProxyEdu Client"' "Criando servico do cliente"
  ExecWait 'sc.exe description "${CLIENT_SERVICE_NAME}" "ProxyEdu Client Service"'
  ExecWait 'sc.exe failure "${CLIENT_SERVICE_NAME}" reset= 86400 actions= restart/5000/restart/5000/restart/15000'
  ExecWait 'sc.exe failureflag "${CLIENT_SERVICE_NAME}" 1'
  ExecWait 'sc.exe sidtype "${CLIENT_SERVICE_NAME}" unrestricted'
  !insertmacro RunChecked 'sc.exe start "${CLIENT_SERVICE_NAME}"' "Iniciando servico do cliente"

  ; Firewall rules para o cliente - todas as redes (Private, Public, Domain)
  ; O cliente precisa enviar broadcasts UDP para descobrir o servidor
  ExecWait 'netsh advfirewall firewall add rule name="ProxyEdu Client Discovery (50505)" dir=out action=allow protocol=UDP localport=50505 profile=Any'

  CreateDirectory "$SMPROGRAMS\ProxyEdu"
  CreateShortcut "$SMPROGRAMS\ProxyEdu\Desinstalar ProxyEdu.lnk" "$INSTDIR\Uninstall.exe" "" "${APP_ICON}" 0
SectionEnd

Section "Servidor (Windows Service + Dashboard)" SEC_SERVER
  ; para servico existente e remove antes de copiar arquivos
  ExecWait 'sc.exe failure "${SERVER_SERVICE_NAME}" reset= 0 actions= ""'
  ExecWait 'sc.exe stop "${SERVER_SERVICE_NAME}"'
  Sleep 1500
  ExecWait 'taskkill /F /IM ProxyEdu.Server.exe'
  ExecWait 'sc.exe delete "${SERVER_SERVICE_NAME}"'
  Sleep 2500

  SetOutPath "${SERVER_INSTALL_DIR}"
  File /r "..\artifacts\publish\server\*.*"

  ; cria/atualiza servico do servidor
  !insertmacro RunChecked 'sc.exe create "${SERVER_SERVICE_NAME}" binPath= "\"${SERVER_INSTALL_DIR}\ProxyEdu.Server.exe\"" start= auto type= own DisplayName= "ProxyEdu Server"' "Criando servico do servidor"
  ExecWait 'sc.exe description "${SERVER_SERVICE_NAME}" "ProxyEdu Server Service"'
  ExecWait 'sc.exe failure "${SERVER_SERVICE_NAME}" reset= 86400 actions= restart/5000/restart/5000/restart/15000'
  ExecWait 'sc.exe failureflag "${SERVER_SERVICE_NAME}" 1'
  ExecWait 'sc.exe sidtype "${SERVER_SERVICE_NAME}" unrestricted'
  !insertmacro RunChecked 'sc.exe start "${SERVER_SERVICE_NAME}"' "Iniciando servico do servidor"

  ; Firewall rules para o servidor - todas as redes (Private, Public, Domain)
  ExecWait 'netsh advfirewall firewall add rule name="ProxyEdu Server Dashboard (5000)" dir=in action=allow protocol=TCP localport=5000 profile=Any'
  ExecWait 'netsh advfirewall firewall add rule name="ProxyEdu Server Proxy (8888)" dir=in action=allow protocol=TCP localport=8888 profile=Any'
  ExecWait 'netsh advfirewall firewall add rule name="ProxyEdu Server Discovery (50505)" dir=in action=allow protocol=UDP localport=50505 profile=Any'

  CreateDirectory "$SMPROGRAMS\ProxyEdu"
  CreateShortcut "$SMPROGRAMS\ProxyEdu\Dashboard ProxyEdu.lnk" "http://localhost:5000"
  CreateShortcut "$SMPROGRAMS\ProxyEdu\Desinstalar ProxyEdu.lnk" "$INSTDIR\Uninstall.exe" "" "${APP_ICON}" 0
SectionEnd

Section -PostInstall
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  ; para e remove servicos
  ExecWait 'sc.exe failure "${CLIENT_SERVICE_NAME}" reset= 0 actions= ""'
  ExecWait 'sc.exe stop "${CLIENT_SERVICE_NAME}"'
  ExecWait 'taskkill /F /IM ProxyEdu.Client.exe'
  ExecWait 'sc.exe delete "${CLIENT_SERVICE_NAME}"'

  ExecWait 'sc.exe failure "${SERVER_SERVICE_NAME}" reset= 0 actions= ""'
  ExecWait 'sc.exe stop "${SERVER_SERVICE_NAME}"'
  ExecWait 'taskkill /F /IM ProxyEdu.Server.exe'
  ExecWait 'sc.exe delete "${SERVER_SERVICE_NAME}"'

  ; limpa proxy do usuario atual, caso cliente tenha sido instalado
  ExecWait 'reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f'
  ExecWait 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f'
  ExecWait 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f'
  ExecWait 'netsh winhttp reset proxy'

  ExecWait 'netsh advfirewall firewall delete rule name="ProxyEdu Server Dashboard (5000)"'
  ExecWait 'netsh advfirewall firewall delete rule name="ProxyEdu Server Proxy (8888)"'
  ExecWait 'netsh advfirewall firewall delete rule name="ProxyEdu Server Discovery (50505)"'
  ExecWait 'netsh advfirewall firewall delete rule name="ProxyEdu Client Discovery (50505)"'

  Delete "$SMPROGRAMS\ProxyEdu\Dashboard ProxyEdu.lnk"
  Delete "$SMPROGRAMS\ProxyEdu\Desinstalar ProxyEdu.lnk"
  RMDir "$SMPROGRAMS\ProxyEdu"

  RMDir /r "${CLIENT_BASE_DIR}"
  RMDir /r "${SERVER_BASE_DIR}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
SectionEnd

LangString DESC_SEC_CLIENT ${LANG_PORTUGUESEBR} "Instala o ProxyEdu Client como servico Windows na maquina do aluno."
LangString DESC_SEC_SERVER ${LANG_PORTUGUESEBR} "Instala o ProxyEdu Server com dashboard e proxy como servico Windows."
LangString DESC_SEC_LEGACY_CLEAN ${LANG_PORTUGUESEBR} "Remove servicos e pastas antigas antes da nova instalacao."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_LEGACY_CLEAN} $(DESC_SEC_LEGACY_CLEAN)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_CLIENT} $(DESC_SEC_CLIENT)
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_SERVER} $(DESC_SEC_SERVER)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Este instalador suporta apenas Windows 64-bit."
    Abort
  ${EndIf}
FunctionEnd

Function ComponentsLeave
  SectionGetFlags ${SEC_CLIENT} $0
  IntOp $0 $0 & ${SF_SELECTED}

  SectionGetFlags ${SEC_SERVER} $1
  IntOp $1 $1 & ${SF_SELECTED}

  ${If} $0 = 0
  ${AndIf} $1 = 0
    MessageBox MB_ICONEXCLAMATION "Selecione pelo menos uma opcao: Cliente ou Servidor."
    Abort
  ${EndIf}
FunctionEnd
