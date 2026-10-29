@echo off
rem Builds LoafLock with the C# compiler that comes with Windows (.NET Framework 4).
rem Nothing to install. Run "build.cmd test" to build and run the tests too.
rem Double-clicked, the window stays open at the end so the result can be read.
rem Keep this file plain ASCII with CRLF line endings, or cmd misreads it.
setlocal
cd /d "%~dp0"
call :build %1
set "RC=%ERRORLEVEL%"
if "%~1"=="" echo %cmdcmdline% | "%WINDIR%\System32\find.exe" /i "%~nx0" >nul && pause
exit /b %RC%

:build
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo Can't find the C# compiler that comes with Windows ^(.NET Framework 4^).
    exit /b 1
)

rem A running LoafLock.exe can't be replaced: say so plainly instead of a compiler error.
if exist bin\LoafLock.exe (
    2>nul (>>bin\LoafLock.exe call ) || (
        echo LoafLock is running from bin\LoafLock.exe. Quit it first: right-click Rukus, Quit LoafLock.
        exit /b 1
    )
)

rem The build stamp: the commit GitHub builds from (its first 7 characters), or "dev" for a
rem build made anywhere else. The exe shows it as "LoafLock v0.7 - Build: abc1234".
set "BUILD=dev"
if defined LOAFLOCK_BUILD set "BUILD=%LOAFLOCK_BUILD:~0,7%"
if not exist bin\obj mkdir bin\obj
> bin\obj\BuildInfo.cs echo namespace LoafLock { static class BuildInfo { public const string Commit = "%BUILD%"; } }

rem /codepage:65001 reads the source as UTF-8 (it has a few non-ASCII characters)
set "OPTS=/nologo /debug- /codepage:65001 /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll"
set "ICONS=/resource:assets\loaflock.ico,LoafLock.loaflock.ico /resource:assets\loaflock-locked.ico,LoafLock.loaflock-locked.ico"

"%CSC%" %OPTS% %ICONS% /optimize+ /target:winexe /win32icon:assets\loaflock.ico /win32manifest:assets\LoafLock.manifest /out:bin\LoafLock.exe src\*.cs bin\obj\BuildInfo.cs
if errorlevel 1 exit /b 1

"%CSC%" %OPTS% %ICONS% /target:exe /win32manifest:assets\LoafLock.manifest /main:LoafLock.Tests.TestMain /out:bin\LoafLockTests.exe src\*.cs bin\obj\BuildInfo.cs tests\*.cs
if errorlevel 1 exit /b 1

echo Built bin\LoafLock.exe, build %BUILD%
if /i "%~1"=="test" bin\LoafLockTests.exe
exit /b %ERRORLEVEL%
