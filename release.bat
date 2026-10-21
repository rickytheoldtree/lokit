@echo off
rem The pack title below is Chinese; parse the rest of this file as UTF-8.
chcp 65001 >nul
setlocal

rem Usage: release.bat <version> [release-notes.md]
rem Builds a Velopack installer + portable zip and uploads them to GitHub Releases.
rem Clients installed from a release update themselves from Settings.

cd /d "%~dp0"
set "REPO_URL=https://github.com/rickytheoldtree/game-json-localization-wpf"
set "PACK_ID=LocalizationTranslationWPF"
set "PUBLISH_DIR=%~dp0release\publish"
set "PACK_DIR=%~dp0release\velopack"

set "VERSION=%~1"
if "%VERSION%"=="" set /p "VERSION=Version to release (e.g. 1.2.0): "
if "%VERSION%"=="" (
  echo No version given.
  goto fail
)

set "NOTES_ARG="
if not "%~2"=="" set "NOTES_ARG=--releaseNotes "%~f2""

if "%GITHUB_TOKEN%"=="" for /f "delims=" %%t in ('gh auth token 2^>nul') do set "GITHUB_TOKEN=%%t"
if "%GITHUB_TOKEN%"=="" (
  echo Set GITHUB_TOKEN or sign in with "gh auth login" first.
  goto fail
)

echo Restoring vpk...
dotnet tool restore
if errorlevel 1 goto fail

echo.
echo Publishing %VERSION%...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
dotnet publish ".\LocalizationTranslationWPF.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained false ^
  -p:Version=%VERSION% ^
  -o "%PUBLISH_DIR%"
if errorlevel 1 goto fail

echo.
echo Downloading the previous release so a delta package can be built...
dotnet vpk download github --repoUrl "%REPO_URL%" --token "%GITHUB_TOKEN%" -o "%PACK_DIR%"
if errorlevel 1 echo No previous release found, building a full package only.

echo.
echo Packing...
dotnet vpk pack ^
  --packId %PACK_ID% ^
  --packVersion %VERSION% ^
  --packTitle "本地化工具" ^
  --packDir "%PUBLISH_DIR%" ^
  --mainExe LocalizationTranslationWPF.exe ^
  --runtime win-x64 ^
  --framework net8-x64-desktop ^
  --outputDir "%PACK_DIR%" ^
  %NOTES_ARG%
if errorlevel 1 goto fail

echo.
echo Packages are in "%PACK_DIR%".
choice /m "Upload v%VERSION% to GitHub Releases now"
if errorlevel 2 goto done

dotnet vpk upload github ^
  --repoUrl "%REPO_URL%" ^
  --token "%GITHUB_TOKEN%" ^
  --outputDir "%PACK_DIR%" ^
  --tag v%VERSION% ^
  --releaseName "v%VERSION%" ^
  --publish
if errorlevel 1 goto fail

echo.
echo Released v%VERSION%: %REPO_URL%/releases/tag/v%VERSION%
goto done

:fail
echo.
echo Release failed. See the errors above.
pause
exit /b 1

:done
pause
endlocal
