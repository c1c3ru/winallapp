# STATUS_POWERSHELL

> Troca dos arquivos em lotes (`.bat`) por scripts do Windows PowerShell (`.ps1`), compatíveis com
> Windows 7, 10 e 11. Branch: `claude/powershell-scripts`. Atualizado a cada ciclo.

## Onde havia .bat

| Lugar | Antes | Agora |
|---|---|---|
| Instaladores fictícios da simulação (`mock-installers`) | 7 `*-setup.bat`, `winget.bat`, `choco.bat`, `Pacote Licenciado\setup-licenciado.bat` | Os mesmos em `.ps1` |
| `config.json` real | SimulIDE com `SimulIDE\copiar.bat` (arquivo ainda não existe na rede) | `SimulIDE\copiar.ps1`, com modelo pronto em `exemplos/copiar-portatil.ps1` |
| Tipos aceitos no `config.json` | `exe`, `msi`, `bat`, `cmd` | + `ps1` (ou `powershell`); `bat`/`cmd` continuam aceitos |
| Checklist "Padronizar Windows" | Já era PowerShell (5.1, só Windows 10/11) | Sem mudança |

Os instaladores reais (`.exe`/`.msi`), a cópia de pastas (`copia_pasta`, feita pelo próprio app) e o winget/Chocolatey
nunca usaram `.bat`.

## Como o .ps1 é executado

```
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<script.ps1>" <argumentos>
```

- `-File` devolve o `exit N` do script como código de saída (0, 1641 e 3010 = sucesso), igual ao 2.0 do Windows 7.
- `-ExecutionPolicy Bypass` vale só para esse processo: não altera a política da máquina nem exige assinar os scripts da rede.
- O caminho é `%WINDIR%\System32\WindowsPowerShell\v1.0\powershell.exe` (ou `Sysnative` num processo 32 bits em Windows 64 bits).

## Compatibilidade Windows 7 / 10 / 11

- Windows 7 SP1 vem com o **PowerShell 2.0**; Windows 10 e 11 vêm com o **5.1**, que roda tudo o que é do 2.0.
  Por isso os scripts usam só recursos do 2.0.
- Teste `CompatibilidadePowerShellTests` barra, em todo `.ps1` da simulação e da pasta `exemplos`, recursos que não
  existem no 2.0 (`$PSScriptRoot`, `$PSCommandPath`, `-in`, `[ordered]`, `[pscustomobject]`, `::new()`, `.Where()`,
  `Get-Content -Raw`, `Invoke-WebRequest`, `ConvertTo-Json`, `Get-CimInstance` etc.) e exige arquivo só ASCII
  (sem BOM, o PowerShell lê na página de código do sistema e acentos quebrariam).
- O CI (Windows Server com PowerShell 5.1, o mesmo do Windows 10/11) roda de verdade: a simulação inteira com os `.ps1`,
  o roteamento winget/Chocolatey, o modelo `copiar-portatil.ps1` (cópia + atalho) e um `.bat` antigo (compatibilidade).
- Não há runner com Windows 7 no GitHub Actions; o 2.0 é garantido pelo teste acima. Recomendado conferir uma vez
  `WinAllApp.exe --simulacao` numa máquina Windows 7 do laboratório.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 1 | Tipo `ps1` no `InstallCommandBuilder`, mocks convertidos, modelo `copiar-portatil.ps1`, teste de compatibilidade com o 2.0, testes reais no Windows. Mocks executados com PowerShell 7 no Linux para conferir argumentos e log. | 206 testes ok no Linux; CI Windows: aguardando |

## Pendências

- Criar `SimulIDE\copiar.ps1` na pasta de rede a partir do modelo (e conferir o nome do executável do SimulIDE).
