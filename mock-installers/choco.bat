@echo off
rem Substituto FICTICIO do choco.exe usado no modo simulacao. Nao baixa nem instala nada.
rem Registra o comando recebido para conferir o roteamento (winget no Win 10/11, Chocolatey no Win 7).
>> "%~dp0instalacoes-simuladas.log" echo %DATE% %TIME% choco %*
exit /b 0
