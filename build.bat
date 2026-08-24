@echo off
setlocal

if "%KSP_DIR%"=="" (
  echo KSP_DIR is not set.
  echo Example:
  echo   set KSP_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
  exit /b 1
)

where msbuild >nul 2>nul
if errorlevel 1 (
  echo MSBuild was not found. Open PersistentSRBSmoke.sln in Visual Studio 2022 instead.
  exit /b 1
)

msbuild PersistentSRBSmoke.sln /restore /t:Build /p:Configuration=Release
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-volumetric-assets.ps1
if errorlevel 1 exit /b %errorlevel%

powershell -NoProfile -ExecutionPolicy Bypass -File tests\volumetric-smoke-contract.ps1 -RequireBundle
