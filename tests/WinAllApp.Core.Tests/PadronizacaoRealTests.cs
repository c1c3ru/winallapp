using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.Services.Padronizacao;
using Xunit;
using Xunit.Abstractions;

namespace WinAllApp.Core.Tests
{
    /// <summary>
    /// Roda os scripts de verdade no Windows PowerShell 5.1 do runner do CI (máquina descartável, como Administrador).
    /// Tudo o que é criado aqui (contas, perfis, impressora, arquivos) é removido no fim de cada teste.
    /// </summary>
    public class PadronizacaoRealTests
    {
        private readonly ITestOutputHelper _saida;

        public PadronizacaoRealTests(ITestOutputHelper saida) => _saida = saida;

        private static VersaoPadronizacao VersaoDaMaquina() => SistemaPadronizacao.Detectar(DetectorAmbiente.Detectar());

        private async Task<ResultadoPowerShell> RodarAsync(string script, IDictionary<string, string> variaveis = null)
        {
            var resultado = await new ExecutorPowerShell().ExecutarAsync(script,
                new Dictionary<string, string>(variaveis ?? new Dictionary<string, string>()), TimeSpan.FromMinutes(5), CancellationToken.None);
            _saida.WriteLine($"--- código {resultado.Codigo}");
            _saida.WriteLine(resultado.Saida);
            return resultado;
        }

        /// <summary>Falha mostrando a saída do PowerShell (os logs do Actions não são baixáveis; a anotação do teste é).</summary>
        private static void Sucesso(ResultadoPowerShell resultado, params int[] aceitos)
        {
            if (aceitos.Length == 0) aceitos = new[] { 0 };
            Assert.True(aceitos.Contains(resultado.Codigo), $"código {resultado.Codigo}: {resultado.Saida}");
        }

        private async Task<Dictionary<string, string>> LerAsync(string corpo)
        {
            var resultado = await RodarAsync(ScriptsPadronizacao.Montar(corpo));
            Sucesso(resultado);
            return resultado.Saida.Replace("\r", string.Empty).Split('\n')
                .Where(l => l.Contains('='))
                .Select(l => l.Split(new[] { '=' }, 2))
                .GroupBy(p => p[0].Trim())
                .ToDictionary(g => g.Key, g => g.Last()[1].Trim());
        }

        private static IEnumerable<KeyValuePair<string, string>> TodosOsScripts()
        {
            foreach (var versao in new[] { VersaoPadronizacao.Windows10, VersaoPadronizacao.Windows11 })
            {
                yield return new KeyValuePair<string, string>("ativacao-" + versao, ScriptsPadronizacao.Ativacao(versao));
                yield return new KeyValuePair<string, string>("update-" + versao, ScriptsPadronizacao.WindowsUpdate(versao));
            }
            yield return new KeyValuePair<string, string>("sistema", ScriptsPadronizacao.VerificarSistema());
            yield return new KeyValuePair<string, string>("contas", ScriptsPadronizacao.Contas());
            yield return new KeyValuePair<string, string>("uac", ScriptsPadronizacao.Uac());
            yield return new KeyValuePair<string, string>("papel", ScriptsPadronizacao.PapelDeParede());
            yield return new KeyValuePair<string, string>("gpo", ScriptsPadronizacao.GposPersonalizacao());
            yield return new KeyValuePair<string, string>("nome", ScriptsPadronizacao.RenomearComputador());
            yield return new KeyValuePair<string, string>("impressora", ScriptsPadronizacao.Impressora());
            yield return new KeyValuePair<string, string>("limpeza", ScriptsPadronizacao.LimparAreaDeTrabalho());
        }

        [WindowsFact]
        public async Task TodosOsScriptsSaoValidosNoWindowsPowerShell51()
        {
            var pasta = Dados.NovaPastaTemporaria();
            foreach (var script in TodosOsScripts())
                File.WriteAllText(Path.Combine(pasta, script.Key + ".ps1"), script.Value, new System.Text.UTF8Encoding(true));

            var valores = await LerAsync("""
                Write-Output ('VERSAO=' + $PSVersionTable.PSVersion.Major)
                $erros = 0
                Get-ChildItem -LiteralPath $env:WINALLAPP_PASTA -Filter *.ps1 | ForEach-Object {
                    $tokens = $null; $problemas = $null
                    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$problemas) | Out-Null
                    foreach ($p in $problemas) { Write-Output ('PROBLEMA ' + $_.Name + ' linha ' + $p.Extent.StartLineNumber + ': ' + $p.Message) }
                    $script:erros += $problemas.Count
                }
                Write-Output ('ERROS=' + $script:erros)
                """.Replace("$env:WINALLAPP_PASTA", "'" + pasta.Replace("'", "''") + "'"));

            Assert.Equal("5", valores["VERSAO"]);
            Assert.Equal("0", valores["ERROS"]);
        }

