@echo off
echo =======================================================
echo          SqlXmlAnalyzer 本地打包与发布脚本
echo =======================================================
echo.
echo 正在还原依赖项...
dotnet restore

echo.
echo 正在将应用程序打包为单文件 (win-x64, Self-Contained)...
dotnet publish SqlXmlAnalyzer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true -o .\publish\win-x64

echo.
echo 打包完成！输出目录：
echo %CD%\publish\win-x64
echo.
echo 包含的文件：
dir /b .\publish\win-x64\SqlXmlAnalyzer.exe
echo.
echo 请将 .\publish\win-x64\SqlXmlAnalyzer.exe 发送给用户，该程序可在没有安装 .NET 8.0 的 Windows 机器上直接运行！
pause
