# STATUS_INSTALLER

> Fonte única de verdade do trabalho. Atualizado a cada ciclo. Branch: `claude/installer-por-laboratorio`.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 0 | Branch criada, repositório só tinha README. SDK .NET 8 instalado no container (Ubuntu). | — |
| 1 | Biblioteca core, UI WPF, configs e mocks. Build Release da solução. | **0 erros, 0 avisos** |
| 2 | Testes xUnit (lógica + fluxo do usuário + Process.Start real com mocks `.sh`). | 27 ok, 1 ignorado (só Windows), 1 bug de teste corrigido (código de saída POSIX tem 8 bits) |
| 3 | Teste da tela WPF real (net48) + CI `windows-latest` (build, testes, capturas de tela). | Build Windows ok; 1 teste falhou no Windows |
| 4 | Causa: nos `.bat` fictícios, `echo ... InstallAllUsers=1>> log` era lido pelo cmd como redirecionamento do handle `1` e cortava o argumento. Corrigido pondo o `>>` antes do `echo`. | **CI Windows 100% verde**: build, 28 testes da lógica (inclui simulação com `.bat` reais) e teste da tela WPF |
| 5 | PDFs BL1/BL2 recebidos: `config.json` refeito com os 13 laboratórios reais e 69 programas; aviso na tela para laboratórios só com programas padrões; simulação reorganizada (LCC no BL2, Matemática no BL1). | Build 0 erros/0 avisos; 43 testes ok no Linux (+1 só Windows) |
| 6 | Empacotamento em **executável único**: o código do Core é compilado dentro do `WinAllApp.exe`; tela, `config.json`, config de simulação e `.bat` fictícios vão embutidos; opções `--simulacao` e `--extrair-config`; CI publica a pré-versão `ultima-build` com o .exe. | Build 0 erros/0 avisos; saída = só `WinAllApp.exe` (~87 KB) |

Tentativas falhadas no mesmo bug de compilação: **0 de 5** (nenhum erro de compilação em nenhum ciclo).

**Condição de parada atingida (ciclo 4):** compila para .NET Framework 4.8 sem erros, a UI filtra por Bloco → Laboratório com checkboxes e "Selecionar Todos do Laboratório", e a instalação assíncrona itera só os itens marcados (verificado no Linux e no Windows).

## 1) Arquitetura base concluída

- **WPF sobre .NET Framework 4.8** (`net48`), roda do **Windows 7 SP1 ao Windows 11**. O .NET 4.8 precisa estar instalado no Win 7 (já vem no Win 10 1903+ e no Win 11).
- Solução `WinAllApp.sln`:
  - `src/WinAllApp.Core` (`net48;net8.0`): modelos, leitura/validação do `config.json`, filtro Bloco → Laboratório → Programas, fila de instalação assíncrona e ViewModels (MVVM). Sem dependência de WPF nem de pacotes externos (JSON via `DataContractJsonSerializer`, nativo do .NET).
  - `src/WinAllApp` (`net48`, WinExe): ponto de entrada, `app.manifest` (pede administrador via UAC; declara Win 7/8/8.1/10/11) e a tela `Views/MainWindow.xaml`. **Gera um único `WinAllApp.exe`**: compila junto o código do Core (sem DLL), embute a tela, o `config.json`, o `config.simulacao.json` e os `.bat` fictícios, e não gera `.exe.config` nem `.pdb` separado.
  - Distribuição: pré-versão **`ultima-build`** nos Releases do GitHub, recriada pelo CI a cada push que passa nos testes.
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
  - `src/WinAllApp/config.json` — **mapa real dos PDFs** (ver abaixo);
  - `src/WinAllApp/config.simulacao.json` + `mock-installers/*.bat` — simulação sem instalar nada (BL1: Matemática com 2 e "Teste de falhas"; BL2: LCC com 3 e LAMEP sem programas);
  - `tests/WinAllApp.Core.Tests/TestData/config.teste.json` — LCC (3 programas) e Matemática (2 programas).

### Mapa real (PDFs "BL1/BL2 - Programas Específicos")

| Bloco | Laboratório (id) | Programas |
|---|---|---|
| BL1 | Bioquímica e Fisiologia Vegetal (BIOQ) | 0 — apenas os programas padrões |
| BL1 | Geotecnologia (GEOTEC) | 12 |
| BL1 | LQOI | 1 |
| BL1 | Modelagem Molecular – Curso SIC (MODMOL) | 3 |
| BL1 | Matemática (MAT) | 6 |
| BL2 | LAMEP | 0 — apenas os programas padrões |
| BL2 | LCC | 17 |
| BL2 | LED | 4 |
| BL2 | LEE | 12 |
| BL2 | LIA | 27 |
| BL2 | LINC | 13 |
| BL2 | LSHIP | 6 |
| BL2 | LEA | 8 |

