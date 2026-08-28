@echo off
setlocal

cd /d "%~dp0"
set "OUTPUT_DIR=%~dp0release\win-x64"

echo Publishing LocalizationTranslationWPF...
echo Output: "%OUTPUT_DIR%"
echo.

dotnet publish ".\LocalizationTranslationWPF.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -o "%OUTPUT_DIR%"

if errorlevel 1 (
  echo.
  echo Publish failed. See the errors above.
  pause
  exit /b 1
)

echo.
echo Publish succeeded.
echo Package the entire folder below when distributing:
echo "%OUTPUT_DIR%"
pause

endlocal
