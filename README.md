# WinAllApp

Ferramenta do **IFCE Campus Maracanaú** para **instalar softwares em lote por laboratório**.
O técnico escolhe o **Bloco** (BL1, BL2) e o **Laboratório**, marca os programas daquela sala e
instala tudo em fila, de forma silenciosa, sem travar a janela.

- Um único arquivo, **`WinAllApp.exe`**, portátil: roda direto do pendrive ou de uma pasta de rede, sem instalação.
- Funciona do **Windows 7 SP1 ao Windows 11**.
- A lista de blocos, laboratórios e programas (PDFs BL1/BL2) já vem embutida no executável.
- Na primeira execução, um **tutorial de 3 passos** explica o uso (pode ser reaberto no botão **Tutorial**).

---

## 1. Pré-requisitos

| Item | Windows 7 / 8.1 | Windows 10 / 11 |
|---|---|---|
| **.NET Framework 4.8** | Instalar (download gratuito da Microsoft) | Já vem no sistema |
| **Conta de Administrador** | Obrigatória | Obrigatória |
| **Acesso à pasta de rede** (padrão `\\10.50.11.2\informatica\NAC - Núcleo de Atendimento ao Cliente\Programas\Laboratórios - Programas`) | Obrigatório | Obrigatório |
| winget | Não existe no Windows 7/8.1 | Opcional (App "Instalador de Aplicativo") |
| Chocolatey | Opcional. Exige **.NET 4.8** e **TLS 1.2 ativado** (atualização KB3140245 + registro) | Não é usado |

> O WinAllApp confere sozinho a versão do Windows, o .NET 4.8, o TLS 1.2 e se o winget/Chocolatey existem.
> O resultado aparece no menu à esquerda, em **Sistema**.

## 2. Como baixar

Na página **Releases** do GitHub, pré-versão **`ultima-build`**, baixe o arquivo **`WinAllApp.exe`**.
Ela é atualizada automaticamente a cada alteração que passa em todos os testes.

## 3. Como executar (passo a passo)

1. Copie o `WinAllApp.exe` para o pendrive ou para uma pasta de rede.
2. Na máquina do laboratório, dê **dois cliques** no arquivo. O Windows pede permissão de administrador; clique em **Sim**.
   - Se estiver logado como aluno, clique com o botão direito no arquivo, escolha **Executar como administrador**
     e informe a conta de administrador.
3. Na primeira vez, o **tutorial** abre sozinho:
   1. **Escolha o bloco e o laboratório** no menu à esquerda.
   2. **Confira a pasta de rede**: o campo **Pasta de rede** (no topo) já vem com o caminho do campus e pode ser editado.
      Clique em **Verificar**: aparece "Pasta de rede acessível" (verde) ou "INACESSÍVEL" com um alerta vermelho.
      A verificação roda em segundo plano (spinner ao lado do campo), sem travar a janela.
   3. **Instale em lote**: marque os programas (ou **Selecionar Todos do Laboratório**) e clique em **Instalar selecionados**.
4. **Autodescoberta**: ao abrir um laboratório (e ao editar o campo **Pasta de rede** ou clicar em **Verificar**) o app
   procura em segundo plano cada instalador do `config.json` na pasta de rede:
   - ✔ verde: o arquivo (ou a pasta, em `copia_pasta`) existe; a checkbox fica liberada.
   - ❌ vermelho: não existe, ou a rede não respondeu em 5 s; a checkbox fica bloqueada e a linha mostra o caminho procurado.
   - Spinner cinza: ainda procurando. A janela continua respondendo, e "Selecionar Todos" só marca os encontrados.
   - `gerenciador` com `wingetId`/`chocoId` fica ✔ mesmo fora da rede (instala pelo winget/Chocolatey).
   A busca só lê a rede: o `config.json` nunca é alterado.
5. Use a **barra de pesquisa** para achar um programa pelo nome, versão ou categoria. O filtro é imediato e os itens
   marcados continuam marcados mesmo quando somem da lista. Com a pesquisa ativa, o botão vira **Selecionar os exibidos**.
