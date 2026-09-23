# STATUS_INSTALLER

> Fonte única de verdade do trabalho. Atualizado a cada ciclo. Branch: `claude/installer-por-laboratorio`.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 0 | Branch criada, repositório só tinha README. SDK .NET 8 instalado no container (Ubuntu). | — |
| 1 | Biblioteca core, UI WPF, configs e mocks. Build Release da solução. | **0 erros, 0 avisos** |
| 2 | Testes xUnit (lógica + fluxo do usuário + Process.Start real com mocks `.sh`). | 27 ok, 1 ignorado (só Windows), 1 bug de teste corrigido (código de saída POSIX tem 8 bits) |
| 3 | Teste da tela WPF real (net48) + CI `windows-latest` (build, testes, capturas de tela). | ver secção 4 |

Tentativas falhadas no mesmo bug de compilação: **0 de 5**.

## 1) Arquitetura base concluída

- **WPF sobre .NET Framework 4.8** (`net48`), roda do **Windows 7 SP1 ao Windows 11**. O .NET 4.8 precisa estar instalado no Win 7 (já vem no Win 10 1903+ e no Win 11).
- Solução `WinAllApp.sln`:
  - `src/WinAllApp.Core` (`net48;net8.0`): modelos, leitura/validação do `config.json`, filtro Bloco → Laboratório → Programas, fila de instalação assíncrona e ViewModels (MVVM). Sem dependência de WPF nem de pacotes externos (JSON via `DataContractJsonSerializer`, nativo do .NET).
  - `src/WinAllApp` (`net48`, WinExe): ponto de entrada, `app.manifest` (pede administrador via UAC; declara Win 7/8/8.1/10/11) e a tela `Views/MainWindow.xaml`.
  - `tests/WinAllApp.Core.Tests` (`net8.0`): roda em Linux e Windows.
  - `tests/WinAllApp.UI.Tests` (`net48`): abre a janela real; compila em qualquer SO, **executa só no Windows** (CI).
- **Decisão técnica:** a tela é XAML "solto" embutido no `.exe` e carregado com `XamlReader.Load`, com toda a interação por bindings/comandos. Motivo: o SDK .NET no Linux não traz o compilador de marcação do WPF (`PresentationBuildTasks`), e o pacote NuGet antigo (3.0.0) quebra com caminhos Linux. Com isso a solução inteira compila no Linux e no Windows com o mesmo `dotnet build`, sem Visual Studio.
- CI: `.github/workflows/build.yml` compila e testa em `windows-latest` e publica o executável e capturas da tela como artefatos.

## 2) UI/UX implementada

- **Menu lateral** com dois passos: `1. Bloco` (dropdown BL1/BL2) e `2. Laboratório` (lista com nome, id e nº de programas).
- Ao escolher o laboratório, a área principal mostra **só os programas daquela sala** (vindos do `config.json`), cada um com **checkbox individual**, versão e caminho do instalador.
- Botões **"Selecionar Todos do Laboratório"** e **"Limpar seleção"**, contador "x de y selecionado(s)".
- Rodapé com status, **barra de progresso**, botão **"Instalar selecionados (n)"** (desabilitado sem seleção) e **"Cancelar"** (visível durante a instalação).
- Estado por programa (Instalando…, Instalado, Instalado (reiniciar), Falhou, Cancelado) com cores e tooltip com a mensagem/código.
- Durante a instalação o menu e as checkboxes ficam travados, mas a janela continua respondendo.
- "Registro da instalação" (expansível) com o log da sessão e avisos do config.
- Compatibilidade Win 7: apenas controles nativos e cores sólidas (sem transparência, sombras ou animações), fonte Segoe UI.

## 3) Lógica de automação e processamento do JSON

- `config.json`: `pastaInstaladores` (absoluta, UNC ou relativa ao config), `blocos[] → laboratorios[] → programas[]` (ids) e um **catálogo único** `programas[]` (`id, nome, versao, instalador, tipo, argumentos, timeoutMinutos, codigosSucesso`). Programas usados em várias salas são cadastrados uma vez.
- Validação ao abrir: ids duplicados, laboratório apontando para programa inexistente, programa sem instalador (erros); `.exe` sem argumento silencioso (aviso no log).
- Comando silencioso por tipo:
  - `exe` → o próprio instalador + `argumentos`;
  - `msi` → `msiexec.exe /i "<arquivo>" /qn /norestart` (ou os argumentos do config);
  - `bat`/`cmd` → `cmd.exe /c ""<arquivo>" <argumentos>"`.
- Fila **sequencial e assíncrona** (`InstallQueue` + `ProcessRunner`): `Process.Start` com `UseShellExecute=false`, `CreateNoWindow=true`, espera pelo evento `Exited` via `TaskCompletionSource` (não bloqueia a UI), **timeout** por programa (padrão 60 min), **cancelamento** (encerra o instalador atual e marca o resto como cancelado).
- Códigos de saída: 0 = sucesso; 1641/3010 = sucesso com reinício; demais = falha (personalizável por programa). Instalador ausente no disco = falha sem executar.
- Itera **apenas os itens marcados** do laboratório atual.
- Arquivos de config:
  - `src/WinAllApp/config.json` — exemplo com BL1 (LAMEP, LCC, Geotecnologia) e BL2 (Matemática);
  - `src/WinAllApp/config.simulacao.json` + `mock-installers/*.bat` — simulação sem instalar nada (LCC com 3, Matemática com 2 e um laboratório "Teste de falhas");
  - `tests/WinAllApp.Core.Tests/TestData/config.teste.json` — LCC (3 programas) e Matemática (2 programas).

### QA executado (ciclo 2, Linux)

- Filtro: LCC → 3 programas; Matemática → 2; laboratório de outro bloco não aparece.
- Fluxo do usuário no ViewModel ligado à tela: bloco → laboratório → "Selecionar Todos" → desmarcar 1 → Instalar ⇒ o runner recebeu **só os 2 marcados**, com `/VERYSILENT /NORESTART` e `/S`; Matemática usa `msiexec /i ... /qn /norestart`.
- Assincronia: `InstalarAsync` devolve o controle na hora (tarefa não concluída, `Ocupado=true`, navegação travada) e conclui depois.
- `Process.Start` real com instaladores fictícios `.sh`: só os selecionados gravaram no log, com os parâmetros silenciosos; código de erro vira falha; arquivo ausente vira falha; timeout encerra o processo.

## 4) Falhas ou dependências pendentes

- **PDFs BL1 e BL2 não foram recebidos** (não estão no repositório nem anexados). Os laboratórios e programas do `config.json` são **exemplos** (sala marcada como `EXEMPLO`). Com os PDFs, basta preencher o `config.json` com os laboratórios e softwares reais.
- **Parâmetros silenciosos reais** de AutoCAD, Proteus etc. variam por versão/licença e precisam ser confirmados com os instaladores reais (AutoCAD normalmente exige um pacote de *deployment* criado no Autodesk Account).
- **A janela WPF não pode ser aberta no container Linux.** A lógica e o fluxo foram testados no Linux; a tela real é testada no CI Windows (`tests/WinAllApp.UI.Tests`). Resultado do CI: _pendente (atualizado após o push)_.
- Win 7: nenhum problema de renderização encontrado até aqui, mas a validação visual em uma máquina Win 7 real ainda não foi feita.
