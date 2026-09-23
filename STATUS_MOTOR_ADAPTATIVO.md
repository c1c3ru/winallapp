# STATUS_MOTOR_ADAPTATIVO

> Fonte de verdade do motor de instalação com 4 cenários de implantação. Atualizado a cada ciclo.
> Branch: `claude/installer-por-laboratorio`.

## Histórico de ciclos

| Ciclo | O que foi feito | Resultado |
|---|---|---|
| 1 | Modelo de dados (`categoria`, `wingetId`, `wingetVersao`, `chocoId`, `chocoVersao`, `windowsMinimo`, `destino`, `pastaDestinoCopias`), detector de ambiente pelo registro, 4 estratégias + roteador (Strategy Pattern), cópia de pasta em código, fila refeita sobre o roteador. | Build 0 erros |
| 2 | Mock com 4 programas (lab. "Motor: 4 categorias") + 2 de Windows 7 (lab. "Windows 7: versões novas"), `winget.bat`/`choco.bat` fictícios, instalador licenciado numa pasta **com espaço**, pasta portátil com subpasta. Opções `--simular-windows`, `--sem-tls12`, `--sem-dotnet48`. | Simulação Win 10 e Win 7 no Linux: **79 testes ok** (1 só Windows), comandos conferidos no log (abaixo) |
| 3 | `config.json` real classificado pela análise de pacotes: 23 `gerenciador` (com os 23 ids do winget), 22 `offline_licenciado`, 20 `offline_gratuito`, 4 `copia_pasta`. | Teste confere as contagens e que os 23 geram `winget install ... --silent` no Windows 10 |

Tentativas de correção estrutural usadas: **0 de 5** (o roteamento e o escape de caminhos passaram na primeira simulação).

## 1) Lógica de Fonte Primária

- A **pasta de rede** (`pastaInstaladores`, padrão `\\servidor\instaladores`) é a fonte primária de todas as categorias.
- `gerenciador`: se o instalador existir na pasta de rede, ele é usado; só na falta dele entra o winget/Chocolatey.
- `offline_licenciado` e `offline_gratuito`: **só** a pasta de rede. Ausente = falha com o caminho procurado
  (licenciado: "copie para lá o pacote de implantação do fabricante").
- `copia_pasta`: a origem é uma pasta dentro da pasta de rede; o destino é `destino` ou `pastaDestinoCopias\id` (aceita `%VARIAVEIS%`).
- O menu lateral mostra se a pasta de rede está acessível (verificação assíncrona; botão "Verificar rede").
- Licenças: o app só dispara o instalador silencioso da rede. Nenhuma ativação, conta ou licença é automatizada.

## 2) Roteamento de Instalação

`RoteadorInstalacao` escolhe a estratégia pela `categoria` (`EstrategiaGerenciador`, `EstrategiaOffline` licenciado/gratuito,
`EstrategiaCopiaPasta`) e devolve um `PlanoInstalacao` (Processo, CopiarPasta, Bloqueado ou Incompatível) com a descrição
que vai para o registro.

| Situação | Ação gerada |
|---|---|
| Qualquer categoria com instalador na rede | `Process.Start` do instalador da rede (exe direto, `msiexec /i "..."`, ou `cmd /c ""...bat" args"`) |
| `gerenciador`, Win 10/11, sem instalador na rede | `winget install --id <id> --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity [--version X]` |
| `gerenciador`, Win 7/8.1, sem instalador na rede | `choco install <id> -y --no-progress [--version X]` (só com .NET 4.8 + TLS 1.2 + choco presente) |
| `copia_pasta` | Cópia recursiva em código (equivalente a `robocopy /E`), sobrescrevendo |

Código de saída do winget "já instalado" (0x8A15002B) conta como sucesso.

Registro da simulação (teste `RoteamentoTests`, caminhos de teste):

