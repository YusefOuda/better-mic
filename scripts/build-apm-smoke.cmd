@echo off
rem Builds and runs tests\apm_smoke.cc against the static APM build (run build-webrtc-apm.cmd first).
setlocal

set ROOT=%~dp0..
set APM=%ROOT%\build\webrtc-apm-install
set ABSL=%ROOT%\build\webrtc-apm\subprojects\abseil-cpp-20240722.0
set ABSL_SRC=%ROOT%\third_party\webrtc-audio-processing\subprojects\abseil-cpp-20240722.0
set OUT=%ROOT%\build\tests

set PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VSINSTALL=%%i
call "%VSINSTALL%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

if not exist "%OUT%" mkdir "%OUT%"
cl /nologo /std:c++20 /EHsc /O2 /MD /W3 ^
  /DWEBRTC_WIN /D_WIN32 /DNOMINMAX /D_USE_MATH_DEFINES /D__STDC_FORMAT_MACROS=1 ^
  /I"%APM%\include\webrtc-audio-processing-2" /I"%ABSL_SRC%" ^
  /Fo"%OUT%\\" /Fe"%OUT%\apm_smoke.exe" "%ROOT%\tests\apm_smoke.cc" ^
  /link "%APM%\lib\libwebrtc-audio-processing-2.a" "%ABSL%\*.a" winmm.lib || exit /b 1

"%OUT%\apm_smoke.exe"
