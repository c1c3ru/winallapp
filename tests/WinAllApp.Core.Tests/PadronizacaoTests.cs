using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.Services.Padronizacao;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>PowerShell falso: guarda cada script com as variáveis e devolve o código escolhido por etapa.</summary>
    public sealed class PowerShellFalso : IExecutorPowerShell
    {
        private readonly Func<string, ResultadoPowerShell> _resposta;

        public PowerShellFalso(Func<string, ResultadoPowerShell> resposta = null) =>
            _resposta = resposta ?? (_ => new ResultadoPowerShell(0, "RESULTADO: ok"));

        public ConcurrentQueue<KeyValuePair<string, Dictionary<string, string>>> Execucoes { get; } =
            new ConcurrentQueue<KeyValuePair<string, Dictionary<string, string>>>();

        public Task<ResultadoPowerShell> ExecutarAsync(string script, IReadOnlyDictionary<string, string> variaveis,
            TimeSpan timeout, CancellationToken cancelamento)
        {
            cancelamento.ThrowIfCancellationRequested();
            Execucoes.Enqueue(new KeyValuePair<string, Dictionary<string, string>>(script, variaveis.ToDictionary(p => p.Key, p => p.Value)));
            return Task.FromResult(_resposta(script));
        }
    }

    public sealed class RedeFalsa : IDiagnosticoRede
    {
        public bool Servidor { get; set; } = true;
        public bool Pasta { get; set; } = true;
        public bool Internet { get; set; } = true;
        public Task<bool> RespondeAsync(string host, TimeSpan timeout, CancellationToken cancelamento) => Task.FromResult(Servidor);
        public Task<bool> PastaAcessivelAsync(string pasta, TimeSpan timeout, CancellationToken cancelamento) => Task.FromResult(Pasta);
        public Task<bool> InternetAsync(TimeSpan timeout, CancellationToken cancelamento) => Task.FromResult(Internet);
    }

    public static class ApoioPadronizacao
    {
        /// <summary>Pasta de rede fictícia com dois anos e as imagens Adm e Lab com os nomes do servidor real.</summary>
        public static string CriarPastaPapeis(bool comLab = true)
        {
            var raiz = Dados.NovaPastaTemporaria();
            Directory.CreateDirectory(Path.Combine(raiz, "2023"));
            File.WriteAllText(Path.Combine(raiz, "2023", "PapelParede – Adm.jpg"), "antigo");
            var ano = Path.Combine(raiz, "2025");
            Directory.CreateDirectory(ano);
            File.WriteAllText(Path.Combine(ano, "PapelParede – Adm.jpg"), "adm-2025");
            if (comLab) File.WriteAllText(Path.Combine(ano, "PapelParede - Lab.jpg"), "lab-2025");
            File.WriteAllText(Path.Combine(ano, "leia-me.txt"), "não é imagem");
            return raiz;
        }

        public static OpcoesPadronizacao Opcoes(PerfilDoUsuario perfil = PerfilDoUsuario.Aluno) => new OpcoesPadronizacao
        {
            Bloco = "BL2",
            Local = "LIA",
            Numero = "07",
            SenhaInformatica = "S3nh@-Secreta",
            Perfil = perfil,
            PastaPapeisDeParede = CriarPastaPapeis(),
            PastaLocalPapeis = Path.Combine(Dados.NovaPastaTemporaria(), "local")
        };

        public static AmbienteSistema Windows(string versao) => AmbienteSistema.Simular(versao);
    }

    public class PadronizacaoTests
    {
        private static IReadOnlyList<EtapaPadronizacao> Planejar(OpcoesPadronizacao opcoes, VersaoPadronizacao versao) =>
            new MotorPadronizacao(new PowerShellFalso(), new RedeFalsa()).Planejar(opcoes, versao);

        private static EtapaPadronizacao Etapa(IReadOnlyList<EtapaPadronizacao> plano, string id) => plano.Single(e => e.Id == id);

        // ---------- 1) Compatibilidade Windows 10 / 11 ----------

        [Theory]
        [InlineData("10", VersaoPadronizacao.Windows10)]
        [InlineData("11", VersaoPadronizacao.Windows11)]
        [InlineData("8.1", VersaoPadronizacao.NaoSuportado)]
        [InlineData("7", VersaoPadronizacao.NaoSuportado)]
        public void DetectaWindows10Ou11PeloBuild(string windows, VersaoPadronizacao esperado)
        {
            Assert.Equal(esperado, SistemaPadronizacao.Detectar(ApoioPadronizacao.Windows(windows)));
        }

        [Fact]
        public void Build22000EmDianteEhWindows11()
        {
            Assert.Equal(VersaoPadronizacao.Windows10, SistemaPadronizacao.Detectar(new AmbienteSistema { BuildWindows = 19045 }));
            Assert.Equal(VersaoPadronizacao.Windows11, SistemaPadronizacao.Detectar(new AmbienteSistema { BuildWindows = 22000 }));
            Assert.Equal(VersaoPadronizacao.Windows11, SistemaPadronizacao.Detectar(new AmbienteSistema { BuildWindows = 26100 }));
        }

        [Fact]
        public void ForaDoWindows10E11NaoPlaneja()
        {
            Assert.Throws<NotSupportedException>(() => Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.NaoSuportado));
        }

        [Fact]
        public void Windows10AtivaPeloSlmgrEWindows11PeloCim()
        {
            var dez = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10), "ativacao").Script;
            var onze = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows11), "ativacao").Script;

            Assert.Contains("slmgr.vbs", dez);
            Assert.Contains("/ipk", dez);
            Assert.Contains("/ato", dez);
            Assert.DoesNotContain("InstallProductKey", dez);

            Assert.Contains("InstallProductKey", onze);
            Assert.Contains("RefreshLicenseStatus", onze);
            Assert.DoesNotContain("slmgr", onze);
        }

        [Fact]
        public void Windows11TambemDesligaAtualizacoesAntecipadas()
        {
            var dez = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10), "update").Script;
            var onze = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows11), "update").Script;

            Assert.Contains(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate' 1", dez);
            Assert.Contains("NoAutoUpdate", onze);
            Assert.DoesNotContain("IsContinuousInnovationOptedIn", dez);
            Assert.Contains("'IsContinuousInnovationOptedIn' 0", onze);
        }

        [Fact]
        public void VerificacaoDoSistemaUsaCimEExigeAdministrador()
        {
            var sistema = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows11), "sistema");
            Assert.True(sistema.Essencial);
            Assert.Equal("Windows 11", sistema.Variaveis[ScriptsPadronizacao.VarWindows]);
            Assert.Contains("Get-CimInstance -ClassName Win32_OperatingSystem", sistema.Script);
            Assert.Contains("WindowsBuiltInRole]::Administrator", sistema.Script);
            Assert.Contains("-ge 22000", sistema.Script);
        }

        [Fact]
        public void NenhumScriptUsaWmicNemGpedit()
        {
            foreach (var versao in new[] { VersaoPadronizacao.Windows10, VersaoPadronizacao.Windows11 })
            {
                var opcoes = ApoioPadronizacao.Opcoes();
                opcoes.AdicionarImpressora = true;
                opcoes.IpImpressora = "10.50.12.34";
                foreach (var etapa in Planejar(opcoes, versao).Where(e => e.Script != null))
                {
                    Assert.DoesNotContain("wmic", etapa.Script, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("gpedit", etapa.Script, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact]
        public void PlanoSegueAOrdemDoManual()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            opcoes.AdicionarImpressora = true;
            opcoes.IpImpressora = "10.50.12.34";
            var ids = Planejar(opcoes, VersaoPadronizacao.Windows10).Select(e => e.Id).ToArray();
            Assert.Equal(new[] { "sistema", "rede", "ativacao", "contas", "uac", "papel", "gpo", "update", "nome", "impressora", "limpeza" }, ids);
        }

        // ---------- 2) Contas e segurança ----------

        [Fact]
        public void ContasUsamNetUserENetLocalgroupComGruposPeloSid()
        {
            var contas = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10), "contas");
            Assert.Contains("'net.exe' @('user', $conta, '/add'", contas.Script);
            Assert.Contains("'net.exe' @('localgroup', $administradores, $conta, '/add')", contas.Script);
            Assert.Contains("'net.exe' @('localgroup', $administradores, $conta, '/delete')", contas.Script);
            Assert.Contains("S-1-5-32-544", contas.Script);
            Assert.Contains("S-1-5-32-545", contas.Script);
            Assert.Contains("GarantirConta 'Informatica' $true $env:WINALLAPP_SENHA", contas.Script);
        }

        [Fact]
        public void SenhaVaiSoPorVariavelDaEtapaDeContas()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            var plano = Planejar(opcoes, VersaoPadronizacao.Windows11);
            var contas = Etapa(plano, "contas");

            Assert.Equal("S3nh@-Secreta", contas.Variaveis[ScriptsPadronizacao.VarSenha]);
            foreach (var etapa in plano)
            {
                if (etapa.Script != null) Assert.DoesNotContain("S3nh@-Secreta", etapa.Script);
                if (etapa.Id != "contas") Assert.DoesNotContain(ScriptsPadronizacao.VarSenha, etapa.Variaveis.Keys);
            }
            Assert.DoesNotContain("S3nh@-Secreta", ExecutorPowerShell.Argumentos(contas.Script));
        }

        [Theory]
        [InlineData(PerfilDoUsuario.Aluno, "Aluno", "0")]
        [InlineData(PerfilDoUsuario.Bolsista, "Bolsista", "1")]
        public void AlunoEhPadraoEBolsistaEhAdministrador(PerfilDoUsuario perfil, string conta, string admin)
        {
            var contas = Etapa(Planejar(ApoioPadronizacao.Opcoes(perfil), VersaoPadronizacao.Windows10), "contas");
            Assert.Equal(conta, contas.Variaveis[ScriptsPadronizacao.VarContaUsuario]);
            Assert.Equal(admin, contas.Variaveis[ScriptsPadronizacao.VarUsuarioAdmin]);
            Assert.Contains("GarantirConta $env:WINALLAPP_CONTA_USUARIO ($env:WINALLAPP_USUARIO_ADMIN -eq '1') ''", contas.Script);
        }

        [Fact]
        public void UacEmNuncaNotificarSemDesligarOUac()
        {
            var uac = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10), "uac").Script;
            Assert.Contains(@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", uac);
            Assert.Contains("'ConsentPromptBehaviorAdmin' 0", uac);
            Assert.Contains("'PromptOnSecureDesktop' 0", uac);
            Assert.Contains("'EnableLUA' 1", uac);
        }

        [Fact]
        public void SemSenhaDaInformaticaNaoRoda()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            opcoes.SenhaInformatica = "";
            Assert.Contains(opcoes.Validar(), e => e.Contains("senha da conta Informatica"));
            Assert.Throws<ArgumentException>(() => Planejar(opcoes, VersaoPadronizacao.Windows10));
        }

        // ---------- 3) GPOs e personalização ----------

        [Fact]
        public void GposDeTemaEPlanoDeFundoNoRegistroDeCadaConta()
        {
            var gpo = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10), "gpo").Script;
            Assert.Contains(@"'Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop' 'NoChangingWallPaper' 'REG_DWORD' '1'", gpo);
            Assert.Contains(@"'Software\Microsoft\Windows\CurrentVersion\Policies\Explorer' 'NoThemesTab' 'REG_DWORD' '1'", gpo);
            Assert.Contains("'reg.exe' @('load'", gpo);
            Assert.Contains("'reg.exe' @('unload'", gpo);
            Assert.Contains("CreateProfile", gpo);
            Assert.Contains("Conta = ''", gpo); // perfil Default, para contas criadas depois
        }

        [Fact]
        public void PapelDeParedeAplicadoComoPoliticaEComoPreferencia()
        {
            var papel = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows11), "papel");
            Assert.Contains(@"'Software\Microsoft\Windows\CurrentVersion\Policies\System' 'Wallpaper' 'REG_SZ' $papel", papel.Script);
            Assert.Contains(@"'Control Panel\Desktop' 'Wallpaper' 'REG_SZ' $papel", papel.Script);
            Assert.Contains("if ($contaUsuario -eq 'Aluno') { $env:WINALLAPP_PAPEL_LAB } else { $env:WINALLAPP_PAPEL_ADM }", papel.Script);
            Assert.Contains("SystemParametersInfo(0x14", papel.Script);
        }

        [Fact]
        public void PastaDoAnoMaisRecente()
        {
            var escolhida = PapeisDeParede.PastaDoAnoMaisRecente(new[]
            {
                @"\\srv\p\2023", @"\\srv\p\2025", @"\\srv\p\2024 - antigo", @"\\srv\p\Fotos", @"\\srv\p\20250"
            });
            Assert.Equal(@"\\srv\p\2025", escolhida);
            Assert.Null(PapeisDeParede.PastaDoAnoMaisRecente(new[] { "Fotos", "Antigos" }));
        }

        [Theory]
        [InlineData("PapelParede – Adm.jpg", "Adm", true)]
        [InlineData("PapelParede - Lab.JPG", "Lab", true)]
        [InlineData("Papel Parede—adm.png", "Adm", true)]
        [InlineData("PapelParede - Lab.jpg", "Adm", false)]
        [InlineData("PapelParede – Adm.txt", "Adm", false)]
        [InlineData("PapelParede – Adm antigo.jpg", "Adm", false)]
        public void LocalizaAImagemPeloTipoIgnorandoTracosEspacos(string arquivo, string tipo, bool acha)
        {
            var achado = PapeisDeParede.Localizar(new[] { Path.Combine("pasta", arquivo) }, tipo);
            Assert.Equal(acha, achado != null);
        }

        [Fact]
        public void CopiaAsImagensDoAnoMaisRecenteParaODiscoLocal()
        {
            var rede = ApoioPadronizacao.CriarPastaPapeis();
            var local = Path.Combine(Dados.NovaPastaTemporaria(), "local");

            var papeis = PapeisDeParede.Copiar(rede, local, precisaLab: true);

            Assert.Equal(Path.Combine(rede, "2025"), papeis.PastaDoAno);
            Assert.Equal(Path.Combine(local, "PapelParede-Adm.jpg"), papeis.Adm);
            Assert.Equal(Path.Combine(local, "PapelParede-Lab.jpg"), papeis.Lab);
            Assert.Equal("adm-2025", File.ReadAllText(papeis.Adm));
            Assert.Equal("lab-2025", File.ReadAllText(papeis.Lab));
        }

        [Fact]
        public void BolsistaNaoPrecisaDaImagemLabMasAlunoPrecisa()
        {
            var rede = ApoioPadronizacao.CriarPastaPapeis(comLab: false);
            var local = Path.Combine(Dados.NovaPastaTemporaria(), "local");

            var bolsista = PapeisDeParede.Copiar(rede, local, precisaLab: false);
            Assert.NotNull(bolsista.Adm);
            Assert.Null(bolsista.Lab);
            Assert.Throws<FileNotFoundException>(() => PapeisDeParede.Copiar(rede, local, precisaLab: true));
        }

        [Fact]
        public void PastaDePapeisSemAnoOuInexistenteFalhaComMensagemClara()
        {
            var vazia = Dados.NovaPastaTemporaria();
            var erro = Assert.Throws<DirectoryNotFoundException>(() => PapeisDeParede.Copiar(vazia, Path.Combine(vazia, "l"), true));
            Assert.Contains("Nenhuma pasta com ano", erro.Message);
            Assert.Throws<DirectoryNotFoundException>(() => PapeisDeParede.Copiar(Path.Combine(vazia, "nao-existe"), vazia, true));
        }

        // ---------- 4) Sistema e impressora via IP ----------

        [Theory]
        [InlineData("BL1", "LAB", "1", "BL1-LAB-01")]
        [InlineData("bl2", "lia", "07", "BL2-LIA-07")]
        [InlineData("ADM", "Protocó", "12", "ADM-PROTOCO-12")]
        [InlineData("BL3", " lab info ", "3", "BL3-LABINFO-03")]
        public void MontaONomeNoPadraoBlocoLocalXX(string bloco, string local, string numero, string esperado)
        {
            var nome = OpcoesPadronizacao.MontarNome(bloco, local, numero);
            Assert.Equal(esperado, nome);
            Assert.True(OpcoesPadronizacao.NomeComputadorValido(nome, out var motivo), motivo);
        }

        [Theory]
        [InlineData("BL4-LAB-01", "bloco")]
        [InlineData("BL1-LABORATOR-01", "8 letras")]
        [InlineData("BL1--01", "local")]
        [InlineData("BL1-LAB-100", "2 dígitos")]
        [InlineData("BL1-LAB-00", "01")]
        [InlineData("BL1-LA_B-01", "letras e números")]
        [InlineData("BL1LAB01", "BLOCO-LOCAL-XX")]
        public void RecusaNomeForaDoPadrao(string nome, string trechoDoMotivo)
        {
            Assert.False(OpcoesPadronizacao.NomeComputadorValido(nome, out var motivo));
            Assert.Contains(trechoDoMotivo, motivo);
        }

        [Fact]
        public void NomeValidoCabeNoLimiteNetBios()
        {
            Assert.True(OpcoesPadronizacao.NomeComputadorValido("BL1-ABCDEFGH-99", out _));
            Assert.True("BL1-ABCDEFGH-99".Length <= 15);
        }

        [Fact]
        public void RenomeiaComRenameComputerSemReiniciar()
        {
            var nome = Etapa(Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10), "nome");
            Assert.Equal("BL2-LIA-07", nome.Variaveis[ScriptsPadronizacao.VarNome]);
            Assert.Contains("Rename-Computer -NewName $novo -Force", nome.Script);
            Assert.DoesNotContain("-Restart", nome.Script);
            Assert.DoesNotContain("Restart-Computer", nome.Script);
        }

        [Theory]
        [InlineData("10.50.12.34", true)]
        [InlineData("192.168.0.10", true)]
        [InlineData("10.50.12", false)]
        [InlineData("10.50.12.256", false)]
        [InlineData("0.1.2.3", false)]
        [InlineData("255.255.255.255", false)]
        [InlineData("impressora.local", false)]
        [InlineData("", false)]
        public void ValidaOIpDaImpressora(string ip, bool valido)
        {
            Assert.Equal(valido, OpcoesPadronizacao.IpValido(ip));
        }

        [Fact]
        public void ImpressoraSoEntraSeMarcadaEComIpValido()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            Assert.DoesNotContain(Planejar(opcoes, VersaoPadronizacao.Windows10), e => e.Id == "impressora");

            opcoes.AdicionarImpressora = true;
            opcoes.IpImpressora = "10.50.999.1";
            Assert.Contains(opcoes.Validar(), e => e.StartsWith("Impressora: informe um IP válido"));

            opcoes.IpImpressora = " 10.50.12.34 ";
            var impressora = Etapa(Planejar(opcoes, VersaoPadronizacao.Windows11), "impressora");
            Assert.Equal("10.50.12.34", impressora.Variaveis[ScriptsPadronizacao.VarImpressoraIp]);
            Assert.Equal("Impressora 10.50.12.34", impressora.Variaveis[ScriptsPadronizacao.VarImpressoraNome]);
            Assert.Contains("Add-PrinterPort -Name $porta -PrinterHostAddress $ip", impressora.Script);
            Assert.Contains("Add-Printer -Name $nome -PortName $porta -DriverName $driver", impressora.Script);
            Assert.Contains("Microsoft PCL6 Class Driver", impressora.Script);
            Assert.Contains("Generic / Text Only", impressora.Script);
        }

        [Fact]
        public void ImpressoraNaoBuscaDriverNoServidor()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            opcoes.AdicionarImpressora = true;
            opcoes.IpImpressora = "10.50.12.34";
            var script = Etapa(Planejar(opcoes, VersaoPadronizacao.Windows10), "impressora").Script;
            Assert.DoesNotContain(@"\\", script);
            Assert.DoesNotContain("10.50.11.2", script);
            Assert.DoesNotContain("pnputil", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(".inf", script, StringComparison.OrdinalIgnoreCase);
        }

        // ---------- 5) Limpeza e recursos nativos ----------

        [Fact]
        public void LimpezaTiraAtalhosEInstaladoresMasNuncaOProprioApp()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            opcoes.ExecutavelEmUso = @"C:\Users\Tecnico\Desktop\WinAllApp.exe";
            var limpeza = Etapa(Planejar(opcoes, VersaoPadronizacao.Windows10), "limpeza");
            Assert.Equal(@"C:\Users\Tecnico\Desktop\WinAllApp.exe", limpeza.Variaveis[ScriptsPadronizacao.VarExecutavel]);
            Assert.Contains("@('.lnk', '.url', '.exe', '.msi')", limpeza.Script);
            Assert.Contains("CommonDesktopDirectory", limpeza.Script);
            Assert.Contains("$_.FullName -ieq $manter", limpeza.Script);
            Assert.Contains("-like 'WinAllApp*'", limpeza.Script);

            opcoes.LimparAreaDeTrabalho = false;
            Assert.DoesNotContain(Planejar(opcoes, VersaoPadronizacao.Windows10), e => e.Id == "limpeza");
        }

        [Fact]
        public void ImpressoraHabilitaRecursoNativoSeFaltarECuidaDoSpooler()
        {
            var script = ScriptsPadronizacao.Impressora();
            Assert.Contains("Enable-WindowsOptionalFeature -Online -FeatureName 'Printing-Foundation-Features'", script);
            Assert.Contains("Start-Service -Name Spooler", script);
        }

        /// <summary>Exclusão estrita (tópicos 9.1 a 9.5 e 10), sem marcas de impressora e sem desligar proteções.</summary>
        [Fact]
        public void NadaDeProgramasExcluidosMarcasOuDesligarProtecoes()
        {
            var proibidos = new[]
            {
                "Chrome", "WPS", "K-Lite", "KLite", "OCS", "VNC", "chkdsk", "defrag", "Auslogics", "mdsched", "FFT",
                "%temp%", "$env:TEMP", "Brother", "Epson", "Canon", "Lexmark", "Xerox", "Ricoh", "Kyocera", "Samsung",
                "Re-Loader", "Reloader", "KMSpico", "Set-MpPreference", "DisableRealtimeMonitoring", "WinDefend"
            };
            var textos = new List<KeyValuePair<string, string>>();
            foreach (var versao in new[] { VersaoPadronizacao.Windows10, VersaoPadronizacao.Windows11 })
            {
                var opcoes = ApoioPadronizacao.Opcoes();
                opcoes.AdicionarImpressora = true;
                opcoes.IpImpressora = "10.50.12.34";
                foreach (var etapa in Planejar(opcoes, versao).Where(e => e.Script != null))
                    textos.Add(new KeyValuePair<string, string>($"script {etapa.Id} ({versao})", etapa.Script));
            }

            var raiz = RaizDoRepositorio();
            var fontes = Directory.GetFiles(Path.Combine(raiz, "src", "WinAllApp.Core", "Services", "Padronizacao"), "*.cs")
                .Concat(new[]
                {
                    Path.Combine(raiz, "src", "WinAllApp.Core", "ViewModels", "PadronizacaoViewModel.cs"),
                    Path.Combine(raiz, "src", "WinAllApp", "Views", "PadronizacaoWindow.xaml")
                });
            foreach (var fonte in fontes)
                textos.Add(new KeyValuePair<string, string>(Path.GetFileName(fonte), File.ReadAllText(fonte)));

            foreach (var texto in textos)
            {
                foreach (var termo in proibidos)
                    Assert.False(texto.Value.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0, $"\"{termo}\" apareceu em {texto.Key}");
                Assert.False(Regex.IsMatch(texto.Value, @"\bHP\b"), $"\"HP\" apareceu em {texto.Key}");
            }
        }

        private static string RaizDoRepositorio()
        {
            var pasta = new DirectoryInfo(AppContext.BaseDirectory);
            while (pasta != null && !File.Exists(Path.Combine(pasta.FullName, "WinAllApp.sln"))) pasta = pasta.Parent;
            Assert.NotNull(pasta);
            return pasta.FullName;
        }

        // ---------- Motor ----------

        [Fact]
        public void EncodedCommandLevaOScriptInteiroEmUtf16()
        {
            var script = ScriptsPadronizacao.Montar("Resultado 'ação: ç'");
            var argumentos = ExecutorPowerShell.Argumentos(script);
            Assert.StartsWith("-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ", argumentos);
            var base64 = argumentos.Split(' ').Last();
            Assert.Equal(script, Encoding.Unicode.GetString(Convert.FromBase64String(base64)));
        }

        [Fact]
        public void MontarTrataErroEAvisoComCodigosDeSaida()
        {
            var script = ScriptsPadronizacao.Montar("Resultado 'x'");
            Assert.Contains("$ErrorActionPreference = 'Stop'", script);
            Assert.Contains("Write-Output ('ERRO: ' + $_.Exception.Message)", script);
            Assert.Contains("if ($script:TeveAviso) { exit 2 }", script);
            Assert.EndsWith("exit 0\n", script);
        }

        [Theory]
        [InlineData(0, "linha\nRESULTADO: tudo certo\n", StatusEtapa.Concluida, "tudo certo")]
        [InlineData(2, "AVISO: sem chave\nRESULTADO: não ativado\n", StatusEtapa.ConcluidaComAviso, "sem chave não ativado")]
        [InlineData(1, "Conta criada\nERRO: acesso negado\n", StatusEtapa.Falhou, "acesso negado")]
        [InlineData(1, "", StatusEtapa.Falhou, "O PowerShell terminou com o código 1.")]
        public void InterpretaASaidaDoScript(int codigo, string saida, StatusEtapa status, string resumo)
        {
            var resultado = MotorPadronizacao.Interpretar(new ResultadoPowerShell(codigo, saida.Replace("\n", Environment.NewLine)));
            Assert.Equal(status, resultado.Status);
            Assert.Equal(resumo, resultado.Resumo);
        }

        [Fact]
        public async Task ExecutaTudoEmOrdemECopiaOPapelDeParedeAntesDoScript()
        {
            var ps = new PowerShellFalso();
            var motor = new MotorPadronizacao(ps, new RedeFalsa());
            var opcoes = ApoioPadronizacao.Opcoes();
            var plano = motor.Planejar(opcoes, VersaoPadronizacao.Windows10);

            var resultados = await motor.ExecutarAsync(plano, null, CancellationToken.None);

            Assert.All(resultados, r => Assert.Equal(StatusEtapa.Concluida, r.Status));
            Assert.Equal(plano.Count(e => e.Script != null), ps.Execucoes.Count);
            var papel = ps.Execucoes.Single(e => e.Key.Contains("NoPerfil $perfil.Conta") && e.Key.Contains("'Wallpaper'")).Value;
            Assert.True(File.Exists(papel[ScriptsPadronizacao.VarPapelAdm]));
            Assert.True(File.Exists(papel[ScriptsPadronizacao.VarPapelLab]));
            Assert.StartsWith(opcoes.PastaLocalPapeis, papel[ScriptsPadronizacao.VarPapelAdm]);
        }

        [Fact]
        public async Task FalhaNumaEtapaNaoImpedeAsOutras()
        {
            var ps = new PowerShellFalso(s => s.Contains("GarantirConta")
                ? new ResultadoPowerShell(1, "ERRO: acesso negado")
                : new ResultadoPowerShell(0, "RESULTADO: ok"));
            var motor = new MotorPadronizacao(ps, new RedeFalsa());
            var plano = motor.Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10);

            var resultados = await motor.ExecutarAsync(plano, null, CancellationToken.None);

            var indice = plano.ToList().FindIndex(e => e.Id == "contas");
            Assert.Equal(StatusEtapa.Falhou, resultados[indice].Status);
            Assert.Equal("acesso negado", resultados[indice].Resumo);
            Assert.Equal(StatusEtapa.Concluida, resultados.Last().Status);
        }

        [Fact]
        public async Task SemWindowsCompativelOuSemAdministradorOrestoNaoRoda()
        {
            var ps = new PowerShellFalso(s => s.Contains("Win32_OperatingSystem")
                ? new ResultadoPowerShell(1, "ERRO: Abra o WinAllApp como Administrador.")
                : new ResultadoPowerShell(0, "RESULTADO: ok"));
            var motor = new MotorPadronizacao(ps, new RedeFalsa());
            var plano = motor.Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows11);

            var resultados = await motor.ExecutarAsync(plano, null, CancellationToken.None);

            Assert.Equal(StatusEtapa.Falhou, resultados[0].Status);
            Assert.All(resultados.Skip(1), r => Assert.Equal(StatusEtapa.Ignorada, r.Status));
            Assert.Single(ps.Execucoes);
        }

        [Fact]
        public async Task RedeFalhandoViraAvisoDoTopico8()
        {
            var motor = new MotorPadronizacao(new PowerShellFalso(), new RedeFalsa { Servidor = false, Internet = false });
            var plano = motor.Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10);

            var resultados = await motor.ExecutarAsync(plano, null, CancellationToken.None);

            var rede = resultados[1];
            Assert.Equal(StatusEtapa.ConcluidaComAviso, rede.Status);
            Assert.Contains("10.50.11.2: não responde", rede.Resumo);
            Assert.Contains("internet: sem acesso", rede.Resumo);
            Assert.Contains("tópico 8", rede.Resumo);
        }

        [Fact]
        public async Task PapelDeParedeSemRedeFalhaSoAEtapaDele()
        {
            var opcoes = ApoioPadronizacao.Opcoes();
            opcoes.PastaPapeisDeParede = Path.Combine(Dados.NovaPastaTemporaria(), "sem-rede");
            var motor = new MotorPadronizacao(new PowerShellFalso(), new RedeFalsa());
            var plano = motor.Planejar(opcoes, VersaoPadronizacao.Windows10);

            var resultados = await motor.ExecutarAsync(plano, null, CancellationToken.None);

            var indice = plano.ToList().FindIndex(e => e.Id == "papel");
            Assert.Equal(StatusEtapa.Falhou, resultados[indice].Status);
            Assert.Contains("não encontrada", resultados[indice].Resumo);
            Assert.Equal(StatusEtapa.Concluida, resultados[indice + 1].Status);
        }

        [Fact]
        public async Task CancelarMarcaOQueFaltaComoNaoExecutado()
        {
            using var cancelamento = new CancellationTokenSource();
            var ps = new PowerShellFalso(s =>
            {
                if (s.Contains("GarantirConta")) cancelamento.Cancel();
                return new ResultadoPowerShell(0, "RESULTADO: ok");
            });
            var motor = new MotorPadronizacao(ps, new RedeFalsa());
            var plano = motor.Planejar(ApoioPadronizacao.Opcoes(), VersaoPadronizacao.Windows10);

            var resultados = await motor.ExecutarAsync(plano, null, cancelamento.Token);

            var indice = plano.ToList().FindIndex(e => e.Id == "contas");
            Assert.Equal(StatusEtapa.Concluida, resultados[indice].Status);
            Assert.All(resultados.Skip(indice + 1), r => Assert.Equal(StatusEtapa.Ignorada, r.Status));
        }

        // ---------- Tela (ViewModel) ----------

        private static PadronizacaoViewModel NovaTela(IExecutorPowerShell ps = null, string windows = "11")
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var vm = new PadronizacaoViewModel(new MotorPadronizacao(ps ?? new PowerShellFalso(), new RedeFalsa()),
                ApoioPadronizacao.Windows(windows), "/app/WinAllApp.exe")
            {
                PastaPapeisDeParede = ApoioPadronizacao.CriarPastaPapeis(),
                PastaLocalPapeis = Path.Combine(Dados.NovaPastaTemporaria(), "local")
            };
            return vm;
        }

        private static void Preencher(PadronizacaoViewModel vm)
        {
            vm.Bloco = "BL1";
            vm.Local = "geo";
            vm.Numero = "5";
            vm.SenhaInformatica = "Senha!2025";
        }

        [Fact]
        public void TelaSoLiberaComOsCamposObrigatorios()
        {
            var vm = NovaTela();
            Assert.Equal(VersaoPadronizacao.Windows11, vm.Versao);
            Assert.Contains("Windows 11", vm.TextoWindows);
            Assert.False(vm.PodeExecutar);
            Assert.False(vm.ExecutarCommand.CanExecute(null));
            Assert.Contains("senha", vm.TextoErros);

            Preencher(vm);

            Assert.Equal("BL1-GEO-05", vm.NomeComputador);
            Assert.False(vm.TemErros, vm.TextoErros);
            Assert.True(vm.ExecutarCommand.CanExecute(null));

            vm.AdicionarImpressora = true;
            Assert.False(vm.PodeExecutar);
            vm.IpImpressora = "10.50.12.34";
            Assert.True(vm.PodeExecutar);
        }

        [Fact]
        public void PerfilAlunoOuBolsistaSaoExclusivos()
        {
            var vm = NovaTela();
            Assert.True(vm.PerfilAluno);
            vm.PerfilBolsista = true;
            Assert.False(vm.PerfilAluno);
            vm.PerfilAluno = false; // o RadioButton desmarcado não muda a escolha
            Assert.True(vm.PerfilBolsista);
        }

        [Fact]
        public void Windows7NaoPodePadronizar()
        {
            var vm = NovaTela(windows: "7");
            Preencher(vm);
            Assert.False(vm.Suportado);
            Assert.False(vm.PodeExecutar);
            Assert.Contains("só para Windows 10 e 11", vm.TextoErros);
        }

        [Fact]
        public async Task SemConfirmacaoNadaEExecutado()
        {
            var ps = new PowerShellFalso();
            var vm = NovaTela(ps);
            Preencher(vm);
            string pergunta = null;
            vm.Confirmar = texto => { pergunta = texto; return false; };

            vm.ExecutarCommand.Execute(null);
            await vm.ExecutarCommand.Execucao;

            Assert.Contains("7 Renomear para BL1-GEO-05", pergunta);
            Assert.Contains("4 Criar as contas Informatica e Aluno", pergunta);
            Assert.Empty(ps.Execucoes);
            Assert.Empty(vm.Etapas);
        }

        [Fact]
        public async Task ExecutaMostraCadaEtapaEOResumo()
        {
            var ps = new PowerShellFalso(s => s.Contains("SoftwareLicensingProduct")
                ? new ResultadoPowerShell(2, "AVISO: O Windows não está ativado e nenhuma chave foi informada.\nRESULTADO: Windows não ativado (sem chave).")
                : new ResultadoPowerShell(0, "RESULTADO: ok"));
            var vm = NovaTela(ps);
            Preencher(vm);
            vm.Confirmar = _ => true;

            vm.ExecutarCommand.Execute(null);
            await vm.ExecutarCommand.Execucao;
            await EsperarAsync(() => vm.Etapas.All(e => e.Status != StatusEtapa.Pendente && e.Status != StatusEtapa.Executando));

            Assert.False(vm.Executando);
            Assert.Equal(10, vm.Etapas.Count);
            var ativacao = vm.Etapas.Single(e => e.Etapa.Id == "ativacao");
            Assert.Equal(StatusEtapa.ConcluidaComAviso, ativacao.Status);
            Assert.Contains("nenhuma chave", ativacao.Resumo);
            Assert.Equal("9 de 10 etapa(s) concluída(s), 1 com aviso. Reinicie o computador para aplicar o novo nome e as políticas.", vm.ResumoFinal);
            Assert.Contains("Padronização iniciada no Windows 11: BL1-GEO-05", vm.Registro);
            Assert.DoesNotContain("Senha!2025", vm.Registro);
        }

        private static async Task EsperarAsync(Func<bool> condicao)
        {
            for (var i = 0; i < 200 && !condicao(); i++) await Task.Delay(20);
            Assert.True(condicao());
        }

        [Fact]
        public void BotaoDaTelaPrincipalAbreAPadronizacao()
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var carga = Dados.CarregarConfigTeste();
            var fila = new InstallQueue(new RunnerFalso(), new CopiadorPastas(),
                new RoteadorInstalacao(new ContextoInstalacao(carga.PastaConfig, carga.PastaConfig, AmbienteSistema.Simular("10"))));
            var principal = new MainViewModel(new LabCatalog(carga.Config), fila);
            var abriu = 0;
            principal.PadronizacaoSolicitada += (s, e) => abriu++;

            Assert.True(principal.AbrirPadronizacaoCommand.CanExecute(null));
            principal.AbrirPadronizacaoCommand.Execute(null);

            Assert.Equal(1, abriu);
        }
    }
}
