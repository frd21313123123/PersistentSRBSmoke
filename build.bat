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

msbuild PersistentSRBSmoke.sln /t:Build /p:Configuration=Release
