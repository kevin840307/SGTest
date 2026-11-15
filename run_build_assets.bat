@echo off
setlocal EnableExtensions
chcp 65001 >nul
if "%~1"=="" (
  echo Usage: run_build_assets.bat "C:\path\to\original\HK2 game"
  exit /b 2
)
where python >nul 2>nul
if errorlevel 1 (
  echo ERROR: Python not available in PATH.
  exit /b 2
)
python "%~dp0tools\asset_converter\cli.py" build %*
exit /b %errorlevel%
