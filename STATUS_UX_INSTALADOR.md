# STATUS_UX_INSTALADOR

> Fonte de verdade da refatoração de UX do instalador: contador, campo da pasta de rede, pesquisa,
> spinner e alertas de erro. Branch: `claude/ux-instalador`. Atualizado a cada ciclo.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 0 | Leitura de `MainViewModel`, `InstallQueue`, `ProcessRunner`, `MainWindow.xaml` e dos testes de tela. Diagnóstico abaixo. | Causa do contador encontrada |
| 1 | Implementação das 5 partes + 22 testes novos no Linux e 2 testes de tela novos para o Windows. Versão 0.2.0. | Build Release ok; **101 testes ok no Linux** (1 pulado, só Windows). _CI Windows em andamento_ |

Tentativas de corrigir o mesmo bug no WPF/XAML: **0 de 5**.

## 1) Correção do Contador (Ajuste no ViewModel)

- **Diagnóstico (ciclo 0):** o `MainViewModel` guardava `SynchronizationContext.Current` no construtor. No `.exe` real o
  ViewModel é criado em `Program.Main`, **antes** de `app.Run`, quando o WPF ainda não instalou o contexto da UI (fica `null`).
  A fila usa `ConfigureAwait(false)`, então a partir do 1º instalador o progresso chega numa thread do pool; com o contexto
  `null` o ViewModel alterava `Log` (ObservableCollection ligada a um `ListBox`) fora da thread da UI. O WPF lança
  `NotSupportedException`, a fila aborta e o resumo fica "0 instalado(s)". Os testes de tela não pegavam porque definem o
  contexto antes de criar o ViewModel.
- **Feito (ciclo 1):** `InstalarAsync` captura `SynchronizationContext.Current` no início (roda na thread da tela) e só usa o
  do construtor como reserva. Contador novo, derivado dos próprios itens da fila: `TotalNaFila`, `Concluidos`, `Instalados`,
  `Falhas`, `ContadorTexto` ("2 de 5 concluído(s) · 2 instalado(s) · 0 falha(s)"), vermelho quando há falha; a barra de
  progresso usa a mesma conta. O resumo final também sai dos itens, então bate com o contador mesmo se a fila abortar.
- **Reproduzido e coberto:** o teste `ViewModelCriadoAntesDoLoopDaTela_AtualizaTudoNaThreadDaTela` cria o ViewModel sem
  contexto (como o `Program.Main`) e roda a instalação num loop de tela próprio. Com o código antigo ele falha
  (itens alterados fora da thread da tela); com a correção passa. No Windows, `Usuario_PesquisaInstalaComFalhaEVeOContadorEOAlerta`
  faz o mesmo com a janela WPF real.

## 2) Input de Caminho de Rede e Escape de Strings

- **Feito:** campo `CampoPastaRede` (TextBox two-way em `PastaRede`) no topo da área principal, com o padrão
  `\\10.50.11.2\informatica\NAC - Núcleo de Atendimento ao Cliente\ no windows\Programas\Laboratórios - Programas`
  vindo do `config.json` (constante `ContextoInstalacao.PastaRedePadrao`). O texto vai direto para o roteador
  (espaços/aspas das pontas são removidos). Nada de validar credenciais: um erro de acesso vira falha no item e alerta.
- O caminho foi copiado exatamente como pedido, **inclusive o espaço antes de "no windows"**.
- `ProcessRunner.CriarInfo` põe o executável entre aspas duplas (`ComAspas`, sem duplicar) antes do `Process.Start`.
  MSI (`/i "caminho"`) e .bat (`cmd /c ""caminho" args"`) já iam entre aspas.

## 3) Barra de Pesquisa (Filtro dinâmico da lista via ViewModel)

- **Feito:** `SearchText` no `MainViewModel`; `ProgramasVisiveis` é refeita por LINQ a cada tecla com os **mesmos objetos**
  de `Programas`, então `Selecionado` sobrevive ao item sair e voltar do filtro. Sem diferenciar maiúsculas nem acentos
  (nome, id, versão, categoria). Sem code-behind: `TextBox` com `UpdateSourceTrigger=PropertyChanged`.
- "Selecionar" passa a marcar os exibidos (rótulo muda para "Selecionar os exibidos"); "Instalar" usa todos os marcados,
  inclusive os escondidos pela pesquisa. Botão ✕ limpa a pesquisa; aviso quando nada corresponde.

## 4) Responsividade e Spinner (Implementação de `async/await`)

- Diagnóstico: o primeiro item da fila (checagem de arquivo na rede e `Process.Start`) rodava na thread da UI até o 1º `await`;
  um caminho UNC lento deixava a janela "Não respondendo".
- **Feito:** `InstalarAsync` faz `await Task.Run(() => _fila.ExecutarAsync(...))`; cada aviso volta para a tela por `NaUi`.
  A verificação da pasta de rede também roda em `Task.Run`. Spinners (arco girando, sem bibliotecas): no rodapé durante a
  instalação, na linha em andamento e ao lado do campo de rede durante a verificação. A animação para quando o spinner some.

## 5) Feedback Visual de Erros na UI

- **Feito:** faixa vermelha `FaixaErro` (ícone "!", texto e botão Fechar) para pasta de rede inacessível/vazia, programas que
  falharam (lista os nomes) e erro inesperado. A linha que falhou ganha fundo vermelho claro, ícone vermelho e "Motivo: …".
  Um instalador que não abre (ex.: acesso negado na rede) vira falha com o motivo.

## 6) Falhas de compilação ou testes quebrados

- Ciclo 1: nenhuma falha de compilação. Um teste antigo (`Cancelar_InterrompeOAtualEMarcaORestanteComoCancelado`) assumia
  que o 1º instalador começava antes do `await`; com a fila em `Task.Run` o teste agora espera o 1º começar antes de cancelar.
- Aguardando o CI Windows (telas reais e `.exe` real).
