@echo off
REM Windows 10/11 发布脚本（.NET Framework 4.8，系统自带，无需安装运行时）
REM 产物：publish\小工具Lite.exe，直接双击运行
REM 需要本机安装 .NET SDK（任意版本，6 以上即可）用于编译
dotnet publish src\ProjectRecorderLite\ProjectRecorderLite.csproj -c Release -o publish
del /q publish\*.pdb 2>nul
del /q publish\*.config 2>nul
echo.
echo 发布完成：publish\小工具Lite.exe
echo 数据文件：exe 同级 Data\data.json（明文 JSON，无加密）
pause
