@echo off
rem Instalador FICTICIO para testar a fila de instalacao. Nao instala nada.
rem Registra nome e parametros recebidos e simula alguns segundos de trabalho.
rem O redirecionamento vem antes do echo: "...=1>> arquivo" seria lido como redirecionamento do handle 1.
>> "%~dp0instalacoes-simuladas.log" echo %DATE% %TIME% %~nx0 %*
ping -n 3 127.0.0.1 >nul
exit /b 3010
