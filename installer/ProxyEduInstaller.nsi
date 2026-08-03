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
!define APP_VERSION "1.1.10"
!endif
!ifndef APP_FILE_VERSION
!define APP_FILE_VERSION "1.1.10.0"
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

; Metadados de versao exibidos na aba "Detalhes" do Windows Explorer
VIProductVersion "${APP_FILE_VERSION}"
VIFileVersion "${APP_FILE_VERSION}"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileVersion" "${APP_FILE_VERSION}"
VIAddVersionKey "FileDescription" "${APP_NAME} Instalador"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "${APP_PUBLISHER}"

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

; ---------------------------------------------------------------------------
; Macros
; ---------------------------------------------------------------------------

; Executa um comando de forma totalmente silenciosa (sem janela de console),
; usado para comandos nao-criticos: stop, delete, taskkill, netsh, reg, etc.
; $0 = codigo de saida, $1 = saida de texto (stdout/stderr combinados)
!macro ExecSilent CMD
  DetailPrint `> ${CMD}`
  nsExec::ExecToStack '${CMD}'
  Pop $0
  Pop $1
!macroend

; Executa um comando critico silenciosamente e aborta a instalacao caso falhe.
; Usa nsExec::ExecToStack em vez de ExecWait para nao piscar janela de CMD.
!macro RunChecked CMD STEP
  DetailPrint "${STEP}"
  ClearErrors
  nsExec::ExecToStack '${CMD}'
  Pop $0
  Pop $1
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "${STEP}$\r$\nFalha ao executar comando do sistema."
    Abort
  ${EndIf}
  ${If} $0 <> 0
    MessageBox MB_ICONSTOP "${STEP}$\r$\nCodigo de saida: $0$\r$\n$1"
    Abort
  ${EndIf}
!macroend

; ---------------------------------------------------------------------------
; DeleteServiceWait SERVICE_NAME PROCESS_EXE
;
; Sequencia robusta de remocao:
;   1. Remove politica de auto-restart (para o SCM nao recriar o servico)
;   2. Para o servico e aguarda SERVICE_STOPPED (loop sc query STATE)
;   3. Mata forcado o processo (garante que nenhum handle fica aberto)
;   4. Aguarda o processo desaparecer do tasklist
;   5. Deleta o servico
;   6. Aguarda sc qc retornar 1060 (nome realmente liberado pelo SCM)
; ---------------------------------------------------------------------------
!macro DeleteServiceWait SERVICE_NAME PROCESS_EXE
  ; 1. remove auto-restart para o SCM nao recriar o servico apos stop
  !insertmacro ExecSilent 'sc.exe failure "${SERVICE_NAME}" reset= 0 actions= ""'

  ; 2. para o servico e aguarda estado STOPPED (maximo 15s)
  !insertmacro ExecSilent 'sc.exe stop "${SERVICE_NAME}"'
  StrCpy $R5 0
  ${Do}
    Sleep 1000
    nsExec::ExecToStack 'sc.exe query "${SERVICE_NAME}"'
    Pop $R6
    Pop $R7
    ; codigo != 0 = servico nao existe — sai cedo
    ${If} $R6 != 0
      ${ExitDo}
    ${EndIf}
    ; sc query retorna codigo 1 quando o servico esta STOPPED
    ; (0 = running/pending, 1 = stopped, outros = erros)
    ; usamos find no texto para cobrir todos os casos
    nsExec::ExecToStack 'cmd /c sc.exe query "${SERVICE_NAME}" | find "STOPPED"'
    Pop $R8
    Pop $R9
    ${If} $R8 == 0
      ${ExitDo}
    ${EndIf}
    IntOp $R5 $R5 + 1
    ${If} $R5 >= 15
      ${ExitDo}
    ${EndIf}
    DetailPrint "Aguardando parada de ${SERVICE_NAME} ($R5/15)..."
  ${Loop}

  ; 3. mata o processo para liberar qualquer handle aberto no SCM
  !insertmacro ExecSilent 'taskkill /F /IM "${PROCESS_EXE}"'

  ; 4. aguarda o processo sumir do tasklist (maximo 10s)
  StrCpy $R5 0
  ${Do}
    Sleep 500
    ; find retorna 0 se o processo ainda aparece no tasklist
    nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq ${PROCESS_EXE}" /NH | find /I "${PROCESS_EXE}"'
    Pop $R8
    Pop $R9
    ${If} $R8 == 0
      IntOp $R5 $R5 + 1
      ${If} $R5 >= 20
        DetailPrint "Aviso: processo ${PROCESS_EXE} ainda ativo apos timeout."
        ${ExitDo}
      ${EndIf}
      DetailPrint "Aguardando fim do processo ${PROCESS_EXE} ($R5/20)..."
    ${Else}
      ${ExitDo}
    ${EndIf}
  ${Loop}

  ; 5. deleta o servico
  !insertmacro ExecSilent 'sc.exe delete "${SERVICE_NAME}"'

  ; 6. aguarda sc qc retornar 1060 — nome realmente liberado pelo SCM
  StrCpy $R5 0
  ${Do}
    Sleep 1000
    nsExec::ExecToStack 'sc.exe qc "${SERVICE_NAME}"'
    Pop $R6
    Pop $R7
    ${If} $R6 != 0
      ${ExitDo}   ; 1060 = servico nao existe — nome liberado
    ${EndIf}
    IntOp $R5 $R5 + 1
    ${If} $R5 >= 30
      DetailPrint "Timeout aguardando SCM liberar ${SERVICE_NAME}."
      ${ExitDo}
    ${EndIf}
    DetailPrint "Aguardando SCM liberar ${SERVICE_NAME} ($R5/30)..."
  ${Loop}
