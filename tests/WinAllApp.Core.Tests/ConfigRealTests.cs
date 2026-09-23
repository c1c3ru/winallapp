using System.IO;
using System.Linq;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>Confere o config.json do aplicativo contra os PDFs "BL1/BL2 - Programas Específicos".</summary>
    public class ConfigRealTests
    {
        private static ConfigLoadResult Carregar() =>
            ConfigLoader.CarregarArquivo(Path.Combine(Dados.Pasta, "app", "config.json"));

        [Fact]
        public void Blocos_TemOsLaboratoriosDosPdfs()
        {
            var catalogo = new LabCatalog(Carregar().Config);

            Assert.Equal(new[] { "BIOQ", "GEOTEC", "LQOI", "MODMOL", "MAT" }, catalogo.ObterLaboratorios("BL1").Select(l => l.Id));
            Assert.Equal(new[] { "LAMEP", "LCC", "LED", "LEE", "LIA", "LINC", "LSHIP", "LEA" }, catalogo.ObterLaboratorios("BL2").Select(l => l.Id));
        }

        [Theory]
        [InlineData("BL1", "BIOQ", 0)]
        [InlineData("BL1", "GEOTEC", 12)]
        [InlineData("BL1", "LQOI", 1)]
        [InlineData("BL1", "MODMOL", 3)]
        [InlineData("BL1", "MAT", 6)]
        [InlineData("BL2", "LAMEP", 0)]
        [InlineData("BL2", "LCC", 17)]
        [InlineData("BL2", "LED", 4)]
        [InlineData("BL2", "LEE", 12)]
        [InlineData("BL2", "LIA", 27)]
        [InlineData("BL2", "LINC", 13)]
        [InlineData("BL2", "LSHIP", 6)]
        [InlineData("BL2", "LEA", 8)]
        public void Laboratorio_TemAQuantidadeDeProgramasDoPdf(string bloco, string lab, int quantidade)
        {
            var catalogo = new LabCatalog(Carregar().Config);

            Assert.Equal(quantidade, catalogo.ObterProgramas(bloco, lab).Count);
        }

        [Fact]
        public void Matematica_TemOsProgramasDoPdfNaOrdem()
        {
            var nomes = new LabCatalog(Carregar().Config).ObterProgramas("BL1", "MAT").Select(p => p.Nome);

            Assert.Equal(new[] { "Winplot", "GeoGebra", "Basic MiKTeX", "TeXstudio", "Python", "DjVu Reader" }, nomes);
        }

        [Fact]
        public void LaboratorioSoComProgramasPadrao_MostraAvisoEDesabilitaSelecao()
        {
            var carga = Carregar();
            var vm = new MainViewModel(carga.Config, carga.PastaConfig, new RunnerFalso());

            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL2");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LAMEP");

            Assert.Empty(vm.Programas);
            Assert.True(vm.MostrarAvisoVazio);
            Assert.Contains("programas padrões", vm.TextoAvisoVazio);
            Assert.False(vm.SelecionarTodosCommand.CanExecute(null));

            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            Assert.False(vm.MostrarAvisoVazio);
            Assert.Equal(17, vm.Programas.Count);
        }
    }
}
