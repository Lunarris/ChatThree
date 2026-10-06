@echo off
setlocal

set PROJECT=%~dp0ChatThree.csproj

echo Building Debug...
dotnet build "%PROJECT%" -c Debug
if errorlevel 1 goto :error

echo.
echo Building Release...
dotnet build "%PROJECT%" -c Release
if errorlevel 1 goto :error

echo.
echo Debug and Release builds succeeded.
exit /b 0

:error
echo.
echo Build failed.
exit /b 1
