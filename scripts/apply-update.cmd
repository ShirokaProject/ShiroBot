@echo off
REM scripts/apply-update.cmd
REM 用法: apply-update.cmd <pid> <entry-assembly-path>
REM
REM 工作流：
REM   1) 等待主进程退出
REM   2) 把 update\ 下的 asset 解压/复制到运行目录
REM   3) 重启 dotnet <entry-assembly-path>
REM 仅处理 update\pending-update.toml 描述的目标。

setlocal EnableDelayedExpansion

if "%~1"=="" goto :usage
if "%~2"=="" goto :usage

set "PID=%~1"
set "ENTRY=%~2"

for %%I in ("%ENTRY%") do set "APP_DIR=%%~dpI"
set "APP_DIR=%APP_DIR:~0,-1%"
set "UPDATE_DIR=%APP_DIR%\update"
set "MANIFEST=%UPDATE_DIR%\pending-update.toml"

if not exist "%MANIFEST%" (
    echo [apply-update] 未找到 %MANIFEST%，跳过升级。
    exit /b 1
)

set "ASSET="
for /f "usebackq tokens=1,2 delims==" %%A in ("%MANIFEST%") do (
    set "K=%%A"
    set "K=!K: =!"
    if /i "!K!"=="asset" (
        set "V=%%B"
        set "V=!V: =!"
        set "V=!V:"=!"
        set "ASSET=!V!"
    )
)

if "%ASSET%"=="" (
    echo [apply-update] manifest 缺少 asset 字段。
    exit /b 1
)

set "ASSET_PATH=%UPDATE_DIR%\%ASSET%"
if not exist "%ASSET_PATH%" (
    echo [apply-update] 缺少待安装文件: %ASSET_PATH%
    exit /b 1
)

echo [apply-update] 等待主进程 %PID% 退出...
set /a count=0
:wait_loop
tasklist /FI "PID eq %PID%" 2>NUL | find "%PID%" >NUL
if errorlevel 1 goto :pid_gone
set /a count+=1
if %count% GEQ 60 goto :force_kill
timeout /t 1 /nobreak >nul
goto :wait_loop

:force_kill
echo [apply-update] 主进程仍在运行，强制结束。
taskkill /PID %PID% /T /F >nul 2>&1
timeout /t 3 /nobreak >nul

:pid_gone
echo [apply-update] 部署 %ASSET% ...

set "EXT=%ASSET:~-4%"
if /i "%EXT%"==".zip" (
    powershell -NoProfile -Command "Expand-Archive -LiteralPath '%ASSET_PATH%' -DestinationPath '%APP_DIR%' -Force"
) else if /i "%EXT%"==".dll" (
    copy /Y "%ASSET_PATH%" "%APP_DIR%\" >nul
) else (
    copy /Y "%ASSET_PATH%" "%APP_DIR%\" >nul
)

del /q "%MANIFEST%" >nul 2>&1

echo [apply-update] 重启 ShiroBot ...
pushd "%APP_DIR%"
start "" /b dotnet "%ENTRY%"
popd
echo [apply-update] 完成。
endlocal
exit /b 0

:usage
echo 用法: %~nx0 ^<pid^> ^<entry-assembly-path^>
exit /b 64
