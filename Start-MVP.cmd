@echo off
setlocal
set "DOTNET_ROOT=D:\tools\dotnet10"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "WINAPP_CLI_TELEMETRY_OPTOUT=1"
"%DOTNET_ROOT%\dotnet.exe" run --project "%~dp0ClinicCameraCapture.csproj" -c Debug -p:Platform=x64
if errorlevel 1 pause
