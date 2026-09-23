@echo off
rem Instalador FICTICIO para testar a fila de instalacao. Nao instala nada.
rem Registra nome e parametros recebidos e simula alguns segundos de trabalho.
echo %DATE% %TIME% %~nx0 %*>> "%~dp0instalacoes-simuladas.log"
ping -n 3 127.0.0.1 >nul
exit /b 3010