!macroend

; ---------------------------------------------------------------------------
; CreateServiceWait SERVICE_NAME BIN_PATH DISPLAY_NAME STEP
;
; Cria o servico somente apos confirmar via sc qc que o nome foi liberado
; (codigo 1060). BIN_PATH sem aspas — a macro as adiciona corretamente.
; ---------------------------------------------------------------------------
!macro CreateServiceWait SERVICE_NAME BIN_PATH DISPLAY_NAME STEP
  DetailPrint "${STEP}"

  ; aguarda sc qc retornar 1060 antes de criar (cobre DELETE_PENDING)
  StrCpy $R5 0
  ${Do}
    nsExec::ExecToStack 'sc.exe qc "${SERVICE_NAME}"'
    Pop $R6
    Pop $R7
    ${If} $R6 != 0
      ${ExitDo}   ; nome livre — pode criar
    ${EndIf}
    IntOp $R5 $R5 + 1
    ${If} $R5 >= 30
      DetailPrint "Timeout aguardando liberacao de ${SERVICE_NAME}."
      ${ExitDo}
    ${EndIf}
    DetailPrint "Aguardando SCM liberar nome ${SERVICE_NAME} ($R5/30)..."
    Sleep 1000
  ${Loop}

  ; cria o servico — binPath entre aspas duplas para caminhos com espacos
  nsExec::ExecToStack 'sc.exe create "${SERVICE_NAME}" binPath= "\"${BIN_PATH}\"" start= auto type= own DisplayName= "${DISPLAY_NAME}"'
  Pop $0
  Pop $1
  ${If} $0 <> 0
    MessageBox MB_ICONSTOP "${STEP}$\r$\nCodigo: $0$\r$\n$1"
    Abort
  ${EndIf}
!macroend

Section /o "Limpeza antiga (opcional)" SEC_LEGACY_CLEAN
  ; para e remove servicos legados (se existirem)
  !insertmacro DeleteServiceWait "${CLIENT_SERVICE_NAME}" "ProxyEdu.Client.exe"
  !insertmacro DeleteServiceWait "${SERVER_SERVICE_NAME}" "ProxyEdu.Server.exe"

  ; limpa proxy do usuario atual
  !insertmacro ExecSilent 'reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f'
  !insertmacro ExecSilent 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f'
  !insertmacro ExecSilent 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f'
  !insertmacro ExecSilent 'netsh winhttp reset proxy'

  ; remove caminhos legados comuns (apenas instalacao)
  RMDir /r "$PROGRAMFILES\ProxyEdu.Client"
  RMDir /r "$PROGRAMFILES\ProxyEdu.Server"
  RMDir /r "$PROGRAMFILES64\ProxyEdu.Client"
  RMDir /r "$PROGRAMFILES64\ProxyEdu.Server"
  RMDir /r "${CLIENT_BASE_DIR}"
  RMDir /r "${SERVER_BASE_DIR}"
SectionEnd

