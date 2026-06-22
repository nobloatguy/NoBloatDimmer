@echo off
setlocal

set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.1.0-beta.1
set OUTPUT=publish\win-x64
set ARCHIVE=publish\NoBloatDimmer-%VERSION%-win-x64.zip

rmdir /s /q "%OUTPUT%" 2>nul
mkdir "%OUTPUT%"

dotnet publish NoBloatDimmer.csproj -c Release -r win-x64 --self-contained true -p:Version=%VERSION% -p:PublishSingleFile=true -p:PublishTrimmed=false -o "%OUTPUT%"
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -Command "Compress-Archive -Path '%OUTPUT%\\*' -DestinationPath '%ARCHIVE%' -Force"
powershell -NoProfile -Command "Get-FileHash '%ARCHIVE%' -Algorithm SHA256 | Format-List"

echo.
echo Upload the ZIP and the SHA256 value to a GitHub Release tagged v%VERSION%.
endlocal
