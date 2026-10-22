@echo off
rem Gera as duas versões do RESET EXPLODE a partir do código atual desta pasta:
rem   ..\KResetExplode-dist\Portatil\KResetExplode.exe  (+ LEIA-ME.txt)  -> para o pendrive (não precisa instalar nada)
rem   ..\KResetExplode-dist\KResetExplode-Setup.exe                      -> instalador, para o seu PC (precisa do Inno Setup 6)
rem Instale o Inno Setup uma vez com:  winget install JRSoftware.InnoSetup
cd /d "%~dp0"
echo Fechando o KResetExplode se estiver aberto ...
taskkill /f /im KResetExplode.exe >nul 2>&1
rmdir /s /q obj\Release >nul 2>&1
rmdir /s /q publish-install >nul 2>&1

echo Gerando a versao portatil ...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o "..\KResetExplode-dist\Portatil" -nologo -v q
if errorlevel 1 goto falhou
copy /y "LEIA-ME.txt" "..\KResetExplode-dist\Portatil\LEIA-ME.txt" >nul

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo.
    echo Inno Setup 6 nao encontrado: o instalador nao foi gerado ^(so a versao portatil^).
    echo Para gerar o instalador: winget install JRSoftware.InnoSetup
    goto pronto
)
echo Gerando o instalador ...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o publish-install -nologo -v q
if errorlevel 1 goto falhou
for /f "tokens=3 delims=<>" %%V in ('findstr /c:"<Version>" KResetExplode.csproj') do set "VERSAO=%%V"
"%ISCC%" /Q "/DAppVersion=%VERSAO%" "/DSourceDir=%~dp0publish-install" "/O%~dp0..\KResetExplode-dist" installer\KResetExplode.iss
if errorlevel 1 goto falhou

:pronto
echo.
echo Pronto: %~dp0..\KResetExplode-dist
for %%F in ("..\KResetExplode-dist\Portatil\KResetExplode.exe") do echo Portatil gerado em %%~tF (confira se e de agora^)
pause
exit /b 0

:falhou
echo.
echo FALHOU. Veja os erros acima. Os arquivos antigos NAO foram atualizados por completo.
pause
exit /b 1
