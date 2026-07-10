@echo off
setlocal

set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.2.0-beta.3
set OUTPUT=publish\win-x64
set ARCHIVE=publish\NoBloatDimmer-win-x64.zip
set VERSIONED_ARCHIVE=publish\NoBloatDimmer-%VERSION%-win-x64.zip

rmdir /s /q "%OUTPUT%" 2>nul
mkdir "%OUTPUT%"

dotnet publish NoBloatDimmer.csproj -c Release -r win-x64 --self-contained true -p:Version=%VERSION% -p:PublishSingleFile=true -p:PublishTrimmed=false -o "%OUTPUT%"
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -Command "Compress-Archive -Path '%OUTPUT%\\*' -DestinationPath '%ARCHIVE%' -Force"
powershell -NoProfile -Command "Copy-Item '%ARCHIVE%' '%VERSIONED_ARCHIVE%' -Force"
powershell -NoProfile -Command "Get-FileHash '%ARCHIVE%' -Algorithm SHA256 | Format-List"
powershell -NoProfile -Command "$size = (Get-Item '%ARCHIVE%').Length / 1MB; Write-Host ('Website size: {0:N1} MB' -f $size)"

echo.
echo Upload NoBloatDimmer-win-x64.zip and the SHA256 value to a GitHub Release tagged v%VERSION%.
echo The website's releases/latest download URL will then update automatically.
endlocal
