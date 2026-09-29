using System;
using System.Text;

namespace WinAllApp.Core.Services.Padronizacao
{
    /// <summary>
    /// Scripts do Windows PowerShell 5.1 (nativo no Windows 10 e 11) para cada tópico do checklist.
    /// Nenhum valor digitado pelo técnico entra no texto do script: tudo chega por variáveis de ambiente
    /// (WINALLAPP_*), então não há como injetar comandos, e a senha não aparece em nenhuma linha de comando.
    /// Código de saída: 0 = concluído, 2 = concluído com aviso, 1 = falhou. A última linha "RESULTADO:" vira o resumo na tela.
    /// </summary>
    public static class ScriptsPadronizacao
    {
        public const string VarWindows = "WINALLAPP_WINDOWS";
        public const string VarSenha = "WINALLAPP_SENHA";
        public const string VarContaUsuario = "WINALLAPP_CONTA_USUARIO";
        public const string VarUsuarioAdmin = "WINALLAPP_USUARIO_ADMIN";
        public const string VarChave = "WINALLAPP_CHAVE";
        public const string VarPapelAdm = "WINALLAPP_PAPEL_ADM";
        public const string VarPapelLab = "WINALLAPP_PAPEL_LAB";
        public const string VarNome = "WINALLAPP_NOME";
        public const string VarImpressoraIp = "WINALLAPP_IMPRESSORA_IP";
        public const string VarImpressoraNome = "WINALLAPP_IMPRESSORA_NOME";
        public const string VarExecutavel = "WINALLAPP_EXECUTAVEL";

        /// <summary>Drivers genéricos que vêm com o Windows, na ordem de preferência (nenhum de fabricante).</summary>
        public static readonly string[] DriversGenericos = { "Microsoft PCL6 Class Driver", "Microsoft PS Class Driver", "Generic / Text Only" };

        /// <summary>Id do Windows no licenciamento (SoftwareLicensingProduct.ApplicationID).</summary>
        public const string AplicacaoWindows = "55c92734-d682-4d71-983e-d6ec3f16059f";

        /// <summary>Envolve o corpo com o tratamento de erro comum e as funções de apoio.</summary>
        public static string Montar(string corpo)
        {
            var texto = new StringBuilder();
            texto.Append(Prelude);
            texto.Append("try {\n");
            texto.Append(corpo.Replace("\r\n", "\n"));
            texto.Append("\n} catch {\n");
            texto.Append("    Write-Output ('ERRO: ' + $_.Exception.Message)\n");
            texto.Append("    exit 1\n");
            texto.Append("}\n");
            texto.Append("if ($script:TeveAviso) { exit 2 }\n");
            texto.Append("exit 0\n");
            return texto.ToString();
        }

        private const string Prelude = """
            $ErrorActionPreference = 'Stop'
            $ProgressPreference = 'SilentlyContinue'
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $script:TeveAviso = $false

            function Aviso([string]$texto) {
                $script:TeveAviso = $true
                Write-Output ('AVISO: ' + $texto)
            }

            function Resultado([string]$texto) { Write-Output ('RESULTADO: ' + $texto) }

            # Executa um programa nativo (net.exe, reg.exe...) e falha com a saída dele se o código não for 0.
            function Invocar([string]$exe, [string[]]$argumentos) {
                $anterior = $ErrorActionPreference
                $ErrorActionPreference = 'Continue'
                try { $saida = (& $exe @argumentos 2>&1 | Out-String) } finally { $ErrorActionPreference = $anterior }
                if ($LASTEXITCODE -ne 0) {
                    throw ($exe + ' ' + $argumentos[0] + ' falhou (código ' + $LASTEXITCODE + '): ' + $saida.Trim())
                }
                return $saida
            }

            function DefinirDword([string]$caminho, [string]$nome, [int]$valor) {
                if (-not (Test-Path -LiteralPath $caminho)) { New-Item -Path $caminho -Force | Out-Null }
                New-ItemProperty -LiteralPath $caminho -Name $nome -Value $valor -PropertyType DWord -Force | Out-Null
            }


            """;