6. Durante a instalação um **spinner** gira no rodapé e na linha em andamento, e o **contador** mostra
   "concluídos de total · instalados · falhas". A janela continua respondendo.
7. Se algo falhar, a linha fica vermelha com o **motivo**, e uma faixa vermelha no topo lista os programas que falharam.
   O **Registro da instalação** no rodapé guarda todos os detalhes.
8. Programas **licenciados** (AutoCAD, MATLAB, Proteus…) ficam como "Instalado (ativar licença)": a ativação é feita depois, à mão.

Marque **Não mostrar novamente** no tutorial para ele não abrir nas próximas vezes. A preferência fica em
`%APPDATA%\WinAllApp\preferencias.ini` (nada é gravado ao lado do .exe).

### Padronizar o Windows 10/11 (checklist da TI)

O botão **Padronizar Windows**, no menu lateral, abre a tela do checklist institucional (tópicos 3 a 8, 9.6 e 9.7 do
manual). Preencha e clique em **Padronizar agora**; o app mostra a lista do que vai mudar e pede confirmação.

| Campo | O que acontece |
|---|---|
| Bloco, Local e Nº | Renomeia para `BLOCO-LOCAL-XX` (ADM, BL1, BL2 ou BL3; local até 8 letras). Vale depois de reiniciar |
| Senha da Informatica | Cria ou ajusta a conta **Informatica** (Administrador, com senha) |
| Aluno ou Bolsista | Cria **Aluno** (Usuário Padrão, sem senha, papel de parede Lab) ou **Bolsista** (Administrador, sem senha, papel Adm) |
| Chave da etiqueta | Opcional. Ativa o Windows pelo licenciamento oficial; sem chave, só confere se já está ativado |
| Adicionar Impressora via IP | Opcional. Cria a porta TCP/IP e usa um driver genérico do Windows (qualquer marca) |
| Pasta dos papéis de parede | Copia "PapelParede – Adm" e "PapelParede - Lab" do ano mais recente para o disco local |
| Limpar área de trabalho | Apaga atalhos e instaladores das áreas de trabalho (nunca o WinAllApp.exe) |

Também sempre roda: teste de rede e internet, UAC em "Nunca notificar", GPOs que impedem trocar tema e plano de fundo
(em todas as contas e nas criadas depois) e Windows Update automático desabilitado. O app detecta sozinho se é
Windows 10 ou 11 e ajusta os comandos. Ativadores não oficiais não são usados e o antivírus nunca é desligado.
Programas (Chrome, WPS…), manutenção de disco e etiquetas continuam manuais. Detalhes em `STATUS_AUTOMACAO_CHECKLIST.md`.

Com `--simulacao` a tela funciona igual, mas nada é executado no computador.

### Opções de linha de comando

| Comando | O que faz |
|---|---|
| `WinAllApp.exe` | Usa o `config.json` ao lado do .exe; se não houver, usa o embutido |
| `WinAllApp.exe --extrair-config` | Grava o `config.json` embutido ao lado do .exe para você editar |
| `WinAllApp.exe --config outro.json` | Usa outro arquivo de configuração |
| `WinAllApp.exe --tutorial` | Abre o tutorial de boas-vindas de novo |
| `WinAllApp.exe --simulacao` | Modo de teste: instaladores fictícios embutidos, nada é instalado |
| `WinAllApp.exe --simulacao --simular-windows 7` | Simula o Windows 7 (ou `8.1`, `10`, `11`) para ver o roteamento |
| `... --sem-tls12` / `... --sem-dotnet48` | Na simulação, finge que falta o pré-requisito do Chocolatey |

## 4. Como o WinAllApp decide de onde instalar

A **pasta de rede é a fonte primária**. Cada programa do `config.json` tem uma `categoria`:

