# STATUS_AUTOMACAO_CHECKLIST

> Fonte de verdade da automação do checklist institucional ("Checklist Windows 10 – Manual Completo"): tópicos 3 a 8,
> 9.6 modificado (impressora genérica por IP) e 9.7, no Windows 10 e no Windows 11.
> Branch: `claude/automacao-checklist`. Atualizado a cada ciclo.

**Situação: em andamento (ciclo 1, aguardando o CI Windows).** Versão 0.4.0.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 0 | Leitura do manual (89 páginas) e do código. Plano abaixo. | Plano definido |
| 1 | Motor `Services/Padronizacao` (11 etapas), tela `PadronizacaoWindow.xaml` aberta pelo botão "Padronizar Windows", 73 testes no Linux, 8 testes que rodam os scripts de verdade no runner Windows e 1 teste da janela. Scripts conferidos com o parser do PowerShell. | 189 testes ok no Linux; CI Windows pendente |

Tentativas de correção de script/permissão: **0 de 5**.

## O que entra e o que fica fora

| Tópico do manual | Decisão |
|---|---|
| 3 Ativação | Entra, só pelo caminho oficial: chave da etiqueta com o licenciamento do próprio Windows. O ativador "Re-Loader" e o desligamento do Windows Defender **não** são automatizados (ver seção 6). |
| 4 Contas | Entra: Informatica (Administrador, com senha) e Aluno (Padrão, sem senha) ou Bolsista (Administrador, sem senha). |
| 5 UAC | Entra: "Nunca notificar". |
| 6.1 Papel de parede | Entra: copia a imagem do ano mais recente para o disco local e aplica por conta (Lab para Aluno, Adm para as demais). |
| 6.2 GPOs | Entra, como chaves de registro: "Impedir a alteração de tema" e "Impedir a alteração de plano de fundo". |
| 6.3 Windows Update | Entra, como chave de registro: "Configurar Atualizações Automáticas = Desabilitado". |
| 7 Nome do computador | Entra: BLOCO-LOCAL-XX validado. |
| 8 Rede e internet | Entra: teste do servidor de arquivos e da internet (só diagnóstico). |
| 9.1 a 9.5 (Chrome, WPS, K-Lite, OCS, UltraVNC) | **Fora**, por exclusão estrita. |
| 9.6 Impressora | Entra modificado: opcional, qualquer impressora por IP com porta TCP/IP e driver genérico do Windows. Sem marcas. |
| 9.7 Pós-instalação | Entra: apagar atalhos e instaladores da área de trabalho. |
| 10 (HD, %temp%, desfragmentar, scandisk, stress, memória) | **Fora**, por exclusão estrita. |
| 11 Etiquetas, 12 Lacrar tampa | **Fora** (trabalho físico). |

## 1) Compatibilidade Win10/Win11

- Plano: detectar pelo build (`CurrentBuildNumber` ≥ 22000 = Windows 11) com o `DetectorAmbiente` que o app já usa, e
  conferir de novo dentro do PowerShell com `Get-CimInstance Win32_OperatingSystem`. Abaixo do Windows 10 a padronização
  não roda.
- Adaptações previstas: ativação por `slmgr.vbs` no Windows 10 e pela classe CIM `SoftwareLicensingService` no Windows 11
  (o VBScript virou recurso opcional em remoção no 11); no Windows 11 também desligar "Receber as atualizações mais
  recentes assim que estiverem disponíveis". Nada usa `wmic`, que não existe mais no Windows 11 24H2.

## 2) Gestão de Contas e Segurança

- Plano: `net user` e `net localgroup`, com os nomes dos grupos Administradores/Usuários resolvidos pelo SID
  (S-1-5-32-544 e S-1-5-32-545), para funcionar em Windows em português ou inglês. A senha da Informatica vai por
  variável de ambiente e é gravada por ADSI, sem aparecer em linha de comando.
- UAC "Nunca notificar": `ConsentPromptBehaviorAdmin = 0` e `PromptOnSecureDesktop = 0` em
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` (`EnableLUA` continua 1, como faz o controle deslizante).

## 3) GPOs e Personalização

- Plano: para cada conta (e para o perfil Default, que vale para contas criadas depois), carregar o `NTUSER.DAT` com
  `reg load` e gravar `Policies\ActiveDesktop\NoChangingWallPaper = 1`, `Policies\Explorer\NoThemesTab = 1` e o papel de
  parede. Se o perfil ainda não existe, criá-lo com `CreateProfile` (userenv.dll).
- Windows Update: `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU\NoAutoUpdate = 1`.

## 4) Sistema e Impressora via IP

- Plano: nome validado no app (`BLOCO` ∈ ADM, BL1, BL2, BL3; `LOCAL` até 8 caracteres; `XX` com 2 dígitos; 15
  caracteres no máximo) e aplicado com `Rename-Computer` (vale depois de reiniciar).
- Impressora: só se a opção estiver marcada e o IP for válido. `Add-PrinterPort -PrinterHostAddress` e `Add-Printer`
  com o primeiro driver genérico da Microsoft disponível. O manual pedia o CD da Brother copiado do servidor; o
  repositório não tinha código disso, então não há o que remover.

## 5) Limpeza e Recursos Nativos

- Plano: ligar o spooler e, se os cmdlets de impressão faltarem, `Enable-WindowsOptionalFeature`. Apagar atalhos
  (`.lnk`, `.url`) e instaladores (`.exe`, `.msi`) das áreas de trabalho, sem apagar o próprio WinAllApp.exe.

## 6) Falhas de script ou permissões pendentes

- Nenhuma ainda.
