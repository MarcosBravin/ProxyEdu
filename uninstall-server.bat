@echo off
setlocal EnableExtensions

set NO_PAUSE=0
if /I "%~1"=="nopause" set NO_PAUSE=1

echo ============================================
echo  ProxyEdu Server - Desinstalador Total
echo ============================================
echo.

:: Check admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo ERRO: Execute como Administrador!
    if "%NO_PAUSE%"=="0" pause
    exit /b 1
)

set SERVICE_NAME=ProxyEduServer
set DATA_DIR=C:\ProgramData\ProxyEdu
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
taskkill /F /IM ProxyEdu.Server.exe >nul 2>&1
echo OK.

echo.
echo [3/4] Removendo base de dados e arquivos de dados...
if exist "%DATA_DIR%" (
    rmdir /S /Q "%DATA_DIR%"
    echo Pasta removida: %DATA_DIR%
) else (
    echo Pasta de dados nao encontrada: %DATA_DIR%
)

echo.
echo [4/4] Removendo binarios locais de publicacao...
for %%F in (
    ProxyEdu.Server.exe
    ProxyEdu.Server.dll
    ProxyEdu.Server.deps.json
    ProxyEdu.Server.runtimeconfig.json
    appsettings.json
    install-server.bat
) do (
    if exist "%SCRIPT_DIR%%%F" del /F /Q "%SCRIPT_DIR%%%F" >nul 2>&1
)
if exist "%SCRIPT_DIR%wwwroot" rmdir /S /Q "%SCRIPT_DIR%wwwroot" >nul 2>&1
if exist "%SCRIPT_DIR%artifacts\publish\server" rmdir /S /Q "%SCRIPT_DIR%artifacts\publish\server" >nul 2>&1

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
echo  Desinstalacao do servidor concluida.
echo ============================================
if "%NO_PAUSE%"=="0" pause
