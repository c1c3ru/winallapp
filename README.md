# WinAllApp

Aplicação Windows (WPF, .NET Framework 4.8) para **instalar softwares em lote por laboratório**:
o técnico escolhe o **Bloco** (BL1, BL2), depois o **Laboratório**, marca os programas daquela sala
e instala tudo em fila, de forma silenciosa, sem travar a janela.

Compatível do **Windows 7 SP1 ao Windows 11** (requer .NET Framework 4.8 instalado no Windows 7/8.1).

## Como usar

O aplicativo é **um único arquivo: `WinAllApp.exe`** (cerca de 90 KB, sem DLLs nem pastas extras).
A lista de blocos, laboratórios e programas dos PDFs BL1/BL2 já vem embutida nele.

**Baixar:** na página de Releases do GitHub, pré-versão **`ultima-build`**, arquivo `WinAllApp.exe`
(atualizada automaticamente a cada push que passa nos testes).

1. Copie o `WinAllApp.exe` para o pendrive ou para a máquina do laboratório.
2. Dê dois cliques (o Windows pede permissão de administrador).
3. Escolha o bloco, o laboratório, marque os programas e clique em **Instalar selecionados**.

Os instaladores são procurados na pasta definida em `pastaInstaladores` (padrão: `\\servidor\instaladores`).

| Comando | O que faz |
|---|---|
| `WinAllApp.exe` | Usa o `config.json` ao lado do .exe; se não houver, usa o embutido |
| `WinAllApp.exe --extrair-config` | Grava o `config.json` embutido ao lado do .exe para você editar (caminhos, parâmetros) |
| `WinAllApp.exe --config outro.json` | Usa outro arquivo de configuração |
| `WinAllApp.exe --simulacao` | Modo de teste: usa instaladores fictícios embutidos e não instala nada |

### Editar a lista de programas

No `config.json`: `pastaInstaladores`; `programas` (catálogo com `id`, `nome`, `instalador`, `tipo` `exe`/`msi`/`bat` e `argumentos` silenciosos);
`blocos[].laboratorios[].programas` (ids de cada sala). Para mudar a lista embutida no próprio .exe, edite `src/WinAllApp/config.json` e recompile.

## Compilar

```
dotnet build WinAllApp.sln -c Release
dotnet test tests/WinAllApp.Core.Tests          # qualquer SO
dotnet test tests/WinAllApp.UI.Tests            # só Windows (abre a janela)
```

Funciona com o SDK .NET 8 em Windows ou Linux (não precisa do Visual Studio). Saída: `src/WinAllApp/bin/Release/net48/WinAllApp.exe` (arquivo único).

O andamento do projeto e as pendências estão em [`STATUS_INSTALLER.md`](STATUS_INSTALLER.md).