| Categoria | Para quê | Como instala |
|---|---|---|
| `gerenciador` | Programas gratuitos com pacote no winget/Chocolatey (GeoGebra, Octave, Python…) | 1º o instalador da pasta de rede, se existir. Senão: **winget** no Windows 10/11 ou **Chocolatey** no Windows 7/8.1 (só com .NET 4.8 e TLS 1.2) |
| `offline_licenciado` | Programas pagos ou com conta (AutoCAD, MATLAB, Proteus, PSIM…) | Só o instalador silencioso da pasta de rede. A ativação da licença é manual |
| `offline_gratuito` | Gratuitos sem pacote ou versões antigas (Octave 4.0, QGIS 3.0.2, MPLAB 8.53…) | Só o instalador da pasta de rede |
| `copia_pasta` | Programas portáteis e materiais de aula (Winplot, Logisim, Materiais) | Copia a pasta da rede para a máquina (com subpastas) |

**Windows 7:** programas marcados com `"windowsMinimo": "10"` (Python 3.9+, Arduino 2, VS 2022, QGIS recente…)
aparecem com o aviso **"Versão incompatível com o SO"** e não são instalados, a não ser que tenham uma
versão compatível fixada em `chocoVersao` (ex.: Python 3.8.10, Arduino 1.8.19), que então é instalada pelo Chocolatey.

Resumo do `config.json` atual: **23** `gerenciador`, **22** `offline_licenciado`, **20** `offline_gratuito`, **4** `copia_pasta`.

## 5. Como editar o `config.json`

1. Rode `WinAllApp.exe --extrair-config`. Um `config.json` aparece ao lado do .exe.
2. Edite com o Bloco de Notas (salve em UTF-8) e abra o WinAllApp de novo. Erros no arquivo aparecem numa mensagem ao abrir.

Estrutura:

```json
{
  "pastaInstaladores": "\\\\10.50.11.2\\informatica\\NAC - Núcleo de Atendimento ao Cliente\\Programas\\Laboratórios - Programas",
  "pastaDestinoCopias": "C:\\Programas",
  "blocos": [
    {
      "id": "BL2",
      "nome": "Bloco 2 (BL2)",
      "laboratorios": [
        { "id": "LCC", "nome": "LCC", "programas": [ "geogebra", "autocad-2018", "winplot" ] }
      ]
    }
  ],
  "programas": [ ... ]
}
```

- `pastaInstaladores`: pasta de rede com os instaladores (UNC, absoluta ou relativa ao config). É o valor inicial do campo
  **Pasta de rede** da tela, que pode ser trocado antes de instalar. Caminhos com espaços funcionam (vão entre aspas no disparo).
- `pastaDestinoCopias`: onde ficam os programas copiados (`copia_pasta`). Aceita `%VARIAVEIS%`.
- `blocos[].laboratorios[].programas`: ids dos programas de cada sala (o mesmo programa pode estar em várias salas).
- Nas barras invertidas do JSON, use `\\` (ex.: `"AutoCAD2018\\Setup.exe"`).

### Adicionar um programa

Acrescente um item em `programas` e coloque o `id` na lista do laboratório. Exemplos, um de cada categoria:

```json
{ "id": "geogebra", "nome": "GeoGebra", "categoria": "gerenciador",
  "instalador": "GeoGebra\\GeoGebra-Windows-Installer.msi", "tipo": "msi", "argumentos": "/qn /norestart",
  "wingetId": "GeoGebra.Classic", "chocoId": "geogebra-classic" },

{ "id": "python", "nome": "Python", "categoria": "gerenciador",
  "instalador": "Python\\python-setup.exe", "argumentos": "/quiet InstallAllUsers=1 PrependPath=1",
  "wingetId": "Python.Python.3.13", "chocoId": "python3", "chocoVersao": "3.8.10", "windowsMinimo": "10" },

{ "id": "autocad-2018", "nome": "AutoCAD 2018", "categoria": "offline_licenciado",
  "instalador": "AutoCAD2018\\Setup.exe", "argumentos": "/W /q /I AutoCAD2018.ini", "timeoutMinutos": 120 },

{ "id": "octave-4.0", "nome": "GNU Octave 4.0", "categoria": "offline_gratuito",
  "instalador": "Octave4.0\\octave-4.0.0-installer.exe", "argumentos": "/S" },

{ "id": "winplot", "nome": "Winplot", "categoria": "copia_pasta",
  "instalador": "Winplot", "destino": "C:\\Programas\\Winplot" }
```

