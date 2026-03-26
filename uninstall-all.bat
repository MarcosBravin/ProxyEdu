@echo off
setlocal EnableExtensions

set NO_PAUSE=0
if /I "%~1"=="nopause" set NO_PAUSE=1

echo ============================================
echo  ProxyEdu - Desinstalador Total (All-in-one)
echo ============================================
echo.

:: Check admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERRO: Execute como Administrador!
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)

set ROOT=%~dp0
if "%ROOT:~-1%"=="\" set ROOT=%ROOT:~0,-1%
set SERVICE_CLIENT=ProxyEduClient
set SERVICE_SERVER=ProxyEduServer
set APP_NAME=ProxyEdu
set APP_PUBLISHER=ProxyEdu
set UNINSTALL_KEY=HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\%APP_NAME%
set APP_KEY=HKLM\Software\%APP_PUBLISHER%\%APP_NAME%
set UNINSTALL_KEY_WOW=HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\%APP_NAME%
set APP_KEY_WOW=HKLM\Software\WOW6432Node\%APP_PUBLISHER%\%APP_NAME%
set INSTDIR_PF=%ProgramFiles%\ProxyEdu
set INSTDIR_PF86=%ProgramFiles(x86)%\ProxyEdu
set DATA_DIR=%ProgramData%\ProxyEdu
set START_MENU_DIR=%ProgramData%\Microsoft\Windows\Start Menu\Programs\ProxyEdu
set IS_SOURCE_TREE=0
if exist "%ROOT%\ProxyEdu.sln" set IS_SOURCE_TREE=1
if exist "%ROOT%\build.bat" if exist "%ROOT%\installer\ProxyEduInstaller.nsi" set IS_SOURCE_TREE=1

if "%IS_SOURCE_TREE%"=="1" (
    echo INFO: Modo seguro ativado - pasta de projeto detectada em:
    echo       %ROOT%
)

echo [1/10] Parando processos e removendo servicos...
taskkill /F /IM ProxyEdu.Client.exe >nul 2>&1
taskkill /F /IM ProxyEdu.Server.exe >nul 2>&1
sc stop %SERVICE_CLIENT% >nul 2>&1
sc stop %SERVICE_SERVER% >nul 2>&1
timeout /t 2 /nobreak >nul
sc delete %SERVICE_CLIENT% >nul 2>&1
sc delete %SERVICE_SERVER% >nul 2>&1
echo OK.

echo.
echo [2/10] Limpando configuracao de proxy do Windows (usuario atual + usuarios carregados)...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f >nul 2>&1
for /f "tokens=*" %%S in ('reg query HKU 2^>nul ^| findstr /R /C:"S-1-5-21-" /C:"S-1-12-1-"') do (
    reg add "%%S\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul 2>&1
    reg delete "%%S\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f >nul 2>&1
    reg delete "%%S\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f >nul 2>&1
)
netsh winhttp reset proxy >nul 2>&1
echo OK.

echo.
echo [3/10] Removendo regras de firewall do ProxyEdu...
netsh advfirewall firewall delete rule name="ProxyEdu Server Dashboard (5000)" >nul 2>&1
netsh advfirewall firewall delete rule name="ProxyEdu Server Proxy (8888)" >nul 2>&1
netsh advfirewall firewall delete rule name="ProxyEdu Server Discovery (50505)" >nul 2>&1
echo OK.

echo.
echo [4/10] Removendo certificado raiz do ProxyEdu...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='SilentlyContinue';" ^
  "$stores=@('Cert:\LocalMachine\Root','Cert:\CurrentUser\Root');" ^
  "foreach($s in $stores){ Get-ChildItem $s | Where-Object { $_.Subject -like '*ProxyEdu Root CA*' -or $_.Issuer -like '*ProxyEdu*' } | Remove-Item -Force; }"
echo OK.

