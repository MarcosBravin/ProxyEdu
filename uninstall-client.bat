@echo off
setlocal EnableExtensions

set NO_PAUSE=0
if /I "%~1"=="nopause" set NO_PAUSE=1

echo ============================================
echo  ProxyEdu Client - Desinstalador Total
echo ============================================
echo.

:: Check admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERRO: Execute como Administrador!
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)

set SERVICE_NAME=ProxyEduClient
set SCRIPT_DIR=%~dp0

echo [1/4] Parando e removendo servico...
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% equ 0 (
    sc stop %SERVICE_NAME% >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete %SERVICE_NAME% >nul 2>&1
    timeout /t 2 /nobreak >nul
    echo Servico %SERVICE_NAME% removido.
) else (
    echo Servico %SERVICE_NAME% nao encontrado.
)

echo.
echo [2/4] Encerrando processos remanescentes...
taskkill /F /IM ProxyEdu.Client.exe >nul 2>&1
echo OK.

echo.
echo [3/4] Limpando configuracao de proxy do Windows...
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyEnable /t REG_DWORD /d 0 /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyServer /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings" /v ProxyOverride /f >nul 2>&1
netsh winhttp reset proxy >nul 2>&1
echo Proxy resetado.

echo.
echo [4/4] Removendo binarios locais de publicacao...
for %%F in (
    ProxyEdu.Client.exe
    ProxyEdu.Client.dll
    ProxyEdu.Client.deps.json
    ProxyEdu.Client.runtimeconfig.json
    appsettings.json
    install-client.bat
) do (
    if exist "%SCRIPT_DIR%%%F" del /F /Q "%SCRIPT_DIR%%%F" >nul 2>&1
)
if exist "%SCRIPT_DIR%artifacts\publish\client" rmdir /S /Q "%SCRIPT_DIR%artifacts\publish\client" >nul 2>&1

for %%D in (
    ProxyEdu.Client
    ProxyEdu.Server
    ProxyEdu.Shared
) do (
    if exist "%SCRIPT_DIR%%%D\bin" (
        rmdir /S /Q "%SCRIPT_DIR%%%D\bin" >nul 2>&1
        echo Pasta removida: %%D\bin
    ) else (
        echo Pasta nao encontrada: %%D\bin
    )
)

echo.
echo ============================================
echo  Desinstalacao do cliente concluida.
echo ============================================
if "%NO_PAUSE%"=="0" pause
