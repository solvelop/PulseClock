@echo off
REM Build PulseClock.exe with the in-box .NET Framework C# compiler. No Visual
REM Studio, no NuGet, no SDK. Requires .NET Framework 4.x (on all modern Windows).
setlocal

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe. Is .NET Framework 4.x installed?
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:PulseClock.exe ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  PulseClock.cs

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Build OK. Run PulseClock.exe
endlocal
