@echo off
setlocal EnableExtensions
chcp 65001 >nul
if "%~1"=="" (
  echo Usage: build_assets.bat "C:\path\to\original\HK2 game"
  echo Required: Python 3 and Pillow. The full source archive can be large.
  exit /b 2
)
where python >nul 2>nul
if errorlevel 1 (
  echo ERROR: Python not available in PATH.
  exit /b 2
)
python "%~dp0cli.py" build "%~1"
exit /b %errorlevel%