@echo off
rem Builds third_party\webrtc-audio-processing as a static Release library with MSVC (x64).
rem Output: build\webrtc-apm (build dir), build\webrtc-apm-install (headers + .lib)
setlocal

set ROOT=%~dp0..
set SRC=%ROOT%\third_party\webrtc-audio-processing
set BUILD=%ROOT%\build\webrtc-apm
set PREFIX=%ROOT%\build\webrtc-apm-install

rem Meson/Ninja installed via "pip install --user meson ninja"
set PATH=%APPDATA%\Python\Python312\Scripts;%LOCALAPPDATA%\Programs\Python\Python312;%PATH%

rem vcvars64.bat calls vswhere.exe via PATH
set PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSINSTALL=%%i
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

if not exist "%BUILD%\build.ninja" (
  rem C++20: upstream uses designated initializers (agc2/input_volume_stats_reporter.cc), which
  rem GCC/Clang accept in C++17 as an extension but MSVC does not. abseil must use the same std.
  meson setup "%BUILD%" "%SRC%" --backend=ninja --buildtype=release --default-library=static --prefix="%PREFIX%" ^
    -Dcpp_std=c++20 -Dabseil-cpp:cpp_std=c++20 || exit /b 1
)
meson compile -C "%BUILD%" || exit /b 1
meson install -C "%BUILD%" || exit /b 1
