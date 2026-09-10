@echo off
rem Instalar-Mod.bat / Install-Mod.bat
rem
rem Put this next to CS2PerformancePatcher.bat and double-click it. It downloads the same
rem verified package the main launcher uses, then installs the Cs2Saver mod into the game's
rem local mods folder -- no window to navigate, no command to type.
rem
rem It carries none of the download logic itself on purpose. That lives in
rem CS2PerformancePatcher.bat, which this calls with a verb; two copies of a
rem download-and-verify routine is two copies that can disagree about what "verified" means.

setlocal
set "CS2PP_MAIN=%~dp0CS2PerformancePatcher.bat"

if not exist "%CS2PP_MAIN%" (
    echo.
    echo   Falta o arquivo CS2PerformancePatcher.bat nesta pasta.
    echo   Missing CS2PerformancePatcher.bat in this folder.
    echo.
    echo   Baixe os dois juntos em / download both together from:
    echo   https://github.com/NspxMiguel/CS2PerformancePatcher/releases/latest
    echo.
    pause
    exit /b 2
)

call "%CS2PP_MAIN%" install-mod
set "CS2PP_RESULT=%ERRORLEVEL%"

echo.
if "%CS2PP_RESULT%"=="0" (
    echo   Pronto. Abra o jogo, ative o Cs2Saver num playset e escolha um preset
    echo   na pagina de opcoes dele. Ele comeca desligado: nada muda ate voce escolher.
    echo.
    echo   Done. Open the game, enable Cs2Saver in a playset, then pick a preset on its
    echo   options page. It starts inert: nothing changes until you choose.
) else (
    echo   Nao deu certo. A mensagem acima diz o motivo.
    echo   It did not work. The message above says why.
)

echo.
pause
exit /b %CS2PP_RESULT%
