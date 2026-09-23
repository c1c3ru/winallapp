# STATUS_RELEASE

> Fonte de verdade para o encerramento do projeto (release portátil, onboarding e documentação).
> Branch: `claude/installer-por-laboratorio`. Atualizado a cada ciclo.

**Situação: concluído.** Build de Release gerada, README detalhado e onboarding exibido corretamente.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 1 | Onboarding (3 passos), logo do IFCE na tela e no tutorial, ícone do .exe, README com tutorial, build Release no Linux. | Build Release 0 erros; 79 testes da lógica ok no Linux |
| 2 | CI Windows: build Release, testes da lógica, telas reais (onboarding e principal) e execução do `.exe` real. | Build ok; 1 teste falhou: na 2ª execução do `.exe` real o tutorial voltou (ver seção 4) |
| 3 | Correção: as preferências passam a respeitar a variável `%APPDATA%`. | **CI Windows verde** (commit `8299418`): build Release, todos os testes da lógica, telas reais e `.exe` real; `ultima-build` publicada |

Tentativas de correção de compilação da interface: **1 de 5**.

## 1) Compilação da Release

- `dotnet build WinAllApp.sln -c Release` gera **um único arquivo**: `src/WinAllApp/bin/Release/net48/WinAllApp.exe`
  (~180 KB, sem DLLs, sem `.exe.config`). É portátil: roda do pendrive ou da pasta de rede.
- Dentro do .exe: código, telas XAML, `config.json` real, kit de simulação, logo do IFCE; ícone do IFCE como ícone do arquivo.
- O CI publica o .exe na pré-versão **`ultima-build`** a cada push verde. Sem instalador (MSI/Inno) de propósito.

## 2) Implementação do Onboarding

- Janela modal "Bem-vindo ao WinAllApp" sobre a principal, **só na primeira execução** (ou com `--tutorial` / botão **Tutorial**).
- 3 passos estáticos com ícone e dica: 1) escolher bloco/laboratório; 2) conferir a pasta de rede (mostra o caminho configurado);
  3) instalar em lote (e lembrete da ativação manual dos licenciados). Botões Pular / Voltar / Avançar-Concluir.
- "Não mostrar novamente" grava `onboardingConcluido=1` em `%APPDATA%\WinAllApp\preferencias.ini` (nada ao lado do .exe).
  Falha ao gravar não trava o app; o tutorial só volta a aparecer.
- Tela principal: logo do IFCE no topo do menu, cor verde do IFCE, bloco "Pasta de rede" (acessível/INACESSÍVEL),
  bloco "Sistema" (Windows, .NET 4.8, TLS 1.2, winget/Chocolatey) e botões "Verificar rede" e "Tutorial".
- Verificação: testes da tela real no Windows (CI) abrem o onboarding, navegam os 3 passos, conferem a logo e o fechamento;
  outro teste executa o `WinAllApp.exe` real com um `%APPDATA%` vazio e confere que a janela "Bem-vindo ao WinAllApp" aparece
  na 1ª execução e não aparece na 2ª. Capturas vão para o branch `capturas-ci`.

## 3) Documentação

- `README.md` reescrito: pré-requisitos (.NET 4.8, administrador, pasta de rede, TLS 1.2 no Win 7), download, execução passo a passo
  como Administrador, opções de linha de comando, as 4 categorias, como editar o `config.json` (com exemplos de cada categoria
  e tabela de campos), preparação da pasta de rede e compilação.

## 4) Falhas na build ou ajustes visuais pendentes

- **Ciclo 2 (corrigido no ciclo 3):** o `.exe` real mostrou o tutorial também na 2ª execução. Causa: o caminho das preferências
  vinha de `Environment.GetFolderPath(ApplicationData)`, que ignora a variável `%APPDATA%` e lê sempre o perfil do Windows;
  o teste (e qualquer perfil redirecionado por script) grava em outro lugar. Agora `PreferenciasUsuario.PastaAppData()` usa
  `%APPDATA%` quando definida e só cai no perfil quando ela não existe.
- **Nenhuma falha aberta.** O CI Windows do ciclo 3 passou: o `.exe` real mostra o tutorial na 1ª execução e não na 2ª.
- Capturas conferidas (branch `capturas-ci`): tutorial com logo e 3 passos; tela principal com logo, pasta de rede
  (verde), bloco "Sistema" e avisos de Windows 7 em vermelho.
- Ajuste visual opcional, sem impacto: quando o nome do programa já contém a versão (ex.: "Visual Studio 2022"),
  o aviso de incompatibilidade repete a versão ("… 2022 2022 exige Windows 10").
- Avisos de compilação restantes (não bloqueiam): CA1416 no alvo net8.0 do Core (registro do Windows), que só roda no Windows.
