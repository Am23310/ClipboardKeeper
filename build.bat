@echo off
rem ClipboardKeeper build: run unit tests first (TDD), then build icon + exe.
rem Keep this file ASCII-only: cmd.exe parses it in the ANSI codepage (GBK).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=-r:System.Web.Extensions.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll

echo [1/3] Running unit tests (HistoryStore behavior tests)...
%CSC% -nologo -out:selftest.exe HistoryStore.cs Program.cs TrayContext.cs %REFS%
if errorlevel 1 goto :fail
selftest.exe --selftest
if errorlevel 1 goto :fail

echo [2/3] Generating app.ico ...
%CSC% -nologo -out:icon-gen.exe icon-gen.cs -r:System.Drawing.dll
if errorlevel 1 goto :fail
icon-gen.exe
if errorlevel 1 goto :fail

echo [3/3] Compiling ClipboardKeeper.exe (icon embedded)...
%CSC% -nologo -target:winexe -win32icon:app.ico -out:ClipboardKeeper.exe HistoryStore.cs Program.cs TrayContext.cs %REFS%
if errorlevel 1 goto :fail
echo.
echo Build OK: ClipboardKeeper.exe
echo Install: copy the exe to %%LOCALAPPDATA%%\ClipboardKeeper\ and run it once
echo          (first run registers autostart automatically).
goto :eof

:fail
echo BUILD FAILED!
exit /b 1