        /// <summary>Confere o Windows pelo CIM (além do registro lido pelo app) e se o app está como Administrador.</summary>
        public static string VerificarSistema() => Montar("""
                $so = Get-CimInstance -ClassName Win32_OperatingSystem
                $build = [int]$so.BuildNumber
                $versao = [Environment]::OSVersion.Version
                if ($versao.Major -lt 10) { throw ('A padronização é só para Windows 10 e 11; este é ' + $so.Caption + '.') }
                $detectado = if ($build -ge 22000) { 'Windows 11' } else { 'Windows 10' }
                Write-Output ('Sistema: ' + $so.Caption + ' (build ' + $build + ')')
                if ($detectado -ne $env:WINALLAPP_WINDOWS) {
                    Aviso ('O app detectou ' + $env:WINALLAPP_WINDOWS + ' e o CIM ' + $detectado + '; os comandos seguem o ' + $env:WINALLAPP_WINDOWS + '.')
                }
                $identidade = [Security.Principal.WindowsIdentity]::GetCurrent()
                $admin = (New-Object Security.Principal.WindowsPrincipal($identidade)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
                if (-not $admin) { throw 'Abra o WinAllApp como Administrador.' }
                Resultado ($detectado + ' (build ' + $build + '), executando como Administrador.')
            """);

        /// <summary>
        /// Tópico 3. Só o caminho oficial: a chave da etiqueta no licenciamento do próprio Windows.
        /// Windows 10: slmgr.vbs. Windows 11: classe CIM SoftwareLicensingService (o VBScript virou recurso opcional em remoção).
        /// Nenhuma proteção (antivírus, Defender) é desligada.
        /// </summary>
        public static string Ativacao(VersaoPadronizacao versao)
        {
            var instalarChave = versao == VersaoPadronizacao.Windows11
                ? """
                        # Windows 11: licenciamento pelo CIM, sem depender do VBScript.
                        $servico = Get-CimInstance -ClassName SoftwareLicensingService
                        Invoke-CimMethod -InputObject $servico -MethodName InstallProductKey -Arguments @{ ProductKey = $chave } | Out-Null
                        Invoke-CimMethod -InputObject $servico -MethodName RefreshLicenseStatus | Out-Null
                        $produto = ProdutoWindows
                        if ($produto) { Invoke-CimMethod -InputObject $produto -MethodName Activate | Out-Null }
                  """
                : """
                        # Windows 10: slmgr.vbs pelo cscript (console, sem janelas).
                        $slmgr = Join-Path $env:windir 'System32\slmgr.vbs'
                        $cscript = Join-Path $env:windir 'System32\cscript.exe'
                        Invocar $cscript @('//nologo', $slmgr, '/ipk', $chave) | Out-Null
                        Invocar $cscript @('//nologo', $slmgr, '/ato') | Out-Null
                  """;

            return Montar("""
                function ProdutoWindows {
                    Get-CimInstance -ClassName SoftwareLicensingProduct -Filter "ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" |
                        Select-Object -First 1
                }
                $produto = ProdutoWindows
                if ($produto -and $produto.LicenseStatus -eq 1) {
                    Resultado 'O Windows já está ativado.'
                } else {
                    $chave = ([string]$env:WINALLAPP_CHAVE).Trim().ToUpperInvariant()
                    if ($chave.Length -eq 0) {
                        Aviso 'O Windows não está ativado e nenhuma chave foi informada. Digite a chave da etiqueta do gabinete e rode de novo.'
                        Resultado 'Windows não ativado (sem chave).'
                    } else {
                """ + instalarChave + """

                        $produto = ProdutoWindows
                        if (-not $produto -or $produto.LicenseStatus -ne 1) {
                            throw 'A chave foi instalada, mas a ativação não concluiu. Confira a internet e se a chave é a da etiqueta.'
                        }
                        Resultado 'Windows ativado com a chave da etiqueta.'
                    }
                }
                """);
        }