Section "Cliente (Windows Service)" SEC_CLIENT
  ; para servico existente e aguarda remocao completa antes de copiar arquivos
  !insertmacro DeleteServiceWait "${CLIENT_SERVICE_NAME}" "ProxyEdu.Client.exe"

  SetOutPath "${CLIENT_INSTALL_DIR}"
  File /r "..\artifacts\publish\client\*.*"

  ; cria servico do cliente (aguarda SCM liberar o nome se necessario)
  !insertmacro CreateServiceWait "${CLIENT_SERVICE_NAME}" "${CLIENT_INSTALL_DIR}\ProxyEdu.Client.exe" "ProxyEdu Client" "Criando servico do cliente"
  !insertmacro ExecSilent 'sc.exe description "${CLIENT_SERVICE_NAME}" "ProxyEdu Client Service"'
  !insertmacro ExecSilent 'sc.exe failure "${CLIENT_SERVICE_NAME}" reset= 86400 actions= restart/5000/restart/5000/restart/15000'
  !insertmacro ExecSilent 'sc.exe failureflag "${CLIENT_SERVICE_NAME}" 1'
  !insertmacro ExecSilent 'sc.exe sidtype "${CLIENT_SERVICE_NAME}" unrestricted'
  !insertmacro RunChecked 'sc.exe start "${CLIENT_SERVICE_NAME}"' "Iniciando servico do cliente"

  ; Firewall rules para o cliente - todas as redes (Private, Public, Domain)
  ; O cliente precisa enviar broadcasts UDP para descobrir o servidor
  !insertmacro ExecSilent 'netsh advfirewall firewall add rule name="ProxyEdu Client Discovery (50505)" dir=out action=allow protocol=UDP localport=50505 profile=Any'

  CreateDirectory "$SMPROGRAMS\ProxyEdu"
  CreateShortcut "$SMPROGRAMS\ProxyEdu\Desinstalar ProxyEdu.lnk" "$INSTDIR\Uninstall.exe" "" "${APP_ICON}" 0
SectionEnd

Section "Servidor (Windows Service + Dashboard)" SEC_SERVER
  ; para servico existente e aguarda remocao completa antes de copiar arquivos
  !insertmacro DeleteServiceWait "${SERVER_SERVICE_NAME}" "ProxyEdu.Server.exe"

  SetOutPath "${SERVER_INSTALL_DIR}"
  File /r "..\artifacts\publish\server\*.*"

  ; cria servico do servidor (aguarda SCM liberar o nome se necessario)
  !insertmacro CreateServiceWait "${SERVER_SERVICE_NAME}" "${SERVER_INSTALL_DIR}\ProxyEdu.Server.exe" "ProxyEdu Server" "Criando servico do servidor"
  !insertmacro ExecSilent 'sc.exe description "${SERVER_SERVICE_NAME}" "ProxyEdu Server Service"'
  !insertmacro ExecSilent 'sc.exe failure "${SERVER_SERVICE_NAME}" reset= 86400 actions= restart/5000/restart/5000/restart/15000'
  !insertmacro ExecSilent 'sc.exe failureflag "${SERVER_SERVICE_NAME}" 1'
  !insertmacro ExecSilent 'sc.exe sidtype "${SERVER_SERVICE_NAME}" unrestricted'
  !insertmacro RunChecked 'sc.exe start "${SERVER_SERVICE_NAME}"' "Iniciando servico do servidor"

  ; Firewall rules para o servidor - todas as redes (Private, Public, Domain)
  !insertmacro ExecSilent 'netsh advfirewall firewall add rule name="ProxyEdu Server Dashboard (5000)" dir=in action=allow protocol=TCP localport=5000 profile=Any'
  !insertmacro ExecSilent 'netsh advfirewall firewall add rule name="ProxyEdu Server Proxy (8888)" dir=in action=allow protocol=TCP localport=8888 profile=Any'
  !insertmacro ExecSilent 'netsh advfirewall firewall add rule name="ProxyEdu Server Discovery (50505)" dir=in action=allow protocol=UDP localport=50505 profile=Any'

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
  !insertmacro DeleteServiceWait "${CLIENT_SERVICE_NAME}" "ProxyEdu.Client.exe"
  !insertmacro DeleteServiceWait "${SERVER_SERVICE_NAME}" "ProxyEdu.Server.exe"

  ; limpa proxy do usuario atual, caso cliente tenha sido instalado
  !insertmacro ExecSilent 'reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f'
  !insertmacro ExecSilent 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f'
  !insertmacro ExecSilent 'reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f'
  !insertmacro ExecSilent 'netsh winhttp reset proxy'

  !insertmacro ExecSilent 'netsh advfirewall firewall delete rule name="ProxyEdu Server Dashboard (5000)"'
  !insertmacro ExecSilent 'netsh advfirewall firewall delete rule name="ProxyEdu Server Proxy (8888)"'
  !insertmacro ExecSilent 'netsh advfirewall firewall delete rule name="ProxyEdu Server Discovery (50505)"'
  !insertmacro ExecSilent 'netsh advfirewall firewall delete rule name="ProxyEdu Client Discovery (50505)"'

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
