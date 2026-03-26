@echo off
setlocal EnableExtensions

set SCRIPT_DIR=%~dp0
set REPO_DIR=%SCRIPT_DIR%..
set NSI_FILE=%SCRIPT_DIR%ProxyEduInstaller.nsi
set OUT_DIR=%REPO_DIR%\artifacts\installer
set APP_VERSION=1.1.7
set APP_FILE_VERSION=1.1.7.0
set SIGN_CERT_THUMBPRINT=

:parse_args
if "%~1"=="" goto :args_done
if /I "%~1"=="-Version" (
  if "%~2"=="" goto :arg_error
  set APP_VERSION=%~2
  shift
  shift
  goto :parse_args
)
if /I "%~1"=="-FileVersion" (
  if "%~2"=="" goto :arg_error
  set APP_FILE_VERSION=%~2
  shift
  shift
  goto :parse_args
)
if /I "%~1"=="-SignThumbprint" (
  if "%~2"=="" goto :arg_error
  set SIGN_CERT_THUMBPRINT=%~2
  shift
  shift
  goto :parse_args
)
if /I "%~1"=="-Help" goto :usage
if /I "%~1"=="/?" goto :usage
echo ERRO: parametro desconhecido %~1
goto :usage

:arg_error
echo ERRO: valor ausente para %~1
goto :usage

:usage
echo.
echo Uso:
echo   installer\build-installer.bat [-Version 1.2.3] [-FileVersion 1.2.3.0] [-SignThumbprint SHA1]
echo.
echo Exemplos:
echo   installer\build-installer.bat -Version 1.1.7 -FileVersion 1.1.7.0
echo   installer\build-installer.bat -Version 1.1.7 -SignThumbprint ABCDEF123456...
exit /b 1

:args_done

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

if not exist "%REPO_DIR%\artifacts\publish\client\ProxyEdu.Client.exe" (
  echo ERRO: publish do cliente nao encontrado em artifacts\publish\client
  echo Rode: .\build.bat
  exit /b 1
)

if not exist "%REPO_DIR%\artifacts\publish\server\ProxyEdu.Server.exe" (
  echo ERRO: publish do servidor nao encontrado em artifacts\publish\server
  echo Rode: .\build.bat
  exit /b 1
)

set MAKENSIS_EXE=
if exist "%ProgramFiles(x86)%\NSIS\makensis.exe" set MAKENSIS_EXE=%ProgramFiles(x86)%\NSIS\makensis.exe
if exist "%ProgramFiles%\NSIS\makensis.exe" set MAKENSIS_EXE=%ProgramFiles%\NSIS\makensis.exe

if "%MAKENSIS_EXE%"=="" (
  for /f "delims=" %%I in ('where makensis.exe 2^>nul') do (
    set MAKENSIS_EXE=%%I
    goto :makensis_found
  )
)

:makensis_found
if "%MAKENSIS_EXE%"=="" (
  echo ERRO: makensis.exe nao encontrado.
  echo Instale o NSIS 3.x: https://nsis.sourceforge.io/Download
  exit /b 1
)

echo Compilando instalador com: %MAKENSIS_EXE%
"%MAKENSIS_EXE%" /DAPP_VERSION=%APP_VERSION% /DAPP_FILE_VERSION=%APP_FILE_VERSION% "%NSI_FILE%"
if %errorlevel% neq 0 (
  echo ERRO: falha ao compilar o instalador.
  exit /b 1
)

if not "%SIGN_CERT_THUMBPRINT%"=="" (
  set SIGNTOOL_EXE=
  for /f "delims=" %%I in ('where signtool.exe 2^>nul') do (
    set SIGNTOOL_EXE=%%I
    goto :signtool_found
  )

  :signtool_found
  if "%SIGNTOOL_EXE%"=="" (
    echo ERRO: signtool.exe nao encontrado para assinar o instalador.
    exit /b 1
  )

  echo Assinando instalador com certificado %SIGN_CERT_THUMBPRINT%...
  "%SIGNTOOL_EXE%" sign /sha1 "%SIGN_CERT_THUMBPRINT%" /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com "%OUT_DIR%\ProxyEduInstaller.exe"
  if %errorlevel% neq 0 (
    echo ERRO: falha ao assinar o instalador.
    exit /b 1
  )
)

echo.
echo OK: instalador gerado em artifacts\installer\ProxyEduInstaller.exe
echo Versao do produto: %APP_VERSION%
exit /b 0
