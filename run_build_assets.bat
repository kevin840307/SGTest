@echo off
setlocal
if "%~1"=="" (
  echo Usage: run_build_assets.bat "C:\path\to\original\HK2 game"
  exit /b 2
)
call "%~dp0tools\asset_converter\build_assets.bat" "%~1"
exit /b %errorlevel%