# Substituto FICTICIO do choco.exe usado no modo simulacao. Nao baixa nem instala nada.
# Registra o comando recebido para conferir o roteamento (winget no Win 10/11, Chocolatey no Win 7).
# Compativel com o PowerShell 2.0 do Windows 7.
$pasta = Split-Path -Parent $MyInvocation.MyCommand.Path
$linha = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' choco ' + ($args -join ' ')
Add-Content -LiteralPath (Join-Path $pasta 'instalacoes-simuladas.log') -Value $linha
exit 0