```
== Windows 10
mock-winget      [Winget] "C:\Ferramentas\winget.exe" install --id GeoGebra.Classic --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
mock-licenciado  [Rede] "cmd.exe" /c ""/tmp/servidor/instaladores/Pacote Licenciado\setup-licenciado.bat" /qb /norestart"
mock-gratuito    [Rede] "cmd.exe" /c ""/tmp/servidor/instaladores/octave-setup.bat" /S"
mock-copia       [Cópia] "/tmp/servidor/instaladores/Portatil" -> "/tmp/Programas/mock-copia"
== Windows 7 (.NET 4.8 e TLS 1.2 ok)
mock-winget      [Chocolatey] "C:\ProgramData\chocolatey\bin\choco.exe" install geogebra-classic -y --no-progress
mock-licenciado  [Rede] (igual ao Windows 10)
mock-gratuito    [Rede] (igual ao Windows 10)
mock-copia       [Cópia] (igual ao Windows 10)
== Windows 7: versões novas
mock-python      [Chocolatey] "C:\ProgramData\chocolatey\bin\choco.exe" install python3 -y --no-progress --version 3.8.10
mock-vs2022      [Incompatível] Versão incompatível com o SO: Visual Studio 2022 (fictício) 2022 exige Windows 10 ou mais novo; esta máquina é Windows 7.
```

No Windows (CI), a mesma simulação roda com os `.bat` reais e o teste confere as linhas gravadas por `winget.bat`/`choco.bat`,
pelo instalador licenciado (pasta com espaço) e pelo gratuito, além dos arquivos copiados. O escape de caminho UNC com espaço
(`"\\servidor\instaladores\Pacote Licenciado\AutoCAD 2018\Setup.exe"`) tem teste próprio.

## 3) Validação de Pré-requisitos Win 7

`DetectorAmbiente` lê o registro:
- **Windows**: `CurrentMajorVersionNumber`/`CurrentBuildNumber` (10/11) ou `CurrentVersion` 6.1 (7), 6.3 (8.1).
- **.NET 4.8**: `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release >= 528040`.
- **TLS 1.2**: `HKLM\SYSTEM\...\SCHANNEL\Protocols\TLS 1.2\Client`. No Windows 7 exige `DisabledByDefault=0` (vem desligado);
  do 8 em diante vale o padrão ligado, salvo `Enabled=0`.
- **winget**: `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe` ou PATH (só Win 10 build 17763+). **choco**: `%ChocolateyInstall%`, `%ProgramData%\chocolatey\bin` ou PATH.

Sem .NET 4.8 ou TLS 1.2, o Chocolatey **não é chamado**: o item falha com o motivo e o registro avisa ao abrir.
`windowsMinimo` bloqueia versões modernas no Windows 7 com "Versão incompatível com o SO" (aviso já na lista, antes de instalar),
exceto quando há `chocoVersao` compatível (Python 3.8.10, Arduino 1.8.19). No config real: Python, Arduino, VS 2022, Mendeley,
QGIS, AutoCAD 2022 e SketchUp têm `windowsMinimo: "10"`.

## 4) Falhas ou dependências de metadados no JSON

- Ids do Chocolatey: só `arduino` e `octave` foram conferidos no site (a rede do ambiente de desenvolvimento bloqueia a API).
  Os demais (`python3`, `geogebra-classic`, `miktex`, `texstudio`, `googleearthpro`, `qgis-ltr`, `ltspice`, `librecad`,
  `codeblocks`, `windjview`, `visualstudio2019community`, `visualstudio2022community`) são conhecidos, mas não conferidos.
- Versões fixas para Windows 7 (`chocoVersao`): Python 3.8.10, Arduino 1.8.19 e Octave 7.3.0; confirmar numa máquina real.
- Parâmetros silenciosos marcados "a confirmar" no config continuam pendentes (instaladores reais não disponíveis).
- Pendências herdadas: lista dos "programas padrões" (BIOQ e LAMEP) e confirmação de CLP CLIP = CLP CLIC e "Látex".
- O download real de pacotes winget/Chocolatey não foi testado (fora do escopo: rede bloqueada); a estrutura de chamada está pronta e simulada.
