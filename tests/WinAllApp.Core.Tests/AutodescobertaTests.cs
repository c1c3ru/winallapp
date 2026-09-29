using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>
    /// Autodescoberta: o app confere em segundo plano se cada instalador do config.json existe na pasta de rede,
    /// mostra ✔/❌ (IsAvailable) e só deixa marcar o que foi encontrado, sem travar a tela.
    /// </summary>
    public class AutodescobertaTests
    {
        private static readonly string Base = Path.Combine(Path.GetTempPath(), "winallapp-rede-falsa", "Laboratórios - Programas");

        /// <summary>LCC (VS Code, Python, Code::Blocks) com a existência dos arquivos decidida pelo teste.</summary>
        private static (MainViewModel vm, ContextoInstalacao contexto, RunnerFalso runner) Criar(
            Func<string, bool> arquivoExiste, Func<string, bool> pastaExiste = null, bool abrirLaboratorio = true)
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var carga = Dados.CarregarConfigTeste();
            var runner = new RunnerFalso();
            var fila = new InstallQueue(runner, Base);
            var contexto = fila.Roteador.Contexto;
            contexto.ArquivoExiste = arquivoExiste;
            contexto.PastaExiste = pastaExiste ?? (_ => true);
            var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
            if (abrirLaboratorio)
            {
                vm.BlocoSelecionado = vm.Blocos[0];
                vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            }
            return (vm, contexto, runner);
        }

        private static Func<string, bool> Menos(params string[] ausentes) =>
            f => !ausentes.Any(a => f.EndsWith(a, StringComparison.OrdinalIgnoreCase));

        // ===== Scanner (ConfigLoader) =====

        [Fact]
        public void CaminhoNaRede_JuntaAPastaBaseComOCaminhoDoJson()
        {
            var exe = ConfigLoader.CaminhoNaRede(new Programa { Id = "a", Instalador = @"Sub Pasta\setup.exe" }, Base);
            Assert.Equal(InstallCommandBuilder.ResolverCaminho(new Programa { Instalador = @"Sub Pasta\setup.exe" }, Base), exe.Caminho);
            Assert.StartsWith(Base, exe.Caminho);
            Assert.False(exe.EhPasta);
            Assert.False(exe.TemAlternativa);

            var copia = ConfigLoader.CaminhoNaRede(new Programa { Id = "b", Categoria = "copia_pasta", Instalador = "Portatil" }, Base);
            Assert.True(copia.EhPasta);

            var comWinget = ConfigLoader.CaminhoNaRede(new Programa { Id = "c", Categoria = "gerenciador", Instalador = "x.exe", WingetId = "X.X" }, Base);
            Assert.True(comWinget.TemAlternativa);

            // Só gerenciador de pacotes: nada para procurar na rede.
            Assert.Null(ConfigLoader.CaminhoNaRede(new Programa { Id = "d", Categoria = "gerenciador", WingetId = "Y.Y" }, Base));
        }

        [Fact]
        public async Task VerificarCaminho_EncontradoNaoEncontradoEErro()
        {
            var ok = await ConfigLoader.VerificarCaminhoAsync("a", _ => true, TimeSpan.FromSeconds(5), CancellationToken.None);
            var falta = await ConfigLoader.VerificarCaminhoAsync("a", _ => false, TimeSpan.FromSeconds(5), CancellationToken.None);
            var erro = await ConfigLoader.VerificarCaminhoAsync("a", _ => throw new UnauthorizedAccessException("Acesso negado."),
                TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(StatusArquivo.Encontrado, ok.Status);
            Assert.Equal(StatusArquivo.NaoEncontrado, falta.Status);
            Assert.Equal(StatusArquivo.Erro, erro.Status);
            Assert.Equal("Acesso negado.", erro.Detalhe);
        }

        [Fact]
        public async Task VerificarCaminho_ServidorQueNaoRespondeDesisteNoTimeout()
        {
            using (var nuncaResponde = new ManualResetEventSlim())
            {
                var relogio = System.Diagnostics.Stopwatch.StartNew();
                var r = await ConfigLoader.VerificarCaminhoAsync(@"\\10.50.11.2\x\setup.exe", _ => { nuncaResponde.Wait(TimeSpan.FromSeconds(30)); return true; },
                    TimeSpan.FromMilliseconds(200), CancellationToken.None);
                relogio.Stop();
                nuncaResponde.Set();

                Assert.Equal(StatusArquivo.SemResposta, r.Status);
                Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), $"Demorou {relogio.Elapsed}.");
            }
        }

        [Fact]
        public async Task VerificarCaminhos_RodaEmParaleloEAvisaCadaResultado()
        {
            var alvos = Enumerable.Range(1, 8)
                .Select(i => ConfigLoader.CaminhoNaRede(new Programa { Id = "p" + i, Instalador = $"p{i}.exe" }, Base))
                .ToList();
            var simultaneos = 0;
            var maximo = 0;
            var avisos = new ConcurrentQueue<ResultadoVerificacao>();

            var resultados = await ConfigLoader.VerificarCaminhosAsync(alvos, Base,
                f =>
                {
                    var agora = Interlocked.Increment(ref simultaneos);
                    InterlockedMax(ref maximo, agora);
                    // Espera uma segunda consulta começar (até 3 s, abaixo do timeout de 5 s): com o pool de threads
                    // ocupado por outros testes, um Sleep fixo podia terminar antes de a segunda thread entrar.
                    SpinWait.SpinUntil(() => Volatile.Read(ref maximo) > 1, TimeSpan.FromSeconds(3));
                    Thread.Sleep(50);
                    Interlocked.Decrement(ref simultaneos);
                    return !f.EndsWith("p3.exe");
                },
                _ => true, TimeSpan.FromSeconds(5), new ProgressoDireto(avisos.Enqueue), CancellationToken.None);

            Assert.Equal(8, resultados.Count);
            Assert.Equal(8, avisos.Count);
            Assert.Equal(StatusArquivo.NaoEncontrado, resultados.Single(r => r.Alvo.Programa.Id == "p3").Status);
            Assert.Equal(7, resultados.Count(r => r.Encontrado));
            Assert.True(maximo > 1, "As consultas à rede deveriam rodar em paralelo.");
        }

        [Fact]
        public async Task VerificarCaminhos_PastaBaseForaDoArNaoConsultaCadaArquivo()
        {
            var consultas = 0;
            var alvos = new[] { "a.exe", "b.exe", "c.exe" }
                .Select(n => ConfigLoader.CaminhoNaRede(new Programa { Id = n, Instalador = n }, Base)).ToList();

            var resultados = await ConfigLoader.VerificarCaminhosAsync(alvos, Base,
                _ => { Interlocked.Increment(ref consultas); return true; },
                _ => false, TimeSpan.FromSeconds(5), null, CancellationToken.None);

            Assert.Equal(0, consultas);
            Assert.All(resultados, r =>
            {
                Assert.Equal(StatusArquivo.NaoEncontrado, r.Status);
                Assert.True(r.PastaBaseInacessivel);
            });
        }

        // ===== ViewModel: ✔ / ❌ e bloqueio da seleção =====

        [Fact]
        public void AbrirLaboratorio_MostraEncontradosENaoEncontradosEBloqueiaOsQueFaltam()
        {
            var (vm, _, _) = Criar(Menos("python-setup.exe"));
            vm.AguardarAutodescoberta();

            var vscode = vm.Programas.Single(p => p.Programa.Id == "vscode");
            var python = vm.Programas.Single(p => p.Programa.Id == "python");

            Assert.True(vscode.IsAvailable);
            Assert.True(vscode.Encontrado);
            Assert.True(vscode.PodeSelecionar);
            Assert.Equal("Encontrado na rede", vscode.StatusBusca);
            Assert.StartsWith(Base, vscode.CaminhoNaRede);

            Assert.False(python.IsAvailable);
            Assert.True(python.NaoEncontrado);
            Assert.False(python.PodeSelecionar);
            Assert.StartsWith("Não encontrado na rede: ", python.StatusBusca);
            Assert.EndsWith("python-setup.exe", python.StatusBusca);

            // A checkbox do Python não marca, nem pelo clique nem pelo "Selecionar Todos".
            python.Selecionado = true;
            Assert.False(python.Selecionado);
            vm.SelecionarTodosCommand.Execute(null);
            Assert.Equal(2, vm.TotalSelecionados);
            Assert.False(python.Selecionado);

            Assert.Equal(2, vm.TotalEncontrados);
            Assert.Equal(1, vm.TotalNaoEncontrados);
            Assert.Equal("2 de 3 encontrado(s) na rede · 1 não encontrado(s)", vm.ResumoDisponibilidade);
            Assert.Contains(vm.Log, l => l.Contains("não encontrado(s) na rede: Python"));
        }

        [Fact]
        public void EnquantoBusca_ItensFicamEmBuscandoENaoMarcam()
        {
            using (var liberar = new ManualResetEventSlim())
            {
                var (vm, _, _) = Criar(_ => { liberar.Wait(TimeSpan.FromSeconds(10)); return true; });

                Assert.True(vm.VerificandoArquivos);
                Assert.Equal("Procurando os instaladores na rede…", vm.ResumoDisponibilidade);
                Assert.All(vm.Programas, p =>
                {
                    Assert.Null(p.IsAvailable);
                    Assert.True(p.Buscando);
                    Assert.Equal(ProgramaItemViewModel.TextoBuscando, p.StatusBusca);
                });
                vm.Programas[0].Selecionado = true;
                Assert.False(vm.Programas[0].Selecionado);
                Assert.False(vm.SelecionarTodosCommand.CanExecute(null));

                liberar.Set();
                vm.AguardarAutodescoberta();

                Assert.False(vm.VerificandoArquivos);
                Assert.All(vm.Programas, p => Assert.True(p.IsAvailable));
                Assert.True(vm.SelecionarTodosCommand.CanExecute(null));
            }
        }

        [Fact]
        public void TrocarAPastaDeRede_ConfereDeNovoEDesmarcaOQueSumiu()
        {
            var novaBase = Path.Combine(Path.GetTempPath(), "winallapp-rede-falsa", "Outra Pasta");
            // Na pasta nova só existe o VS Code.
            var (vm, _, _) = Criar(f => !f.StartsWith(Path.GetFullPath(novaBase)) || f.EndsWith("VSCodeSetup.exe"));
            vm.AtrasoAposDigitar = TimeSpan.FromMilliseconds(200);
            vm.AguardarAutodescoberta();
            vm.SelecionarTodosCommand.Execute(null);
            Assert.Equal(3, vm.TotalSelecionados);

            vm.PastaRede = novaBase;
            Assert.All(vm.Programas, p => Assert.True(p.Buscando));
            vm.AguardarAutodescoberta();

            Assert.True(vm.Programas.Single(p => p.Programa.Id == "vscode").Encontrado);
            Assert.All(vm.Programas.Where(p => p.Programa.Id != "vscode"), p =>
            {
                Assert.True(p.NaoEncontrado);
                Assert.False(p.Selecionado);
                Assert.Contains("Outra Pasta", p.StatusBusca);
            });
            Assert.Equal(1, vm.TotalSelecionados);
        }

        [Fact]
        public void DigitarNoCampo_SoConfereOCaminhoFinal()
        {
            var consultados = new ConcurrentQueue<string>();
            var (vm, _, _) = Criar(f => { consultados.Enqueue(f); return true; });
            vm.AguardarAutodescoberta();
            while (consultados.TryDequeue(out _)) { }
            vm.AtrasoAposDigitar = TimeSpan.FromMilliseconds(300);

            foreach (var parcial in new[] { @"C:\L", @"C:\La", @"C:\Lab" }) vm.PastaRede = parcial;
            vm.AguardarAutodescoberta();

            Assert.Equal(3, consultados.Count); // 3 programas, só no caminho final
            Assert.All(consultados, c => Assert.Contains("Lab", c));
        }

        [Fact]
        public void RedeForaDoAr_ViraXEmTodosSemEsperarCadaArquivo()
        {
            using (var nuncaResponde = new ManualResetEventSlim())
            {
                var consultasArquivo = 0;
                var (vm, _, _) = Criar(_ => { Interlocked.Increment(ref consultasArquivo); return true; },
                    _ => { nuncaResponde.Wait(TimeSpan.FromSeconds(30)); return true; }, abrirLaboratorio: false);
                vm.TimeoutPorArquivo = TimeSpan.FromMilliseconds(200);
                var relogio = System.Diagnostics.Stopwatch.StartNew();

                vm.BlocoSelecionado = vm.Blocos[0];
                vm.LaboratorioSelecionado = vm.Laboratorios[0];
                vm.AguardarAutodescoberta();
                relogio.Stop();
                nuncaResponde.Set();

                Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), $"Demorou {relogio.Elapsed}.");
                Assert.Equal(0, consultasArquivo);
                Assert.All(vm.Programas, p =>
                {
                    Assert.True(p.NaoEncontrado);
                    Assert.StartsWith("A pasta de rede não respondeu em 1 s", p.StatusBusca);
                });
                Assert.False(vm.SelecionarTodosCommand.CanExecute(null));
            }
        }

        [Fact]
        public void TrocarDeLaboratorioDuranteABusca_ResultadoAntigoNaoVale()
        {
            using (var liberar = new ManualResetEventSlim())
            {
                // LCC demora; MAT responde na hora.
                var (vm, _, _) = Criar(f => { if (!f.EndsWith(".msi") && !f.EndsWith("octave-setup.exe")) liberar.Wait(TimeSpan.FromSeconds(10)); return true; });
                var buscaDoLcc = vm.VerificacaoArquivos;
                var itensDoLcc = vm.Programas.ToList();

                vm.BlocoSelecionado = vm.Blocos[1];
                vm.LaboratorioSelecionado = vm.Laboratorios[0];
                vm.AguardarAutodescoberta();
                Assert.All(vm.Programas, p => Assert.True(p.Encontrado));

                liberar.Set();
                Assert.True(buscaDoLcc.Wait(TimeSpan.FromSeconds(10)));
                Assert.All(itensDoLcc, p => Assert.True(p.Buscando)); // a busca cancelada não aplicou nada
                Assert.False(vm.VerificandoArquivos);
            }
        }

        [Fact]
        public void GerenciadorComWinget_ForaDaRedeContinuaDisponivel()
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var carga = ConfigLoader.CarregarTexto(@"{
                ""pastaInstaladores"": ""rede"",
                ""blocos"": [{ ""id"": ""BL1"", ""laboratorios"": [{ ""id"": ""L1"", ""programas"": [""git"", ""vs"", ""geo"", ""port""] }] }],
                ""programas"": [
                  { ""id"": ""git"", ""nome"": ""Git"", ""categoria"": ""gerenciador"", ""instalador"": ""git.exe"", ""argumentos"": ""/S"", ""wingetId"": ""Git.Git"" },
                  { ""id"": ""vs"", ""nome"": ""VS 2022"", ""categoria"": ""gerenciador"", ""wingetId"": ""Microsoft.VisualStudio.2022.Community"" },
                  { ""id"": ""geo"", ""nome"": ""GeoGebra"", ""categoria"": ""offline_gratuito"", ""instalador"": ""geo.msi"" },
                  { ""id"": ""port"", ""nome"": ""Portátil"", ""categoria"": ""copia_pasta"", ""instalador"": ""Portatil"" }
                ]}", Base);
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            var contexto = new ContextoInstalacao(Base, null, AmbienteSistema.Simular("10"))
            {
                ArquivoExiste = _ => false,
                PastaExiste = d => !d.EndsWith("Portatil")
            };
            var vm = new MainViewModel(new LabCatalog(carga.Config), new InstallQueue(new RunnerFalso(), new CopiadorPastas(), new RoteadorInstalacao(contexto)));
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.AguardarAutodescoberta();

            var git = vm.Programas.Single(p => p.Programa.Id == "git");
            Assert.True(git.Encontrado);
            Assert.Contains("winget", git.StatusBusca);
            Assert.True(vm.Programas.Single(p => p.Programa.Id == "vs").Encontrado);
            Assert.True(vm.Programas.Single(p => p.Programa.Id == "geo").NaoEncontrado);
            var portatil = vm.Programas.Single(p => p.Programa.Id == "port");
            Assert.True(portatil.NaoEncontrado);
            Assert.StartsWith("Pasta não encontrada na rede: ", portatil.StatusBusca);
        }

        [Fact]
        public void Verificacao_NaoAlteraOConfig()
        {
            var carga = Dados.CarregarConfigTeste();
            var antes = carga.Config.Programas.Select(p => p.Instalador + "|" + p.Argumentos).ToList();
            var pastaAntes = carga.Config.PastaInstaladores;
            SynchronizationContext.SetSynchronizationContext(null);
            var fila = new InstallQueue(new RunnerFalso(), Base);
            fila.Roteador.Contexto.ArquivoExiste = _ => false;
            var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.AguardarAutodescoberta();

            Assert.All(vm.Programas, p => Assert.True(p.NaoEncontrado));
            Assert.Equal(antes, carga.Config.Programas.Select(p => p.Instalador + "|" + p.Argumentos));
            Assert.Equal(pastaAntes, carga.Config.PastaInstaladores);
        }

        [Fact]
        public void Verificacao_NaoRodaNaThreadDaTelaEAvisaNelaCadaVeredito()
        {
            var threadsDaConsulta = new ConcurrentBag<int>();
            var threadsDoAviso = new ConcurrentBag<int>();
            using (var liberar = new ManualResetEventSlim())
            using (var tela = new ThreadDeTela())
            {
                var (vm, _, _) = Criar(_ =>
                {
                    threadsDaConsulta.Add(Environment.CurrentManagedThreadId);
                    liberar.Wait(TimeSpan.FromSeconds(10));
                    return true;
                }, abrirLaboratorio: false);

                tela.Executar(() =>
                {
                    vm.BlocoSelecionado = vm.Blocos[0];
                    vm.LaboratorioSelecionado = vm.Laboratorios[0];
                    foreach (var p in vm.Programas)
                        p.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(ProgramaItemViewModel.IsAvailable)) threadsDoAviso.Add(Environment.CurrentManagedThreadId); };
                });

                // Com a rede "pendurada", a tela continua respondendo.
                Assert.True(tela.Consultar(() => vm.VerificandoArquivos));
                Assert.True(tela.Consultar(() => vm.Programas.All(p => p.Buscando)));

                liberar.Set();
                Assert.True(SpinWait.SpinUntil(() => tela.Consultar(() => !vm.VerificandoArquivos), TimeSpan.FromSeconds(10)));
                Assert.True(tela.Consultar(() => vm.Programas.All(p => p.Encontrado)));
                Assert.NotEmpty(threadsDaConsulta);
                Assert.DoesNotContain(tela.IdDaThread, threadsDaConsulta);
                Assert.All(threadsDoAviso, id => Assert.Equal(tela.IdDaThread, id)); // ✔ muda na thread da tela
            }
        }

        private static void InterlockedMax(ref int alvo, int valor)
        {
            int atual;
            while ((atual = Volatile.Read(ref alvo)) < valor && Interlocked.CompareExchange(ref alvo, valor, atual) != atual) { }
        }

        private sealed class ProgressoDireto : IProgress<ResultadoVerificacao>
        {
            private readonly Action<ResultadoVerificacao> _acao;
            public ProgressoDireto(Action<ResultadoVerificacao> acao) => _acao = acao;
            public void Report(ResultadoVerificacao value) => _acao(value);
        }
    }
}
