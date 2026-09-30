# Modelo de script de copia para programas portateis (ex.: SimulIDE), para a pasta de rede.
# Compativel com o PowerShell 2.0 que vem no Windows 7 (e com o 5.1 do Windows 10/11).
#
# Como usar: copie este arquivo como "copiar.ps1" para dentro da pasta do programa na rede
# (ex.: ...\Laboratorios - Programas\SimulIDE\copiar.ps1) e informe no config.json:
#   "instalador": "SimulIDE\\copiar.ps1", "tipo": "ps1",
#   "argumentos": "\"C:\\Programas\\SimulIDE\" simulide.exe SimulIDE"
# Argumentos: 1) pasta de destino  2) executavel (relativo ao destino)  3) nome do atalho (opcional)
#
# O script copia tudo o que esta na pasta dele (menos ele mesmo) para o destino e cria o atalho na
# Area de Trabalho publica. Codigo de saida 0 = sucesso; o WinAllApp mostra falha para qualquer outro.

$eu = $MyInvocation.MyCommand.Name
$origem = Split-Path -Parent $MyInvocation.MyCommand.Path

if ($args.Count -lt 2) {
    Write-Output 'ERRO: uso: copiar.ps1 <pasta de destino> <executavel> [nome do atalho]'
    exit 2
}

$destino = [Environment]::ExpandEnvironmentVariables([string]$args[0])
$executavel = [string]$args[1]
$nome = [IO.Path]::GetFileNameWithoutExtension($executavel)
if ($args.Count -ge 3) { $nome = [string]$args[2] }

# robocopy vem no Windows 7 e copia subpastas mantendo o que ja existe; codigos 0 a 7 sao sucesso.
& robocopy.exe $origem $destino /E /XF $eu /R:1 /W:1 /NP /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Output ('ERRO: robocopy terminou com o codigo ' + $LASTEXITCODE + '.')
    exit 1
}

$alvo = Join-Path $destino $executavel
if (-not (Test-Path -LiteralPath $alvo)) {
    Write-Output ('ERRO: executavel nao encontrado depois da copia: ' + $alvo)
    exit 1
}

try {
    $areaDeTrabalho = Join-Path $env:PUBLIC 'Desktop'
    $shell = New-Object -ComObject WScript.Shell
    $atalho = $shell.CreateShortcut((Join-Path $areaDeTrabalho ($nome + '.lnk')))
    $atalho.TargetPath = $alvo
    $atalho.WorkingDirectory = Split-Path -Parent $alvo
    $atalho.Save()
}
catch {
    Write-Output ('ERRO: nao foi possivel criar o atalho: ' + $_.Exception.Message)
    exit 1
}

Write-Output ('RESULTADO: ' + $nome + ' copiado para ' + $destino)
exit 0