- Catálogo com **69 programas**. Versões diferentes viraram itens separados (AutoCAD / 2018 / 2022, Octave / 4.0 / 7.3.0, Proteus 7.6 / 8, Visual Studio 2012 / 2019 / 2022, QGIS / 3.0.2, PSIM / PSIM 911, MPLAB 8.53 / 8.53 + C18). Grafias diferentes do mesmo programa foram unificadas (Arduino/Arduíno, SketchUP/Google Sketchup, LTspice/LTSpice).
- Laboratórios só com programas padrões mostram o aviso na tela e não têm itens para instalar.
- O teste `ConfigRealTests` confere a quantidade de programas de cada laboratório contra os PDFs.

### QA executado (ciclo 2 no Linux; ciclo 4 também no Windows via CI)

- Filtro: LCC → 3 programas; Matemática → 2; laboratório de outro bloco não aparece.
- Fluxo do usuário no ViewModel ligado à tela: bloco → laboratório → "Selecionar Todos" → desmarcar 1 → Instalar ⇒ o runner recebeu **só os 2 marcados**, com `/VERYSILENT /NORESTART` e `/S`; Matemática usa `msiexec /i ... /qn /norestart`.
- Assincronia: `InstalarAsync` devolve o controle na hora (tarefa não concluída, `Ocupado=true`, navegação travada) e conclui depois.
- `Process.Start` real com instaladores fictícios `.sh`: só os selecionados gravaram no log, com os parâmetros silenciosos; código de erro vira falha; arquivo ausente vira falha; timeout encerra o processo.

## 4) Falhas ou dependências pendentes

- ~~PDFs BL1 e BL2 não recebidos~~ → recebidos e aplicados no ciclo 5.
- **Programas padrões:** os PDFs dizem que BIOQ e LAMEP usam "apenas os programas padrões", mas a lista desses programas não está em nenhum PDF. Se ela for enviada, dá para criar um grupo "Padrão" instalável em qualquer laboratório.
- **Pontos interpretados (confirmar):** "CLP CLIP" (LSHIP) foi tratado como "CLP CLIC"; "Látex" (Geotecnologia) foi cadastrado como LaTeX genérico; "Proteus 7.6" (LIA) = "Proteus 7.6 SP0" (LCC); "PSIM" (LEA), "Octave" e "QGIS" (LIA) e "AutoCAD" (Geotecnologia) estão sem versão, como no PDF.
- **Instaladores:** os caminhos em `config.json` seguem o padrão `\\servidor\instaladores\<Programa>\<arquivo>` e precisam ser ajustados aos arquivos reais. **40 dos 69 programas** estão sem parâmetro silencioso confirmado (marcados com "a confirmar"; o app avisa no registro ao abrir). Winplot, Logisim, SimulIDE e os "Materiais de aula" não têm instalador: estão como `.bat` de cópia a ser criado.
- **Compatibilidade:** Visual Studio 2022 e versões recentes de Python/QGIS/Octave não rodam no Windows 7; os laboratórios com Win 7 precisam das versões antigas.
- **Parâmetros silenciosos reais** de AutoCAD, Proteus etc. variam por versão/licença e precisam ser confirmados com os instaladores reais (AutoCAD normalmente exige um pacote de *deployment* criado no Autodesk Account).
- **A janela WPF não pode ser aberta no container Linux.** A lógica e o fluxo foram testados no Linux; a tela real é testada no CI Windows (`tests/WinAllApp.UI.Tests`): o teste abre a janela, escolhe BL2 → Matemática (2 checkboxes), BL1 → LCC (3 checkboxes), clica em "Selecionar Todos do Laboratório", desmarca o Python, clica em Instalar e confere que a janela seguiu responsiva e que só VS Code e Code::Blocks foram disparados com `/VERYSILENT /NORESTART` e `/S`. **Passou no CI.** As capturas da tela ficam no artefato `capturas-da-tela` de cada execução do Actions (o container não consegue baixá-las, então ainda não foram revisadas visualmente).
- Win 7: nenhum problema de renderização encontrado até aqui, mas a validação visual em uma máquina Win 7 real ainda não foi feita.