        /// <summary>
        /// Tópico 4. Contas pelo net user / net localgroup, com os grupos resolvidos pelo SID
        /// (Administradores = S-1-5-32-544, Usuários = S-1-5-32-545) para servir em Windows em qualquer idioma.
        /// A senha vem de WINALLAPP_SENHA e é gravada por ADSI, sem passar por linha de comando.
        /// </summary>
        public static string Contas() => Montar("""
                function NomeDoGrupo([string]$sid) {
                    (New-Object Security.Principal.SecurityIdentifier($sid)).Translate([Security.Principal.NTAccount]).Value.Split('\')[-1]
                }
                $administradores = NomeDoGrupo 'S-1-5-32-544'
                $usuarios = NomeDoGrupo 'S-1-5-32-545'
                $maquina = $env:COMPUTERNAME

                # Compara pelo ADsPath dos membros (WinNT://GRUPODETRABALHO/MAQUINA/conta); IsMember não reconhece contas locais.
                function EstaNoGrupo([string]$grupo, [string]$conta) {
                    $g = [ADSI]('WinNT://' + $maquina + '/' + $grupo + ',group')
                    foreach ($membro in @($g.Invoke('Members'))) {
                        $caminho = $membro.GetType().InvokeMember('ADsPath', 'GetProperty', $null, $membro, $null)
                        if ($caminho -like ('*/' + $maquina + '/' + $conta)) { return $true }
                    }
                    return $false
                }

                # [ADSI]::Exists lança "The user name could not be found" em vez de devolver falso no provedor WinNT.
                function ContaExiste([string]$conta) {
                    try { return [ADSI]::Exists('WinNT://' + $maquina + '/' + $conta + ',user') } catch { return $false }
                }

                function GarantirConta([string]$conta, [bool]$admin, [string]$senha) {
                    $existia = ContaExiste $conta
                    if (-not $existia) {
                        # /passwordreq:no: a conta nasce sem senha mesmo se a política exigir senha; a da Informatica é gravada logo abaixo.
                        Invocar 'net.exe' @('user', $conta, '/add', '/active:yes', '/passwordreq:no') | Out-Null
                    } else {
                        Invocar 'net.exe' @('user', $conta, '/active:yes') | Out-Null
                    }
                    $usuario = [ADSI]('WinNT://' + $maquina + '/' + $conta + ',user')
                    $flags = ([int]$usuario.UserFlags.Value) -bor 0x10000  # a senha nunca expira
                    if ([string]::IsNullOrEmpty($senha)) {
                        $usuario.UserFlags.Value = $flags -bor 0x20  # senha não exigida
                        $usuario.SetInfo()
                        if ($existia) {
                            try { $usuario.SetPassword('') } catch { Aviso ('A conta ' + $conta + ' já existia e ficou com a senha atual: ' + $_.Exception.InnerException.Message) }
                        }
                    } else {
                        $usuario.SetPassword($senha)
                        $usuario.UserFlags.Value = $flags -band (-bnot 0x20)
                        $usuario.SetInfo()
                    }

                    if (-not (EstaNoGrupo $usuarios $conta)) { Invocar 'net.exe' @('localgroup', $usuarios, $conta, '/add') | Out-Null }
                    if ($admin) {
                        if (-not (EstaNoGrupo $administradores $conta)) { Invocar 'net.exe' @('localgroup', $administradores, $conta, '/add') | Out-Null }
                    } elseif (EstaNoGrupo $administradores $conta) {
                        Invocar 'net.exe' @('localgroup', $administradores, $conta, '/delete') | Out-Null
                    }

                    $tipo = if ($admin) { 'Administrador' } else { 'Usuário Padrão' }
                    $protecao = if ([string]::IsNullOrEmpty($senha)) { 'sem senha' } else { 'com senha' }
                    $acao = if ($existia) { 'ajustada' } else { 'criada' }
                    Write-Output ('Conta ' + $conta + ' ' + $acao + ': ' + $tipo + ', ' + $protecao + '.')
                }

                if ([string]::IsNullOrEmpty($env:WINALLAPP_SENHA)) { throw 'A senha da conta Informatica não foi informada.' }
                GarantirConta 'Informatica' $true $env:WINALLAPP_SENHA
                GarantirConta $env:WINALLAPP_CONTA_USUARIO ($env:WINALLAPP_USUARIO_ADMIN -eq '1') ''
                Resultado ('Contas Informatica e ' + $env:WINALLAPP_CONTA_USUARIO + ' prontas.')
            """);

        /// <summary>Tópico 5: UAC em "Nunca notificar" (o mesmo que o controle deslizante grava; o UAC continua ligado).</summary>
        public static string Uac() => Montar("""
                $sistema = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
                DefinirDword $sistema 'ConsentPromptBehaviorAdmin' 0
                DefinirDword $sistema 'PromptOnSecureDesktop' 0
                DefinirDword $sistema 'EnableLUA' 1
                Resultado 'UAC em "Nunca notificar".'
            """);

