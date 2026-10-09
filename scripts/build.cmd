@echo off
rem Builds micproc.dll (WebRTC APM + RNNoise) and runs the tests. x64 Release, MSVC.
rem Usage: scripts\build.cmd [--reconfigure]
rem Output: build\micproc.dll, build\micproc_test.exe, build\apm_smoke.exe
setlocal

set ROOT=%~dp0..
set BUILD=%ROOT%\build

rem Meson/Ninja installed via "pip install --user meson ninja"
set PATH=%APPDATA%\Python\Python312\Scripts;%LOCALAPPDATA%\Programs\Python\Python312;%PATH%

rem vcvars64.bat calls vswhere.exe via PATH
set PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%
for /f "usebackq tokens=*" %%i in (`vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSINSTALL=%%i
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fetch-rnnoise-model.ps1" || exit /b 1

if not exist "%BUILD%\build.ninja" (
  meson setup "%BUILD%" "%ROOT%" --backend=ninja || exit /b 1
) else if "%1"=="--reconfigure" (
  meson setup --reconfigure "%BUILD%" "%ROOT%" || exit /b 1
)
meson compile -C "%BUILD%" || exit /b 1
meson test -C "%BUILD%" --print-errorlogs || exit /b 1
