using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.UI.Tests
{
    /// <summary>O WinAllApp.exe precisa funcionar sozinho: config e kit de simulação vêm embutidos.</summary>
    public class ExecutavelUnicoTests
    {
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
    }
}
