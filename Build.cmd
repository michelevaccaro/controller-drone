@echo off
setlocal
pushd "%~dp0"
set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Compilatore .NET Framework x64 non trovato.
    goto failed
)
if not exist dist mkdir dist
"%CSC%" /nologo /optimize+ /platform:x64 /target:exe /out:dist\ControllerDrone.exe src\*.cs
if errorlevel 1 goto failed
dist\ControllerDrone.exe --self-test
if errorlevel 1 goto failed
echo Pronto: dist\ControllerDrone.exe
if /i not "%~1"=="--ci" pause
popd
exit /b 0

:failed
if /i not "%~1"=="--ci" pause
popd
exit /b 1
