# STATUS_RELEASE

> Fonte de verdade para o encerramento do projeto (release portátil, onboarding e documentação).
> Branch: `claude/installer-por-laboratorio`. Atualizado a cada ciclo.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 1 | Onboarding (3 passos), logo do IFCE na tela e no tutorial, ícone do .exe, README com tutorial, build Release no Linux. | Build Release 0 erros; 79 testes da lógica ok no Linux |
| 2 | CI Windows: build Release, testes da lógica, telas reais (onboarding e principal) e execução do `.exe` real. | _em andamento_ |

Tentativas de correção de compilação da interface: **0 de 5**.

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

- _Aguardando o CI Windows desta rodada._