        /// <summary>
        /// Funções para gravar no registro de cada conta (NTUSER.DAT) e no perfil Default, que é copiado para
        /// as contas criadas depois. Perfil ainda inexistente é criado com CreateProfile (userenv.dll);
        /// hive carregado com reg load é sempre descarregado no finally.
        /// </summary>
        private const string FuncoesPerfis = """
                $assinaturas = '[DllImport("userenv.dll", CharSet = CharSet.Unicode)] ' +
                    'public static extern int CreateProfile(string sid, string conta, System.Text.StringBuilder caminho, uint tamanho); ' +
                    '[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] ' +
                    'public static extern bool SystemParametersInfo(uint acao, uint parametro, string valor, uint opcoes);'
                Add-Type -Namespace WinAllApp -Name Perfil -MemberDefinition $assinaturas

                $listaPerfis = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
                $sidAtual = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value

                function SidDaConta([string]$conta) {
                    (New-Object Security.Principal.NTAccount($env:COMPUTERNAME, $conta)).Translate([Security.Principal.SecurityIdentifier]).Value
                }

                function GravarReg([string]$raiz, [string]$chave, [string]$nome, [string]$tipo, [string]$valor) {
                    Invocar 'reg.exe' @('add', ($raiz + '\' + $chave), '/v', $nome, '/t', $tipo, '/d', $valor, '/f') | Out-Null
                }

                # Executa $gravar com a raiz do registro da conta ("HKU\..."); conta vazia = perfil Default.
                function NoPerfil([string]$conta, [scriptblock]$gravar) {
                    $sid = $null
                    if ($conta) {
                        $sid = SidDaConta $conta
                        $chavePerfil = Join-Path $listaPerfis $sid
                        if (-not (Test-Path -LiteralPath $chavePerfil)) {
                            $caminho = New-Object System.Text.StringBuilder 260
                            $hr = [WinAllApp.Perfil]::CreateProfile($sid, $conta, $caminho, 260)
                            if ($hr -ne 0 -and $hr -ne -2147024713) { throw ('Não foi possível criar o perfil de ' + $conta + (' (0x{0:X8}).' -f $hr)) }
                        }
                        if (Test-Path -LiteralPath ('Registry::HKEY_USERS\' + $sid)) {
                            & $gravar ('HKU\' + $sid) $sid
                            return
                        }
                        $pasta = (Get-ItemProperty -LiteralPath $chavePerfil).ProfileImagePath
                    } else {
                        $pasta = (Get-ItemProperty -LiteralPath $listaPerfis).Default
                    }
                    $pasta = [Environment]::ExpandEnvironmentVariables($pasta)
                    $apelido = 'HKU\WinAllApp_' + [Guid]::NewGuid().ToString('N')
                    Invocar 'reg.exe' @('load', $apelido, (Join-Path $pasta 'NTUSER.DAT')) | Out-Null
                    try {
                        & $gravar $apelido $sid
                    } finally {
                        [GC]::Collect()
                        [GC]::WaitForPendingFinalizers()
                        $descarregado = $false
                        for ($i = 0; $i -lt 5 -and -not $descarregado; $i++) {
                            try { Invocar 'reg.exe' @('unload', $apelido) | Out-Null; $descarregado = $true } catch { Start-Sleep -Milliseconds 500 }
                        }
                        if (-not $descarregado) { Aviso ('O registro de ' + $pasta + ' ficou carregado; reinicie antes de entrar nessa conta.') }
                    }
                }

                # Contas a padronizar: Informatica (Adm), a do usuário (Lab para Aluno, Adm para Bolsista) e o perfil Default (Adm).
                $contaUsuario = $env:WINALLAPP_CONTA_USUARIO
                $papelUsuario = if ($contaUsuario -eq 'Aluno') { $env:WINALLAPP_PAPEL_LAB } else { $env:WINALLAPP_PAPEL_ADM }
                $perfis = @(
                    @{ Conta = 'Informatica'; Papel = $env:WINALLAPP_PAPEL_ADM; Rotulo = 'Informatica' },
                    @{ Conta = $contaUsuario; Papel = $papelUsuario; Rotulo = $contaUsuario },
                    @{ Conta = ''; Papel = $env:WINALLAPP_PAPEL_ADM; Rotulo = 'contas novas (perfil Default)' }
                )

            """;