        [WindowsFact]
        public async Task VerificaOWindowsEOAdministrador()
        {
            var versao = VersaoDaMaquina();
            Assert.NotEqual(VersaoPadronizacao.NaoSuportado, versao);

            var resultado = await RodarAsync(ScriptsPadronizacao.VerificarSistema(),
                new Dictionary<string, string> { [ScriptsPadronizacao.VarWindows] = SistemaPadronizacao.Nome(versao) });

            Sucesso(resultado);
            Assert.Contains("executando como Administrador", resultado.Saida);
        }

        [WindowsFact]
        public async Task AtivacaoSemChaveSoConfereOStatus()
        {
            var resultado = await RodarAsync(ScriptsPadronizacao.Ativacao(VersaoDaMaquina()),
                new Dictionary<string, string> { [ScriptsPadronizacao.VarChave] = "" });

            // Ativado (0) ou "não ativado, informe a chave" (2); nunca erro de script.
            Sucesso(resultado, 0, 2);
            Assert.Contains("RESULTADO:", resultado.Saida);
        }

        [WindowsFact]
        public async Task ContasPapelDeParedeEGposDeVerdade()
        {
            var senha = "Wa!" + Guid.NewGuid().ToString("N").Substring(0, 12) + "9z";
            var pastaPapeis = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures), "WinAllApp-teste");
            Directory.CreateDirectory(pastaPapeis);
            var adm = Path.Combine(pastaPapeis, "PapelParede-Adm.jpg");
            var lab = Path.Combine(pastaPapeis, "PapelParede-Lab.jpg");
            File.WriteAllBytes(adm, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            File.WriteAllBytes(lab, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            var variaveis = new Dictionary<string, string>
            {
                [ScriptsPadronizacao.VarSenha] = senha,
                [ScriptsPadronizacao.VarContaUsuario] = "Aluno",
                [ScriptsPadronizacao.VarUsuarioAdmin] = "0",
                [ScriptsPadronizacao.VarPapelAdm] = adm,
                [ScriptsPadronizacao.VarPapelLab] = lab
            };

            try
            {
                var contas = await RodarAsync(ScriptsPadronizacao.Contas(), variaveis);
                Sucesso(contas);
                Assert.DoesNotContain(senha, contas.Saida);

                var grupos = await LerAsync("""
                    Add-Type -AssemblyName System.DirectoryServices.AccountManagement
                    $adm = (New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')).Translate([Security.Principal.NTAccount]).Value.Split('\')[-1]
                    $g = [ADSI]('WinNT://' + $env:COMPUTERNAME + '/' + $adm + ',group')
                    Write-Output ('INFORMATICA_ADMIN=' + $g.Invoke('IsMember', ('WinNT://' + $env:COMPUTERNAME + '/Informatica')))
                    Write-Output ('ALUNO_ADMIN=' + $g.Invoke('IsMember', ('WinNT://' + $env:COMPUTERNAME + '/Aluno')))
                    Write-Output ('ALUNO_EXISTE=' + [bool](Get-CimInstance -ClassName Win32_UserAccount -Filter "LocalAccount=True AND Name='Aluno'"))
                    $maquina = New-Object System.DirectoryServices.AccountManagement.PrincipalContext('Machine')
                    Write-Output ('SENHA_OK=' + $maquina.ValidateCredentials('Informatica', $env:WINALLAPP_SENHA_TESTE))
                    """.Replace("$env:WINALLAPP_SENHA_TESTE", "'" + senha + "'"));
                Assert.Equal("True", grupos["INFORMATICA_ADMIN"]);
                Assert.Equal("False", grupos["ALUNO_ADMIN"]);
                Assert.Equal("True", grupos["ALUNO_EXISTE"]);
                Assert.Equal("True", grupos["SENHA_OK"]);

                // Rodar de novo com as contas já existentes (técnico repetindo a padronização).
                var denovo = await RodarAsync(ScriptsPadronizacao.Contas(), variaveis);
                Sucesso(denovo, 0, 2);
                Assert.Contains("Conta Aluno ajustada", denovo.Saida);

                var papel = await RodarAsync(ScriptsPadronizacao.PapelDeParede(), variaveis);
                Sucesso(papel);
                var gpo = await RodarAsync(ScriptsPadronizacao.GposPersonalizacao(), variaveis);
                Sucesso(gpo);

                var registro = await LerAsync("""
                    $lista = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
                    function Ler([string]$rotulo, [string]$pasta) {
                        $apelido = 'HKU\WinAllApp_Teste_' + $rotulo
                        Invocar 'reg.exe' @('load', $apelido, (Join-Path $pasta 'NTUSER.DAT')) | Out-Null
                        try {
                            $raiz = 'Registry::HKEY_USERS\WinAllApp_Teste_' + $rotulo
                            $ad = Get-ItemProperty -LiteralPath ($raiz + '\Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop')
                            $ex = Get-ItemProperty -LiteralPath ($raiz + '\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer')
                            $si = Get-ItemProperty -LiteralPath ($raiz + '\Software\Microsoft\Windows\CurrentVersion\Policies\System')
                            Write-Output ($rotulo + '_WALLPAPER_BLOQUEADO=' + $ad.NoChangingWallPaper)
                            Write-Output ($rotulo + '_TEMA_BLOQUEADO=' + $ex.NoThemesTab)
                            Write-Output ($rotulo + '_PAPEL=' + $si.Wallpaper)
                            Remove-Variable ad, ex, si
                        } finally {
                            [GC]::Collect(); [GC]::WaitForPendingFinalizers()
                            Invocar 'reg.exe' @('unload', $apelido) | Out-Null
                        }
                    }
                    $sid = (New-Object Security.Principal.NTAccount($env:COMPUTERNAME, 'Aluno')).Translate([Security.Principal.SecurityIdentifier]).Value
                    Ler 'ALUNO' (Get-ItemProperty -LiteralPath (Join-Path $lista $sid)).ProfileImagePath
                    Ler 'DEFAULT' ([Environment]::ExpandEnvironmentVariables((Get-ItemProperty -LiteralPath $lista).Default))
                    """);
                Assert.Equal("1", registro["ALUNO_WALLPAPER_BLOQUEADO"]);
                Assert.Equal("1", registro["ALUNO_TEMA_BLOQUEADO"]);
                Assert.Equal(lab, registro["ALUNO_PAPEL"]);
                Assert.Equal("1", registro["DEFAULT_WALLPAPER_BLOQUEADO"]);
                Assert.Equal("1", registro["DEFAULT_TEMA_BLOQUEADO"]);
                Assert.Equal(adm, registro["DEFAULT_PAPEL"]);
            }
            finally
            {
                await RodarAsync(ScriptsPadronizacao.Montar("""
                    foreach ($conta in @('Aluno', 'Informatica')) {
                        if (-not (Get-CimInstance -ClassName Win32_UserAccount -Filter ("LocalAccount=True AND Name='" + $conta + "'"))) { continue }
                        $sid = (New-Object Security.Principal.NTAccount($env:COMPUTERNAME, $conta)).Translate([Security.Principal.SecurityIdentifier]).Value
                        Get-CimInstance -ClassName Win32_UserProfile | Where-Object { $_.SID -eq $sid } | Remove-CimInstance
                        Invocar 'net.exe' @('user', $conta, '/delete') | Out-Null
                    }
                    $lista = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList'
                    $padrao = [Environment]::ExpandEnvironmentVariables((Get-ItemProperty -LiteralPath $lista).Default)
                    Invocar 'reg.exe' @('load', 'HKU\WinAllApp_Limpeza', (Join-Path $padrao 'NTUSER.DAT')) | Out-Null
                    foreach ($chave in @('ActiveDesktop', 'Explorer', 'System')) {
                        & reg.exe delete ('HKU\WinAllApp_Limpeza\Software\Microsoft\Windows\CurrentVersion\Policies\' + $chave) /f 2>&1 | Out-Null
                    }
                    [GC]::Collect()
                    Invocar 'reg.exe' @('unload', 'HKU\WinAllApp_Limpeza') | Out-Null
                    """));
                Directory.Delete(pastaPapeis, recursive: true);
            }
        }

        [WindowsFact]
        public async Task UacEWindowsUpdateDeVerdade()
        {
            var versao = VersaoDaMaquina();
            var antes = await LerAsync("""
                $s = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
                Write-Output ('CONSENT=' + $s.ConsentPromptBehaviorAdmin)
                Write-Output ('SECURE=' + $s.PromptOnSecureDesktop)
                """);
            try
            {
                Sucesso(await RodarAsync(ScriptsPadronizacao.Uac()));
                Sucesso(await RodarAsync(ScriptsPadronizacao.WindowsUpdate(versao)));

                var depois = await LerAsync("""
                    $s = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
                    $au = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
                    Write-Output ('CONSENT=' + $s.ConsentPromptBehaviorAdmin)
                    Write-Output ('SECURE=' + $s.PromptOnSecureDesktop)
                    Write-Output ('LUA=' + $s.EnableLUA)
                    Write-Output ('NOAUTOUPDATE=' + $au.NoAutoUpdate)
                    """);
                Assert.Equal("0", depois["CONSENT"]);
                Assert.Equal("0", depois["SECURE"]);
                Assert.Equal("1", depois["LUA"]);
                Assert.Equal("1", depois["NOAUTOUPDATE"]);
            }
            finally
            {
                await RodarAsync(ScriptsPadronizacao.Montar(
                    $"DefinirDword 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' 'ConsentPromptBehaviorAdmin' {Numero(antes, "CONSENT", 5)}\n" +
                    $"DefinirDword 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' 'PromptOnSecureDesktop' {Numero(antes, "SECURE", 1)}\n" +
                    "Remove-ItemProperty -LiteralPath 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU' -Name NoAutoUpdate -ErrorAction SilentlyContinue"));
            }
        }

        private static int Numero(Dictionary<string, string> valores, string chave, int padrao) =>
            valores.TryGetValue(chave, out var texto) && int.TryParse(texto, out var n) ? n : padrao;

        [WindowsFact]
        public async Task ImpressoraPorIpComDriverGenericoDeVerdade()
        {
            var variaveis = new Dictionary<string, string>
            {
                [ScriptsPadronizacao.VarImpressoraIp] = "127.0.0.1",
                [ScriptsPadronizacao.VarImpressoraNome] = "WinAllApp Teste"
            };
            try
            {
                var resultado = await RodarAsync(ScriptsPadronizacao.Impressora(), variaveis);
                Sucesso(resultado);

                var impressora = await LerAsync("""
                    $p = Get-Printer -Name 'WinAllApp Teste'
                    $porta = Get-PrinterPort -Name $p.PortName
                    Write-Output ('PORTA=' + $p.PortName)
                    Write-Output ('HOST=' + $porta.PrinterHostAddress)
                    Write-Output ('DRIVER=' + $p.DriverName)
                    """);
                Assert.Equal("IP_127.0.0.1", impressora["PORTA"]);
                Assert.Equal("127.0.0.1", impressora["HOST"]);
                Assert.Contains(impressora["DRIVER"], ScriptsPadronizacao.DriversGenericos);

                // Repetir não duplica nada.
                Sucesso(await RodarAsync(ScriptsPadronizacao.Impressora(), variaveis));
            }
            finally
            {
                await RodarAsync(ScriptsPadronizacao.Montar("""
                    Remove-Printer -Name 'WinAllApp Teste' -ErrorAction SilentlyContinue
                    Start-Sleep -Seconds 2
                    Remove-PrinterPort -Name 'IP_127.0.0.1' -ErrorAction SilentlyContinue
                    """));
            }
        }

        [WindowsFact]
        public async Task LimpezaTiraAtalhosEInstaladoresEMantemOResto()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            var atalho = Path.Combine(desktop, "winallapp-teste-atalho.lnk");
            var instalador = Path.Combine(desktop, "instalador-teste.msi");
            var nota = Path.Combine(desktop, "anotacoes-teste.txt");
            var app = Path.Combine(desktop, "WinAllApp-copia-teste.exe");
            foreach (var arquivo in new[] { atalho, instalador, nota, app }) File.WriteAllText(arquivo, "teste");
            try
            {
                var resultado = await RodarAsync(ScriptsPadronizacao.LimparAreaDeTrabalho(),
                    new Dictionary<string, string> { [ScriptsPadronizacao.VarExecutavel] = app });

                Sucesso(resultado);
                Assert.False(File.Exists(atalho));
                Assert.False(File.Exists(instalador));
                Assert.True(File.Exists(nota));
                Assert.True(File.Exists(app));
            }
            finally
            {
                foreach (var arquivo in new[] { atalho, instalador, nota, app })
                    if (File.Exists(arquivo)) File.Delete(arquivo);
            }
        }

        [WindowsFact]
        public async Task RenomearParaONomeAtualNaoMudaNada()
        {
            var resultado = await RodarAsync(ScriptsPadronizacao.RenomearComputador(),
                new Dictionary<string, string> { [ScriptsPadronizacao.VarNome] = Environment.MachineName });

            Sucesso(resultado);
            Assert.Contains("já se chama", resultado.Saida);
        }
    }
}
