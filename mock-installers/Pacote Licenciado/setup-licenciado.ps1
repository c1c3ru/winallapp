# Instalador licenciado FICTICIO numa pasta com espaco no nome (testa o escape de caminhos da rede).
# Um instalador real (ex.: pacote de implantacao da Autodesk) so instala; a ativacao da licenca e manual.
# Compativel com o PowerShell 2.0 do Windows 7. O log fica na pasta de cima (mock-installers).
$pasta = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$linha = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $MyInvocation.MyCommand.Name + ' ' + ($args -join ' ')
Add-Content -LiteralPath (Join-Path $pasta 'instalacoes-simuladas.log') -Value $linha
exit 0
