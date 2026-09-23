@echo off
rem Instalador licenciado FICTICIO numa pasta com espaco no nome (testa o escape de caminhos da rede).
rem Um instalador real (ex.: pacote de implantacao da Autodesk) so instala; a ativacao da licenca e manual.
>> "%~dp0..\instalacoes-simuladas.log" echo %DATE% %TIME% %~nx0 %*
exit /b 0