        /// <summary>Tópico 6.1: papel de parede (já copiado para o disco local) em cada conta, também como política.</summary>
        public static string PapelDeParede() => Montar(FuncoesPerfis + """
                foreach ($perfil in $perfis) {
                    $papel = $perfil.Papel
                    if (-not $papel -or -not (Test-Path -LiteralPath $papel)) { throw ('Papel de parede local não encontrado para ' + $perfil.Rotulo + '.') }
                    NoPerfil $perfil.Conta {
                        param($raiz, $sid)
                        GravarReg $raiz 'Control Panel\Desktop' 'Wallpaper' 'REG_SZ' $papel
                        GravarReg $raiz 'Control Panel\Desktop' 'WallpaperStyle' 'REG_SZ' '10'
                        GravarReg $raiz 'Control Panel\Desktop' 'TileWallpaper' 'REG_SZ' '0'
                        GravarReg $raiz 'Software\Microsoft\Windows\CurrentVersion\Policies\System' 'Wallpaper' 'REG_SZ' $papel
                        GravarReg $raiz 'Software\Microsoft\Windows\CurrentVersion\Policies\System' 'WallpaperStyle' 'REG_SZ' '4'
                        if ($sid -and $sid -eq $sidAtual) { [WinAllApp.Perfil]::SystemParametersInfo(0x14, 0, $papel, 3) | Out-Null }
                    }
                    Write-Output ('Papel de parede de ' + $perfil.Rotulo + ': ' + (Split-Path -Leaf $papel))
                }
                Resultado 'Papel de parede aplicado (Adm e Lab conforme a conta).'
            """);

        /// <summary>
        /// Tópico 6.2: as GPOs "Impedir a alteração de tema" e "Impedir a alteração de plano de fundo"
        /// (Configuração do Usuário) gravadas direto no registro de cada conta, sem gpedit.msc.
        /// </summary>
        public static string GposPersonalizacao() => Montar(FuncoesPerfis + """
                foreach ($perfil in $perfis) {
                    NoPerfil $perfil.Conta {
                        param($raiz, $sid)
                        GravarReg $raiz 'Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop' 'NoChangingWallPaper' 'REG_DWORD' '1'
                        GravarReg $raiz 'Software\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoThemesTab' 'REG_DWORD' '1'
                    }
                    Write-Output ('Tema e plano de fundo bloqueados: ' + $perfil.Rotulo)
                }
                Resultado 'GPOs de tema e plano de fundo aplicadas.'
            """);

        /// <summary>
        /// Tópico 6.3: "Configurar Atualizações Automáticas = Desabilitado" (NoAutoUpdate = 1).
        /// No Windows 11 também desliga "Receber as atualizações mais recentes assim que estiverem disponíveis".
        /// </summary>
        public static string WindowsUpdate(VersaoPadronizacao versao) => Montar("""
                DefinirDword 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate' 1
            """ + (versao == VersaoPadronizacao.Windows11
                ? """

                DefinirDword 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings' 'IsContinuousInnovationOptedIn' 0
                Write-Output 'Windows 11: "Receber as atualizações mais recentes" desligado.'
            """
                : string.Empty) + """

                Resultado 'Atualizações automáticas desabilitadas.'
            """);

        /// <summary>Tópico 7: novo nome (vale depois de reiniciar). Nada é reiniciado automaticamente.</summary>
        public static string RenomearComputador() => Montar("""
                $novo = [string]$env:WINALLAPP_NOME
                $pendente = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName').ComputerName
                if ($env:COMPUTERNAME -ieq $novo) {
                    Resultado ('O computador já se chama ' + $novo + '.')
                } elseif ($pendente -ieq $novo) {
                    Resultado ('O nome ' + $novo + ' já está agendado; reinicie para aplicar.')
                } else {
                    Rename-Computer -NewName $novo -Force -WarningAction SilentlyContinue
                    Resultado ('Nome alterado de ' + $env:COMPUTERNAME + ' para ' + $novo + '; reinicie para aplicar.')
                }
            """);

