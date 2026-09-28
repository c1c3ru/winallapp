# STATUS_AUTODESCOBERTA

> Fonte de verdade da autodescoberta dos instaladores: o app confere em segundo plano se cada arquivo/pasta do
> `config.json` existe na pasta de rede e mostra ✔ (encontrado, pode marcar) ou ❌ (não encontrado, bloqueado).
> Branch: `claude/autodescoberta`. Atualizado a cada ciclo.

**Situação: em andamento (ciclo 1: código pronto, aguardando o CI Windows com a janela real).** Versão 0.3.0.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 0 | Leitura de `Roteamento`, `InstallQueue`, `ConfigLoader`, `MainViewModel`, `ProgramaItemViewModel`, `MainWindow.xaml` e dos testes. Plano abaixo. | Plano definido |
| 1 | Scanner no `ConfigLoader`, `IsAvailable` no item, verificação no `MainViewModel`, ícones e bloqueio no XAML, 14 testes novos no Linux e 1 teste de tela novo no Windows. Versão 0.3.0. | Build Release ok; 116 testes ok no Linux (3 rodadas). CI Windows: aguardando |

Tentativas de corrigir binding/assincronicidade: **0 de 5**.

## 1) Lógica de Autodescoberta (File.Exists e Directory.Exists assíncronos no ViewModel)

- **Feito (ciclo 1):** `ConfigLoader.CaminhoNaRede` monta Caminho Base + caminho do JSON (mesma regra do roteador,
  `InstallCommandBuilder.ResolverCaminho`); em `copia_pasta` confere a pasta (`Directory.Exists`), nos demais o arquivo
  (`File.Exists`), usando as mesmas funções do roteador (`ContextoInstalacao.ArquivoExiste/PastaExiste`).
- `ConfigLoader.VerificarCaminhoAsync` (um caminho, com timeout) e `VerificarCaminhosAsync` (todos em paralelo com
  `Task.WhenAll`, até 8 consultas ao mesmo tempo, avisando cada resultado por `IProgress`).
- `ProgramaItemViewModel.IsAvailable` (`bool?`): nulo = buscando, true = encontrado, false = não encontrado; mais
  `StatusBusca` (texto da linha), `CaminhoNaRede`, `PodeSelecionar`.
- `MainViewModel` dispara a verificação ao abrir um laboratório, ao editar o campo "Pasta de rede" (espera 0,6 s sem
  digitar, só o caminho final é consultado) e no botão "Verificar". Uma verificação nova cancela a anterior (o resultado
  antigo é descartado). Resumo "2 de 3 encontrado(s) na rede · 1 não encontrado(s)" e linha no Registro com os que faltam.
- `gerenciador` com `wingetId`/`chocoId` fica ✔ mesmo sem o arquivo na rede ("instala pelo winget/Chocolatey"), para não
  quebrar o motor adaptativo; `gerenciador` só com pacote não consulta a rede.

## 2) Feedback Visual na UI (ícones ✔ e ❌)

- **Feito (ciclo 1):** ao lado do nome, três ícones com `DataTrigger` em `IsAvailable` (estilos `SpinnerBusca`,
  `IconeEncontrado` ✔ verde e `IconeNaoEncontrado` ❌ vermelho, desenhados com `Path`, sem fontes de emoji, para o
  Windows 7). Embaixo, `StatusBusca` em verde/vermelho com o caminho procurado. Acima da lista, spinner
  `SpinnerDescoberta` e o resumo `TextoDisponibilidade` (vermelho quando falta algum).

## 3) Bloqueio de Seleção

- **Feito (ciclo 1):** estilo `CaixaPrograma`: `IsEnabled` falso por padrão e verdadeiro só com `IsAvailable = True`
  (`DataTrigger`); a lista inteira continua travada durante a instalação. No ViewModel, `Selecionado` recusa marcar
  quem não foi encontrado (vale para o clique e para "Selecionar Todos", que também só fica ativo com algum ✔), e um item
  marcado que some da rede é desmarcado.

## 4) Performance de Rede

- **Feito (ciclo 1):** cada `File.Exists`/`Directory.Exists` roda em `Task.Run`; `Task.WhenAny` com `Task.Delay(5 s)`
  ligado a um `CancellationToken` desiste do arquivo que não responde ("A rede não respondeu em 5 s"). A pasta base é
  testada antes: servidor fora do ar vira ❌ em todos de uma vez, sem uma consulta presa por arquivo. Os vereditos
  voltam para a tela por `Progress<T>` (thread da UI), um a um.
- Coberto por `Verificacao_NaoRodaNaThreadDaTelaEAvisaNelaCadaVeredito` (consulta fora da thread da tela, ✔ aplicado
  nela) e, no Windows, por `Autodescoberta_MostraBuscandoDepoisCheckOuXEBloqueiaOQueFalta` (janela real processando
  mensagens com a rede "lenta").

## 5) Status dos Testes

- **Testes antigos:** quem marcava programas logo depois de abrir o laboratório agora espera a autodescoberta
  (`AguardarAutodescoberta`), como o técnico que vê a lista pronta. Nos testes o contexto de sincronização fica nulo
  para a espera não prender a thread que aplicaria o veredito.
- **Testes novos (Linux, `AutodescobertaTests`, 14):** caminho combinado; encontrado/não encontrado/erro; timeout de
  servidor que não responde; paralelismo e aviso por item; pasta base fora do ar sem consultar arquivos; ✔/❌ e bloqueio
  (clique e "Selecionar Todos"); estado "buscando"; troca da pasta desmarcando o que sumiu; digitação consultando só o
  caminho final; rede fora do ar em menos de 5 s; troca de laboratório descartando a busca antiga; gerenciador com
  winget; o `config.json` não muda; thread da tela livre.
- **Testes de tela (Windows):** `Autodescoberta_MostraBuscandoDepoisCheckOuXEBloqueiaOQueFalta` (capturas 13 e 14) e
  ajustes nos testes existentes.
- Resultado local: 116 aprovados, 1 ignorado (só Windows), 3 rodadas seguidas. CI Windows: aguardando.
