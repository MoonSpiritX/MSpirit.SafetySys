@echo off
setlocal
rem ==============================================================
rem  MSIX packaging launcher - double-click to run.
rem  Real logic lives in the .ps1 file next to this one.
rem
rem  KEEP THIS FILE AS ANSI/GBK (code page 936) WITH CRLF LINE ENDINGS,
rem  and do NOT add "chcp 65001" here. Why: cmd.exe parses .bat files
rem  using the ANSI code page. A UTF-8 copy garbles these comments and,
rem  worse, the trailing 0x82 byte of a full-width period swallows the
rem  following CR/LF - the comment then merges into the next line and
rem  the whole thing runs as a command ("is not recognized as an
rem  internal or external command").
rem ==============================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0´ò°üMSIX.ps1"
set "RC=%ERRORLEVEL%"
echo.
if not "%RC%"=="0" echo [FAILED] exit code %RC% -- see messages above.
pause
endlocal & exit /b %RC%