        /// <summary>
        /// Tópico 9.6 modificado: impressora de rede por IP, com porta TCP/IP padrão e driver genérico do Windows.
        /// Liga o spooler e, se os cmdlets de impressão faltarem, habilita o recurso nativo de impressão.
        /// </summary>
        public static string Impressora() => Montar("""
                $ip = ([string]$env:WINALLAPP_IMPRESSORA_IP).Trim()
                $nome = ([string]$env:WINALLAPP_IMPRESSORA_NOME).Trim()

                $spooler = Get-Service -Name Spooler
                if ($spooler.StartType -eq 'Disabled') { Set-Service -Name Spooler -StartupType Automatic }
                if ($spooler.Status -ne 'Running') { Start-Service -Name Spooler }

                if (-not (Get-Command -Name Add-Printer -ErrorAction SilentlyContinue)) {
                    Write-Output 'Habilitando o recurso de impressão do Windows...'
                    Enable-WindowsOptionalFeature -Online -FeatureName 'Printing-Foundation-Features' -All -NoRestart | Out-Null
                    Import-Module PrintManagement
                }

                $porta = 'IP_' + $ip
                if (-not (Get-PrinterPort -Name $porta -ErrorAction SilentlyContinue)) {
                    Add-PrinterPort -Name $porta -PrinterHostAddress $ip
                    Write-Output ('Porta TCP/IP criada: ' + $porta)
                }

                $driver = $null
                foreach ($candidato in @('Microsoft PCL6 Class Driver', 'Microsoft PS Class Driver', 'Generic / Text Only')) {
                    if (Get-PrinterDriver -Name $candidato -ErrorAction SilentlyContinue) { $driver = $candidato; break }
                    try { Add-PrinterDriver -Name $candidato -ErrorAction Stop; $driver = $candidato; break } catch { }
                }
                if (-not $driver) { throw 'Nenhum driver genérico do Windows está disponível nesta máquina.' }

                if (Get-Printer -Name $nome -ErrorAction SilentlyContinue) {
                    Set-Printer -Name $nome -PortName $porta -DriverName $driver
                } else {
                    Add-Printer -Name $nome -PortName $porta -DriverName $driver
                }
                Resultado ('Impressora "' + $nome + '" em ' + $ip + ' com o driver ' + $driver + '.')
            """);

        /// <summary>
        /// Tópico 9.7: tira atalhos (.lnk, .url) e instaladores (.exe, .msi) das áreas de trabalho de todas as contas.
        /// Nunca apaga o próprio WinAllApp.exe (nem outra cópia WinAllApp*.exe).
        /// </summary>
        public static string LimparAreaDeTrabalho() => Montar("""
                $manter = [string]$env:WINALLAPP_EXECUTAVEL
                $pastas = New-Object System.Collections.Generic.List[string]
                $pastas.Add([Environment]::GetFolderPath('CommonDesktopDirectory'))
                $listaPerfis = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
                $raizes = @((Get-ItemProperty -LiteralPath $listaPerfis).Default)
                $raizes += Get-ChildItem -LiteralPath $listaPerfis | ForEach-Object { (Get-ItemProperty -LiteralPath $_.PSPath).ProfileImagePath }
                foreach ($raiz in $raizes) {
                    if ($raiz) { $pastas.Add((Join-Path ([Environment]::ExpandEnvironmentVariables($raiz)) 'Desktop')) }
                }

                $extensoes = @('.lnk', '.url', '.exe', '.msi')
                $removidos = 0
                foreach ($pasta in ($pastas | Select-Object -Unique)) {
                    if (-not (Test-Path -LiteralPath $pasta)) { continue }
                    Get-ChildItem -LiteralPath $pasta -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
                        if ($extensoes -notcontains $_.Extension.ToLowerInvariant()) { return }
                        if ($manter -and $_.FullName -ieq $manter) { return }
                        if ($_.Extension -ieq '.exe' -and $_.Name -like 'WinAllApp*') { return }
                        Remove-Item -LiteralPath $_.FullName -Force
                        $script:removidos++
                        Write-Output ('Removido: ' + $_.FullName)
                    }
                }
                Resultado ('Área de trabalho limpa: ' + $script:removidos + ' atalho(s) ou instalador(es) removido(s).')
            """);
    }
}
