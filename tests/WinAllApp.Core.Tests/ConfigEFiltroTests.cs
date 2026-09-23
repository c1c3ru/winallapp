using System.IO;
using System.Linq;
using WinAllApp.Core.Services;
using Xunit;

namespace WinAllApp.Core.Tests
{
    public class ConfigEFiltroTests
    {
        [Fact]
        public void ConfigDeTeste_CarregaDoisBlocosSemErros()
        {
            var carga = Dados.CarregarConfigTeste();

            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            Assert.Equal(new[] { "BL1", "BL2" }, carga.Config.Blocos.Select(b => b.Id));
            Assert.Equal(5, carga.Config.Programas.Count);
            Assert.Equal("Matemática", carga.Config.Blocos[1].Laboratorios[0].Nome);
        }

        [Fact]
        public void FiltroPorLaboratorio_LccTemTresProgramas()
        {
            var catalogo = new LabCatalog(Dados.CarregarConfigTeste().Config);

            var programas = catalogo.ObterProgramas("BL1", "LCC");

            Assert.Equal(new[] { "vscode", "python", "codeblocks" }, programas.Select(p => p.Id));
        }

        [Fact]
        public void FiltroPorLaboratorio_MatematicaTemDoisProgramas()
        {
            var catalogo = new LabCatalog(Dados.CarregarConfigTeste().Config);

            var programas = catalogo.ObterProgramas("BL2", "MAT");

            Assert.Equal(new[] { "geogebra", "octave" }, programas.Select(p => p.Id));
        }

        [Fact]
        public void FiltroPorLaboratorio_LaboratorioDeOutroBlocoNaoAparece()
        {
            var catalogo = new LabCatalog(Dados.CarregarConfigTeste().Config);

            Assert.Empty(catalogo.ObterProgramas("BL1", "MAT"));
            Assert.Equal(new[] { "LCC" }, catalogo.ObterLaboratorios("BL1").Select(l => l.Id));
            Assert.Equal(new[] { "MAT" }, catalogo.ObterLaboratorios("bl2").Select(l => l.Id));
        }

        [Fact]
        public void ConfigDoAplicativo_ExemploESimulacaoSaoValidos()
        {
            foreach (var nome in new[] { "config.json", "config.simulacao.json" })
            {
                var carga = ConfigLoader.CarregarArquivo(Path.Combine(Dados.Pasta, "app", nome));
                Assert.True(carga.Valido, nome + ": " + string.Join("; ", carga.Erros));
            }
        }

        [Fact]
        public void Validacao_AcusaProgramaInexistenteEIdDuplicado()
        {
            const string json = @"{
              ""blocos"": [ { ""id"": ""BL1"", ""laboratorios"": [ { ""id"": ""LCC"", ""programas"": [ ""a"", ""fantasma"" ] } ] } ],
              ""programas"": [
                { ""id"": ""a"", ""instalador"": ""a.msi"" },
                { ""id"": ""A"", ""instalador"": ""b.msi"" }
              ] }";

            var carga = ConfigLoader.CarregarTexto(json, "/tmp");

            Assert.False(carga.Valido);
            Assert.Contains(carga.Erros, e => e.Contains("fantasma"));
            Assert.Contains(carga.Erros, e => e.Contains("duplicado"));
        }

        [Fact]
        public void Validacao_AvisaExeSemArgumentoSilencioso()
        {
            const string json = @"{
              ""blocos"": [ { ""id"": ""BL1"", ""laboratorios"": [ { ""id"": ""X"", ""programas"": [ ""p"" ] } ] } ],
              ""programas"": [ { ""id"": ""p"", ""instalador"": ""setup.exe"" } ] }";

            var carga = ConfigLoader.CarregarTexto(json, "/tmp");

            Assert.True(carga.Valido);
            Assert.Single(carga.Avisos);
        }

        [Fact]
        public void JsonMalFormado_RetornaErroSemExcecao()
        {
            var carga = ConfigLoader.CarregarTexto("{ isto não é json", "/tmp");

            Assert.False(carga.Valido);
            Assert.StartsWith("config.json inválido", carga.Erros[0]);
        }
    }
}
