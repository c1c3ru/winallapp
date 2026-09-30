# Instalador FICTICIO para testar a fila de instalacao. Nao instala nada.
# Registra nome e parametros recebidos e simula alguns segundos de trabalho.
# Compativel com o PowerShell 2.0 do Windows 7 (sem $PSScriptRoot, que so existe a partir do 3.0).
$pasta = Split-Path -Parent $MyInvocation.MyCommand.Path
$linha = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $MyInvocation.MyCommand.Name + ' ' + ($args -join ' ')
Add-Content -LiteralPath (Join-Path $pasta 'instalacoes-simuladas.log') -Value $linha
Start-Sleep -Seconds 2
exit 0
