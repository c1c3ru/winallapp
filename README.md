# WinAllApp

Aplicação Windows (WPF, .NET Framework 4.8) para **instalar softwares em lote por laboratório**:
o técnico escolhe o **Bloco** (BL1, BL2), depois o **Laboratório**, marca os programas daquela sala
e instala tudo em fila, de forma silenciosa, sem travar a janela.

Compatível do **Windows 7 SP1 ao Windows 11** (requer .NET Framework 4.8 instalado no Windows 7/8.1).

## Como usar

1. Coloque os instaladores numa pasta (local ou rede, ex.: `\\servidor\instaladores`).
2. Edite `config.json` (ao lado do `WinAllApp.exe`):
   - `pastaInstaladores`: pasta dos instaladores;
   - `programas`: catálogo com `id`, `nome`, `instalador`, `tipo` (`exe`, `msi`, `bat`) e `argumentos` silenciosos;
   - `blocos[].laboratorios[].programas`: ids dos programas de cada sala.
3. Execute `WinAllApp.exe` (pede permissão de administrador). Para usar outro arquivo: `WinAllApp.exe --config caminho\outro.json`.

### Testar sem instalar nada

```
WinAllApp.exe --config config.simulacao.json
```

Usa os instaladores fictícios de `mock-installers\` (só registram nome e parâmetros em `instalacoes-simuladas.log`).

## Compilar

```
dotnet build WinAllApp.sln -c Release
dotnet test tests/WinAllApp.Core.Tests          # qualquer SO
dotnet test tests/WinAllApp.UI.Tests            # só Windows (abre a janela)
```

Funciona com o SDK .NET 8 em Windows ou Linux (não precisa do Visual Studio). Saída: `src/WinAllApp/bin/Release/net48/`.

O andamento do projeto e as pendências estão em [`STATUS_INSTALLER.md`](STATUS_INSTALLER.md).
