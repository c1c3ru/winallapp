using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace WinAllApp.Core.Tests
{
    /// <summary>
    /// Roteamento das 4 categorias com o mock do config.simulacao.json (laboratórios MOTOR e WIN7),
    /// forçando o Windows detectado como 10 e como 7. Confere o comando gerado para cada programa.
    /// </summary>
    public class RoteamentoTests
    {
        private const string Winget = @"C:\Ferramentas\winget.exe";
        private const string Choco = @"C:\ProgramData\chocolatey\bin\choco.exe";

        private readonly ITestOutputHelper _saida;

        public RoteamentoTests(ITestOutputHelper saida) => _saida = saida;

        private static readonly string PastaRede = Path.Combine(Path.GetTempPath(), "servidor", "instaladores");
        private static readonly string DestinoCopias = Path.Combine(Path.GetTempPath(), "Programas");

        private static InstallerConfig ConfigMock()
        {
            var carga = ConfigLoader.CarregarArquivo(Path.Combine(Dados.Pasta, "app", "config.simulacao.json"));
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            return carga.Config;
        }

        private static Programa P(string id) => ConfigMock().Programas.Single(p => p.Id == id);

        /// <summary>Rede "de mentira": todos os arquivos e pastas existem, menos os listados.</summary>
        private static RoteadorInstalacao Roteador(AmbienteSistema ambiente, params string[] ausentes)
        {
            var contexto = new ContextoInstalacao(PastaRede, DestinoCopias, ambiente)
            {
                ArquivoExiste = c => !ausentes.Any(a => c.EndsWith(a, StringComparison.OrdinalIgnoreCase)),
                PastaExiste = c => !ausentes.Any(a => c.EndsWith(a, StringComparison.OrdinalIgnoreCase))
            };
            return new RoteadorInstalacao(contexto);
        }

        private static AmbienteSistema Win10() => AmbienteSistema.Simular("10", Winget, Choco);
        private static AmbienteSistema Win7(bool dotNet48 = true, bool tls12 = true) => AmbienteSistema.Simular("7", Winget, Choco, dotNet48, tls12);

        private void Mostrar(string titulo, IEnumerable<PlanoInstalacao> planos)
        {
            _saida.WriteLine("== " + titulo);
            foreach (var plano in planos) _saida.WriteLine($"{plano.Programa.Id,-16} {plano.Descricao}");
        }

        [Fact]
        public void Win10_CadaCategoriaVaiParaOCaminhoCerto()
        {
            var roteador = Roteador(Win10());
            var planos = new[] { "mock-winget", "mock-licenciado", "mock-gratuito", "mock-copia" }.Select(id => roteador.Planejar(P(id))).ToList();
            Mostrar("Windows 10", planos);

            var winget = planos[0];
            Assert.Equal(FonteInstalacao.Winget, winget.Fonte);
            Assert.Equal(Winget, winget.Comando.Arquivo);
            Assert.Equal("install --id GeoGebra.Classic --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                winget.Comando.Argumentos);

            var licenciado = planos[1];
            Assert.Equal(CategoriaInstalacao.OfflineLicenciado, licenciado.Categoria);
            Assert.Equal(FonteInstalacao.Rede, licenciado.Fonte);
            Assert.Equal("cmd.exe", licenciado.Comando.Arquivo);
            Assert.StartsWith("/c \"\"" + PastaRede, licenciado.Comando.Argumentos);
            Assert.Contains("Pacote Licenciado", licenciado.Comando.Argumentos);
            Assert.EndsWith("setup-licenciado.bat\" /qb /norestart\"", licenciado.Comando.Argumentos);
            Assert.Contains("ativação da licença", licenciado.Motivo);

            var gratuito = planos[2];
            Assert.Equal(FonteInstalacao.Rede, gratuito.Fonte);
            Assert.Equal($"/c \"\"{Path.Combine(PastaRede, "octave-setup.bat")}\" /S\"", gratuito.Comando.Argumentos);

            var copia = planos[3];
            Assert.Equal(AcaoInstalacao.CopiarPasta, copia.Acao);
            Assert.Equal(Path.Combine(PastaRede, "Portatil"), copia.Origem);
            Assert.Equal(Path.Combine(DestinoCopias, "mock-copia"), copia.Destino);
        }

        [Fact]
        public void Win7_GerenciadorViraChocolateyEOsDemaisNaoMudam()
        {
            var roteador = Roteador(Win7());
            var planos = new[] { "mock-winget", "mock-licenciado", "mock-gratuito", "mock-copia" }.Select(id => roteador.Planejar(P(id))).ToList();
            Mostrar("Windows 7 (.NET 4.8 e TLS 1.2 ok)", planos);

            Assert.Equal(FonteInstalacao.Chocolatey, planos[0].Fonte);
            Assert.Equal(Choco, planos[0].Comando.Arquivo);
            Assert.Equal("install geogebra-classic -y --no-progress", planos[0].Comando.Argumentos);
            Assert.Equal(FonteInstalacao.Rede, planos[1].Fonte);
            Assert.Equal(FonteInstalacao.Rede, planos[2].Fonte);
            Assert.Equal(AcaoInstalacao.CopiarPasta, planos[3].Acao);
        }

        [Theory]
        [InlineData(false, true, ".NET Framework 4.8")]
        [InlineData(true, false, "TLS 1.2")]
        public void Win7_SemPreRequisitos_NaoChamaOChocolatey(bool dotNet48, bool tls12, string motivo)
        {
            var plano = Roteador(Win7(dotNet48, tls12)).Planejar(P("mock-winget"));
            _saida.WriteLine(plano.Descricao);

            Assert.Equal(AcaoInstalacao.Bloqueado, plano.Acao);
            Assert.Null(plano.Comando);
            Assert.Contains(motivo, plano.Motivo);
        }

        [Fact]
        public void Win7_VersoesNovasSaoBloqueadasOuTrocadasPelaVersaoCompativel()
        {
            var roteador = Roteador(Win7());
            var python = roteador.Planejar(P("mock-python"));
            var vs2022 = roteador.Planejar(P("mock-vs2022"));
            Mostrar("Windows 7: versões novas", new[] { python, vs2022 });

            Assert.Equal(FonteInstalacao.Chocolatey, python.Fonte);
            Assert.Equal("install python3 -y --no-progress --version 3.8.10", python.Comando.Argumentos);

            Assert.Equal(AcaoInstalacao.Incompativel, vs2022.Acao);
            Assert.StartsWith("Versão incompatível com o SO", vs2022.Motivo);
            Assert.Contains("Windows 7", vs2022.Motivo);
        }

        [Fact]
        public void Win10_VersoesNovasUsamOWinget()
        {
            var roteador = Roteador(Win10());
            Assert.Equal("install --id Python.Python.3.13 --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                roteador.Planejar(P("mock-python")).Comando.Argumentos);
            Assert.Equal(FonteInstalacao.Winget, roteador.Planejar(P("mock-vs2022")).Fonte);
        }

        [Fact]
        public void RedeEAFontePrimaria_WingetSoQuandoOInstaladorNaoEstaNaPasta()
        {
            var octave = new Programa
            {
                Id = "octave", Nome = "GNU Octave", Categoria = "gerenciador", WingetId = "GNU.Octave", ChocoId = "octave",
                Instalador = @"Octave\octave-setup.exe", Argumentos = "/S"
            };

            var daRede = Roteador(Win10()).Planejar(octave);
            Assert.Equal(FonteInstalacao.Rede, daRede.Fonte);
            Assert.Equal(Path.Combine(PastaRede, @"Octave\octave-setup.exe"), daRede.Comando.Arquivo);

            var semArquivo = Roteador(Win10(), "octave-setup.exe").Planejar(octave);
            Assert.Equal(FonteInstalacao.Winget, semArquivo.Fonte);

            var semArquivoWin7 = Roteador(Win7(), "octave-setup.exe").Planejar(octave);
            Assert.Equal(FonteInstalacao.Chocolatey, semArquivoWin7.Fonte);
        }

        [Fact]
        public void Win10SemWinget_SemInstaladorNaRede_ExplicaOQueFalta()
        {
            var plano = Roteador(AmbienteSistema.Simular("10")).Planejar(P("mock-winget"));
            Assert.Equal(AcaoInstalacao.Bloqueado, plano.Acao);
            Assert.Contains("winget indisponível", plano.Motivo);
        }

        [Fact]
        public void Licenciado_AusenteNaRede_PedeOPacoteDeImplantacao()
        {
            var plano = Roteador(Win10(), "setup-licenciado.bat").Planejar(P("mock-licenciado"));
            Assert.Equal(AcaoInstalacao.Bloqueado, plano.Acao);
            Assert.Contains("pacote de implantação", plano.Motivo);
        }

        [Fact]
        public void CopiaPasta_OrigemAusente_EDestinoComVariavelDeAmbiente()
        {
            var ausente = Roteador(Win10(), "Portatil").Planejar(P("mock-copia"));
            Assert.Equal(AcaoInstalacao.Bloqueado, ausente.Acao);

            Environment.SetEnvironmentVariable("WINALLAPP_TESTE_DESTINO", DestinoCopias);
            var programa = P("mock-copia");
            programa.Destino = Path.Combine("%WINALLAPP_TESTE_DESTINO%", "Winplot");
            var plano = Roteador(Win10()).Planejar(programa);
            Assert.Equal(Path.Combine(DestinoCopias, "Winplot"), plano.Destino);
        }

        [Theory]
        [InlineData("gerenciador", null, null, CategoriaInstalacao.Gerenciador)]
        [InlineData(null, "winget", null, CategoriaInstalacao.Gerenciador)]
        [InlineData("offline_licenciado", "exe", null, CategoriaInstalacao.OfflineLicenciado)]
        [InlineData("copia_pasta", null, null, CategoriaInstalacao.CopiaPasta)]
        [InlineData(null, "exe", "Foo.Bar", CategoriaInstalacao.Gerenciador)]
        [InlineData(null, "exe", null, CategoriaInstalacao.OfflineGratuito)]
        public void Categoria_ChaveTipoOuDeducao(string categoria, string tipo, string wingetId, CategoriaInstalacao esperado)
        {
            var p = new Programa { Id = "x", Categoria = categoria, Tipo = tipo, WingetId = wingetId, Instalador = "x.exe" };
            Assert.Equal(esperado, CategoriaResolver.Resolver(p));
        }

        [Fact]
        public void TipoComNomeDeCategoria_DeduzOArquivoPelaExtensao()
        {
            var p = new Programa { Id = "g", Tipo = "winget", WingetId = "GeoGebra.Classic", Instalador = @"GeoGebra\geogebra.msi" };
            Assert.Equal(TipoInstalador.Msi, InstallCommandBuilder.ResolverTipo(p));
        }

        [Fact]
        public void ConfigComCategoriaOuWindowsInvalidos_DaErro()
        {
            var json = "{ \"blocos\": [ { \"id\": \"BL1\", \"laboratorios\": [] } ], \"programas\": [" +
                       "{ \"id\": \"a\", \"instalador\": \"a.exe\", \"argumentos\": \"/S\", \"categoria\": \"nuvem\" }," +
                       "{ \"id\": \"b\", \"instalador\": \"b.exe\", \"argumentos\": \"/S\", \"windowsMinimo\": \"95\" }," +
                       "{ \"id\": \"c\", \"categoria\": \"gerenciador\", \"wingetId\": \"C.C\" }," +
                       "{ \"id\": \"d\", \"categoria\": \"copia_pasta\" } ] }";
            var carga = ConfigLoader.CarregarTexto(json, Path.GetTempPath());

            Assert.Contains(carga.Erros, e => e.Contains("categoria desconhecida"));
            Assert.Contains(carga.Erros, e => e.Contains("windowsMinimo inválido"));
            Assert.DoesNotContain(carga.Erros, e => e.Contains("Programa c "));  // gerenciador só com wingetId é válido
            Assert.Contains(carga.Erros, e => e.Contains("Programa d (cópia de pasta)"));
        }

        [Fact]
        public void ConfigReal_TemAs4CategoriasE23PacotesWinget()
        {
            var carga = ConfigLoader.CarregarArquivo(Path.Combine(Dados.Pasta, "app", "config.json"));
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));

            var porCategoria = carga.Config.Programas.GroupBy(CategoriaResolver.Resolver).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(23, porCategoria[CategoriaInstalacao.Gerenciador]);
            Assert.Equal(22, porCategoria[CategoriaInstalacao.OfflineLicenciado]);
            Assert.Equal(20, porCategoria[CategoriaInstalacao.OfflineGratuito]);
            Assert.Equal(4, porCategoria[CategoriaInstalacao.CopiaPasta]);
            Assert.Equal(23, carga.Config.Programas.Count(p => CategoriaResolver.Resolver(p) == CategoriaInstalacao.Gerenciador && !string.IsNullOrEmpty(p.WingetId)));

            // No Windows 10, sem nenhum instalador na rede, os 23 vão pelo winget com --silent.
            var roteador = new RoteadorInstalacao(new ContextoInstalacao(@"\\servidor\instaladores", null, Win10()) { ArquivoExiste = _ => false });
            var planos = carga.Config.Programas.Where(p => CategoriaResolver.Resolver(p) == CategoriaInstalacao.Gerenciador).Select(roteador.Planejar).ToList();
            Mostrar("config.json real, Windows 10 sem a pasta de rede", planos);
            Assert.All(planos, pl => Assert.Equal(FonteInstalacao.Winget, pl.Fonte));
            Assert.All(planos, pl => Assert.Contains("--silent", pl.Comando.Argumentos));
        }

        [Fact]
        public async Task Fila_Win10EWin7_ExecutaCadaPlanoECopiaAPasta()
        {
            foreach (var windows in new[] { "10", "7" })
            {
                var pasta = Dados.NovaPastaTemporaria();
                var rede = Path.Combine(pasta, "rede");
                Directory.CreateDirectory(Path.Combine(rede, "Portatil", "dados"));
                File.WriteAllText(Path.Combine(rede, "Portatil", "winplot.exe"), "x");
                File.WriteAllText(Path.Combine(rede, "Portatil", "dados", "exemplo.txt"), "y");
                File.WriteAllText(Path.Combine(rede, "octave-setup.bat"), "");
                Directory.CreateDirectory(Path.Combine(rede, "Pacote Licenciado"));
                File.WriteAllText(Path.Combine(rede, "Pacote Licenciado", "setup-licenciado.bat"), "");

                var config = ConfigMock();
                config.Programas.Single(p => p.Id == "mock-licenciado").Instalador = Path.Combine("Pacote Licenciado", "setup-licenciado.bat");
                var ambiente = AmbienteSistema.Simular(windows, Winget, Choco);
                var roteador = new RoteadorInstalacao(new ContextoInstalacao(rede, Path.Combine(pasta, "local"), ambiente));
                var runner = new RunnerFalso(c => c.Arquivo == Winget ? EstrategiaGerenciador.WingetJaInstalado : 0);
                var vm = new MainViewModel(new LabCatalog(config), new InstallQueue(runner, new CopiadorPastas(), roteador));

                vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL1");
                vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "MOTOR");
                vm.SelecionarTodosCommand.Execute(null);
                var resultados = await vm.InstalarAsync();

                _saida.WriteLine($"== Registro da fila, Windows {windows}");
                foreach (var linha in vm.Log) _saida.WriteLine(linha);

                Assert.All(resultados, r => Assert.True(r.Sucesso, r.Mensagem));
                var comandos = runner.Comandos.ToList();
                Assert.Equal(3, comandos.Count); // cópia não usa processo
                Assert.Equal(windows == "10" ? Winget : Choco, comandos[0].Arquivo);
                Assert.Contains("setup-licenciado.bat", comandos[1].Argumentos);
                Assert.Contains("octave-setup.bat", comandos[2].Argumentos);
                Assert.True(File.Exists(Path.Combine(pasta, "local", "mock-copia", "dados", "exemplo.txt")));
                Assert.Contains("Pasta copiada", resultados[3].Mensagem);
                if (windows == "10") Assert.Contains("Já estava instalado", resultados[0].Mensagem);
                Assert.Equal("Instalado (ativar licença)", vm.Programas[1].EstadoTexto);
            }
        }

        [Fact]
        public async Task Fila_Win7_ProgramaIncompativelFicaMarcadoSemExecutar()
        {
            var config = ConfigMock();
            var roteador = new RoteadorInstalacao(new ContextoInstalacao(PastaRede, DestinoCopias, Win7()));
            var runner = new RunnerFalso();
            var vm = new MainViewModel(new LabCatalog(config), new InstallQueue(runner, new CopiadorPastas(), roteador));
            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL1");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "WIN7");

            // Aviso aparece na lista antes de instalar.
            Assert.Contains("3.8.10", vm.Programas[0].AvisoCompatibilidade);
            Assert.StartsWith("Versão incompatível com o SO", vm.Programas[1].AvisoCompatibilidade);

            vm.SelecionarTodosCommand.Execute(null);
            var resultados = await vm.InstalarAsync();

            Assert.Equal(EstadoInstalacao.Sucesso, resultados[0].Estado);
            Assert.Equal(EstadoInstalacao.Incompativel, resultados[1].Estado);
            Assert.Equal("Incompatível com o SO", vm.Programas[1].EstadoTexto);
            Assert.Single(runner.Comandos);
            Assert.Contains("1 incompatível(is)", vm.StatusTexto);
        }
    }
}