echo.
echo [5/10] Removendo atalhos e entradas de desinstalacao...
if exist "%START_MENU_DIR%" rmdir /S /Q "%START_MENU_DIR%" >nul 2>&1
reg delete "%UNINSTALL_KEY%" /f >nul 2>&1
reg delete "%APP_KEY%" /f >nul 2>&1
reg delete "%UNINSTALL_KEY_WOW%" /f >nul 2>&1
reg delete "%APP_KEY_WOW%" /f >nul 2>&1
echo OK.

echo.
echo [6/10] Removendo pastas instaladas e dados da maquina...
if /I not "%ROOT%"=="%INSTDIR_PF%" (
    if exist "%INSTDIR_PF%\Uninstall.exe" rmdir /S /Q "%INSTDIR_PF%" >nul 2>&1
)
if /I not "%ROOT%"=="%INSTDIR_PF86%" (
    if exist "%INSTDIR_PF86%\Uninstall.exe" rmdir /S /Q "%INSTDIR_PF86%" >nul 2>&1
)
if exist "%DATA_DIR%" rmdir /S /Q "%DATA_DIR%" >nul 2>&1
echo OK.

echo.
echo [7/10] Removendo pastas legadas conhecidas (fora da arvore do projeto)...
if /I not "%ROOT%"=="C:\ProxyEdu" (
if exist "C:\ProxyEdu\ProxyEdu.Client" rmdir /S /Q "C:\ProxyEdu\ProxyEdu.Client" >nul 2>&1
if exist "C:\ProxyEdu\ProxyEdu.Server" rmdir /S /Q "C:\ProxyEdu\ProxyEdu.Server" >nul 2>&1
if exist "C:\ProxyEdu\ProxyEdu.Shared" rmdir /S /Q "C:\ProxyEdu\ProxyEdu.Shared" >nul 2>&1
)
if exist "%ProgramFiles%\ProxyEdu.Client" rmdir /S /Q "%ProgramFiles%\ProxyEdu.Client" >nul 2>&1
if exist "%ProgramFiles%\ProxyEdu.Server" rmdir /S /Q "%ProgramFiles%\ProxyEdu.Server" >nul 2>&1
if exist "%ProgramFiles(x86)%\ProxyEdu.Client" rmdir /S /Q "%ProgramFiles(x86)%\ProxyEdu.Client" >nul 2>&1
if exist "%ProgramFiles(x86)%\ProxyEdu.Server" rmdir /S /Q "%ProgramFiles(x86)%\ProxyEdu.Server" >nul 2>&1
echo OK.

echo.
echo [8/10] Limpando artefatos de build e instalador (seguro para rebuild)...
if exist "%ROOT%\artifacts" rmdir /S /Q "%ROOT%\artifacts" >nul 2>&1
if exist "%ROOT%\ProxyEdu.Client\publish" rmdir /S /Q "%ROOT%\ProxyEdu.Client\publish" >nul 2>&1
if exist "%ROOT%\ProxyEdu.Server\publish" rmdir /S /Q "%ROOT%\ProxyEdu.Server\publish" >nul 2>&1
if exist "%ROOT%\ProxyEdu.Shared\publish" rmdir /S /Q "%ROOT%\ProxyEdu.Shared\publish" >nul 2>&1
if exist "%ROOT%\ProxyEduInstaller.exe" del /F /Q "%ROOT%\ProxyEduInstaller.exe" >nul 2>&1
echo OK.

echo.
echo [9/10] Limpando pastas bin/obj dos projetos...
for %%D in (
    ProxyEdu.Client
    ProxyEdu.Server
    ProxyEdu.Shared
) do (
    if exist "%ROOT%\%%D\bin" rmdir /S /Q "%ROOT%\%%D\bin" >nul 2>&1
    if exist "%ROOT%\%%D\obj" rmdir /S /Q "%ROOT%\%%D\obj" >nul 2>&1
)
echo OK.

echo.
echo [10/10] Limpeza concluida.
if "%IS_SOURCE_TREE%"=="1" (
    echo Fonte preservada: somente artefatos, servicos e dados foram limpos.
)

echo.
echo ============================================
echo  Processo de desinstalacao finalizado.
echo ============================================
if "%NO_PAUSE%"=="0" pause