| Campo | Obrigatório | Descrição |
|---|---|---|
| `id`, `nome` | sim | Identificador único e nome exibido |
| `categoria` | recomendado | `gerenciador`, `offline_licenciado`, `offline_gratuito` ou `copia_pasta`. Sem ela: `gerenciador` se houver `wingetId`/`chocoId`, senão `offline_gratuito` |
| `instalador` | sim (exceto `gerenciador` só com pacote) | Arquivo na pasta de rede; em `copia_pasta`, a **pasta** a copiar |
| `tipo` | não | `exe`, `msi`, `ps1` (PowerShell), `bat` ou `cmd` (deduzido pela extensão) |
| `argumentos` | recomendado | Parâmetros silenciosos (`/S`, `/quiet`, `/VERYSILENT`…). MSI usa `/qn /norestart` por padrão |
| `wingetId`, `wingetVersao` | não | Pacote e versão fixa no winget (Windows 10/11) |
| `chocoId`, `chocoVersao` | não | Pacote e versão fixa no Chocolatey (Windows 7/8.1) |
| `windowsMinimo` | não | `7`, `8.1`, `10` ou `11`. Abaixo disso, aviso "Versão incompatível com o SO" |
| `destino` | não | Só `copia_pasta`: pasta local (padrão `pastaDestinoCopias\id`) |
| `timeoutMinutos` | não | Tempo máximo do instalador (padrão 60) |
| `codigosSucesso` | não | Códigos de saída de sucesso (padrão 0, 1641 e 3010) |
| `observacao` | não | Nota exibida abaixo do nome |

## 6. Preparar a pasta de rede (tarefa da TI)

A pasta `\\servidor\instaladores` precisa ter os instaladores e pastas citados no `config.json`.
Para os programas com pacote no winget, uma máquina Windows 10/11 da TI pode baixá-los para a pasta com
`winget download` (isso **não** faz parte do WinAllApp, que só instala nas máquinas dos laboratórios):

```powershell
winget download --id GeoGebra.Classic -d \\servidor\instaladores\GeoGebra
winget download --id GNU.Octave --version 7.3.0 -d \\servidor\instaladores\Octave7.3.0
```

Scripts (`.ps1`): quando um programa precisa de passos extras (ex.: portátil que é copiado e ganha atalho), use um
script PowerShell. O WinAllApp roda com `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "script.ps1" argumentos`,
sem mudar a política do computador. Escreva só com recursos do **PowerShell 2.0** (o do Windows 7), que também
rodam no 5.1 do Windows 10/11: nada de `$PSScriptRoot`, `-in`, `[ordered]`, `Invoke-WebRequest` etc., e salve o
arquivo sem acentos (ASCII). Modelo pronto: `exemplos/copiar-portatil.ps1`. Arquivos `.bat` antigos continuam aceitos.

Instaladores licenciados: copie para a pasta o **pacote de implantação** gerado no portal do fabricante
(ex.: Autodesk). O WinAllApp não preenche contas nem ativa licenças.

## 7. Compilar (desenvolvedores)

```bash
dotnet build WinAllApp.sln -c Release
dotnet test tests/WinAllApp.Core.Tests -c Release   # qualquer SO (inclui a simulação Win10/Win7 do roteamento)
dotnet test tests/WinAllApp.UI.Tests -c Release     # só Windows (abre as telas reais e o .exe)
```

Funciona com o SDK .NET 8 em Windows ou Linux, sem Visual Studio.
Saída: `src/WinAllApp/bin/Release/net48/WinAllApp.exe` (arquivo único, com a logo e o ícone do IFCE).
O CI (`.github/workflows/build.yml`) compila em Release no `windows-latest`, roda os testes e publica o `ultima-build`.

Andamento e decisões: [`STATUS_MOTOR_ADAPTATIVO.md`](STATUS_MOTOR_ADAPTATIVO.md), [`STATUS_RELEASE.md`](STATUS_RELEASE.md)
e [`STATUS_INSTALLER.md`](STATUS_INSTALLER.md).
