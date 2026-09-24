using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace WinAllApp.UI.Tests
{
    /// <summary>O WinAllApp.exe precisa funcionar sozinho: config e kit de simulação vêm embutidos.</summary>
    public class ExecutavelUnicoTests
    {
        private readonly ITestOutputHelper _saida;

        public ExecutavelUnicoTests(ITestOutputHelper saida) => _saida = saida;

        [Fact]
        public void SemConfigAoLado_UsaOConfigEmbutidoComOsLaboratoriosReais()
        {
            Assert.False(File.Exists(Path.Combine(Program.PastaDoExecutavel, "config.json")));

            var carga = Program.CarregarConfig(new string[0], out var origem);

            Assert.Contains("embutida", origem);
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            var catalogo = new LabCatalog(carga.Config);
            Assert.Equal(5, catalogo.ObterLaboratorios("BL1").Count);
            Assert.Equal(8, catalogo.ObterLaboratorios("BL2").Count);
        }

        [Fact]
        public async Task ModoSimulacao_ExtraiOsBatsEInstalaSoOsSelecionados()
        {
            var carga = Program.CarregarConfig(new[] { "--simulacao" }, out var origem);

            Assert.StartsWith("SIMULAÇÃO", origem);
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            var mocks = Path.Combine(carga.PastaConfig, "mock-installers");
            Assert.Contains("\r\n", File.ReadAllText(Path.Combine(mocks, "vscode-setup.bat")));

            var pastaInstaladores = InstallCommandBuilder.ResolverPastaInstaladores(carga.Config, carga.PastaConfig);
            var vm = new MainViewModel(carga.Config, pastaInstaladores, new ProcessRunner());
            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL2");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            vm.SelecionarTodosCommand.Execute(null);
            vm.Programas.Single(p => p.Programa.Id == "python").Selecionado = false;

            var resultados = await vm.InstalarAsync();

            Assert.All(resultados, r => Assert.True(r.Sucesso, r.Mensagem));
            var log = File.ReadAllLines(Path.Combine(mocks, "instalacoes-simuladas.log"));
            Assert.Equal(2, log.Length);
            Assert.Contains(log, l => l.Contains("vscode-setup.bat /VERYSILENT /NORESTART"));
            Assert.Contains(log, l => l.Contains("codeblocks-setup.bat /S"));
        }
    
        /// <summary>
        /// Simulação completa com os .bat reais: força Windows 10 e depois Windows 7 e confere no log
        /// o comando que chegou a cada "instalador" (winget.bat, choco.bat, pasta de rede e cópia).
        /// </summary>
        [Theory]
        [InlineData("10", "winget install --id GeoGebra.Classic --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")]
        [InlineData("7", "choco install geogebra-classic -y --no-progress")]
        public async Task ModoSimulacao_Motor4Categorias_RoteiaConformeOWindows(string windows, string comandoGerenciador)
        {
            var args = new[] { "--simulacao", "--simular-windows", windows };
            var carga = Program.CarregarConfig(args, out _);
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            var ambiente = Program.CriarAmbiente(args, simulacao: true, pastaConfig: carga.PastaConfig);
            var vm = Program.CriarViewModel(carga, ambiente);

            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL1");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "MOTOR");
            vm.SelecionarTodosCommand.Execute(null);
            var resultados = await vm.InstalarAsync();

            foreach (var linha in vm.Log) _saida.WriteLine(linha);
            Assert.All(resultados, r => Assert.True(r.Sucesso, r.Mensagem));

            var log = File.ReadAllLines(Path.Combine(carga.PastaConfig, "mock-installers", "instalacoes-simuladas.log"));
            foreach (var linha in log) _saida.WriteLine("log: " + linha);
            Assert.Equal(3, log.Length);
            Assert.EndsWith(comandoGerenciador, log[0]);
            Assert.EndsWith("setup-licenciado.bat /qb /norestart", log[1]);
            Assert.EndsWith("octave-setup.bat /S", log[2]);
            Assert.True(File.Exists(Path.Combine(carga.PastaConfig, "destino-copias", "mock-copia", "dados", "exemplo.txt")));
        }

        [Fact]
        public async Task ModoSimulacao_Win7SemTls12_NaoChamaOChocolateyEAvisaVersaoIncompativel()
        {
            var args = new[] { "--simulacao", "--simular-windows", "7", "--sem-tls12" };
            var carga = Program.CarregarConfig(args, out _);
            var vm = Program.CriarViewModel(carga, Program.CriarAmbiente(args, true, carga.PastaConfig));

            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL1");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "WIN7");
            Assert.StartsWith("Versão incompatível com o SO", vm.Programas.Single(p => p.Programa.Id == "mock-vs2022").AvisoCompatibilidade);
            vm.SelecionarTodosCommand.Execute(null);
            var resultados = await vm.InstalarAsync();

            Assert.Equal(EstadoInstalacao.Falha, resultados[0].Estado);
            Assert.Contains("TLS 1.2", resultados[0].Mensagem);
            Assert.Equal(EstadoInstalacao.Incompativel, resultados[1].Estado);
            Assert.False(File.Exists(Path.Combine(carga.PastaConfig, "mock-installers", "instalacoes-simuladas.log")));
        }

        [Fact]
        public void CaminhoDeRedeUnc_ComEspacos_FicaEntreAspas()
        {
            var exe = InstallCommandBuilder.Construir(
                new WinAllApp.Core.Models.Programa { Id = "a", Instalador = @"Pacote Licenciado\AutoCAD 2018\Setup.exe", Argumentos = "/W /q" },
                @"\\servidor\instaladores");
            Assert.Equal(@"\\servidor\instaladores\Pacote Licenciado\AutoCAD 2018\Setup.exe", exe.Arquivo);
            Assert.Equal(@"""\\servidor\instaladores\Pacote Licenciado\AutoCAD 2018\Setup.exe"" /W /q", exe.ToString());

            var msi = InstallCommandBuilder.Construir(
                new WinAllApp.Core.Models.Programa { Id = "b", Instalador = @"Google Earth\earth.msi" }, @"\\servidor\instaladores");
            Assert.Equal(@"/i ""\\servidor\instaladores\Google Earth\earth.msi"" /qn /norestart", msi.Argumentos);
        }

        [Fact]
        public void PrimeiraExecucao_MostraOTutorial_DepoisNaoMaisAMenosQuePedido()
        {
            var preferencias = new PreferenciasUsuario(Path.Combine(Path.GetTempPath(), "winallapp-pref-" + Guid.NewGuid().ToString("N"), "p.ini"));
            Assert.True(Program.DeveMostrarOnboarding(new string[0], preferencias));
            preferencias.OnboardingConcluido = true;
            Assert.False(Program.DeveMostrarOnboarding(new string[0], preferencias));
            Assert.True(Program.DeveMostrarOnboarding(new[] { "--tutorial" }, preferencias));
        }

        /// <summary>
        /// Executa o WinAllApp.exe de verdade (como o técnico faria) com um %APPDATA% vazio:
        /// o tutorial precisa abrir sozinho sobre a janela principal. Na segunda execução, não.
        /// </summary>
        [Fact]
        public void ExecutavelReal_PrimeiraExecucaoAbreOTutorial_SegundaNao()
        {
            var appData = Path.Combine(Path.GetTempPath(), "winallapp-appdata-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(appData);
            var exe = Path.Combine(Program.PastaDoExecutavel, "WinAllApp.exe");
            Assert.True(File.Exists(exe), exe);

            var primeira = Executar(exe, appData, TimeSpan.FromSeconds(40));
            if (primeira == null) return; // ambiente sem elevação: já registrado na saída
            _saida.WriteLine("1ª execução: " + string.Join(" | ", primeira));
            Assert.Contains(primeira, t => t.StartsWith("WinAllApp") && t.Contains("MODO SIMULAÇÃO"));
            Assert.Contains("Bem-vindo ao WinAllApp", primeira);

            new PreferenciasUsuario(Path.Combine(appData, "WinAllApp", "preferencias.ini")).OnboardingConcluido = true;
            var segunda = Executar(exe, appData, TimeSpan.FromSeconds(8));
            _saida.WriteLine("2ª execução: " + string.Join(" | ", segunda));
            Assert.Contains(segunda, t => t.StartsWith("WinAllApp") && t.Contains("MODO SIMULAÇÃO"));
            Assert.DoesNotContain("Bem-vindo ao WinAllApp", segunda);
        }

        /// <summary>Abre o .exe em modo simulação, espera o tutorial (ou o tempo limite) e devolve os títulos das janelas visíveis.</summary>
        private List<string> Executar(string exe, string appData, TimeSpan espera)
        {
            var info = new ProcessStartInfo(exe, "--simulacao") { UseShellExecute = false };
            info.EnvironmentVariables["APPDATA"] = appData;
            Process processo;
            try
            {
                processo = Process.Start(info);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                _saida.WriteLine("Execução real ignorada: o manifesto pede administrador e o teste não está elevado.");
                return null;
            }

            try
            {
                var limite = DateTime.UtcNow + espera;
                var titulos = new List<string>();
                while (DateTime.UtcNow < limite)
                {
                    Thread.Sleep(500);
                    titulos = TitulosDoProcesso(processo.Id);
                    if (titulos.Contains("Bem-vindo ao WinAllApp")) break;
                }
                return titulos;
            }
            finally
            {
                try { processo.Kill(); } catch (InvalidOperationException) { }
                processo.WaitForExit(5000);
                processo.Dispose();
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder texto, int max);

        private static List<string> TitulosDoProcesso(int pid)
        {
            var titulos = new List<string>();
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var dono);
                if (dono == pid && IsWindowVisible(h))
                {
                    var sb = new StringBuilder(256);
                    GetWindowText(h, sb, sb.Capacity);
                    if (sb.Length > 0) titulos.Add(sb.ToString());
                }
                return true;
            }, IntPtr.Zero);
            return titulos;
        }
    }
}
