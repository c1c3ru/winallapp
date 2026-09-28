using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>Contador, pesquisa, campo da pasta de rede, alertas de erro e aspas no Process.Start.</summary>
    public class UxInstaladorTests
    {
        /// <summary>
        /// Sem contexto de sincronização (o do xUnit rodaria os avisos de andamento em paralelo): os avisos da fila
        /// chegam em ordem, na própria thread da fila. A thread de tela real é coberta por <see cref="ThreadDeTela"/>.
        /// </summary>
        private static (MainViewModel vm, RunnerFalso runner, InstallQueue fila) Criar(RunnerFalso runner = null)
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var carga = Dados.CarregarConfigTeste();
            runner = runner ?? new RunnerFalso();
            var fila = new InstallQueue(runner, Path.Combine(carga.PastaConfig, "instaladores")) { VerificarArquivoExiste = false };
            var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            vm.AguardarAutodescoberta();
            return (vm, runner, fila);
        }

        // ===== 1) Contador =====

        [Fact]
        public async Task Contador_MostraConcluidosDoTotalInstaladosEFalhas()
        {
            var (vm, _, _) = Criar(new RunnerFalso(c => c.Arquivo.EndsWith("VSCodeSetup.exe") ? 1603 : 0));
            Assert.False(vm.TemContador);

            vm.SelecionarTodosCommand.Execute(null);
            await vm.InstalarAsync();

            Assert.Equal(3, vm.TotalNaFila);
            Assert.Equal(3, vm.Concluidos);
            Assert.Equal(2, vm.Instalados);
            Assert.Equal(1, vm.Falhas);
            Assert.Equal("3 de 3 concluído(s) · 2 instalado(s) · 1 falha(s)", vm.ContadorTexto);
            Assert.Equal(100, vm.Progresso);
            Assert.StartsWith("Concluído: 2 instalado(s), 1 falha(s)", vm.StatusTexto);
        }

        [Fact]
        public async Task Contador_AvancaAItemPorItemDuranteAFila()
        {
            var runner = new RunnerFalso { Portao = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            var (vm, _, _) = Criar(runner);
            vm.Programas[0].Selecionado = true;
            vm.Programas[2].Selecionado = true;
            var contagens = new ConcurrentQueue<string>();
            vm.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MainViewModel.ContadorTexto)) contagens.Enqueue(vm.ContadorTexto); };

            var tarefa = vm.InstalarAsync();
            Assert.Equal("0 de 2 concluído(s) · 0 instalado(s) · 0 falha(s)", vm.ContadorTexto);
            await EsperarAsync(() => !runner.Comandos.IsEmpty);
            runner.Portao.SetResult(true);
            await tarefa;

            Assert.Contains("1 de 2 concluído(s) · 1 instalado(s) · 0 falha(s)", contagens);
            Assert.Equal("2 de 2 concluído(s) · 2 instalado(s) · 0 falha(s)", vm.ContadorTexto);
        }

        [Fact]
        public async Task Contador_ZeraAoTrocarDeLaboratorio()
        {
            var (vm, _, _) = Criar();
            vm.SelecionarTodosCommand.Execute(null);
            await vm.InstalarAsync();
            Assert.True(vm.TemContador);

            vm.BlocoSelecionado = vm.Blocos[1];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.AguardarAutodescoberta();

            Assert.False(vm.TemContador);
            Assert.Equal(string.Empty, vm.ContadorTexto);
        }

        /// <summary>
        /// Reproduz o .exe: o ViewModel nasce sem contexto de UI (Program.Main, antes de app.Run) e a instalação
        /// roda depois, dentro do loop da tela. Toda mudança de lista, item e contador tem de acontecer na thread da tela
        /// (senão o WPF lança NotSupportedException no Registro e o contador para em "0 instalado(s)").
        /// </summary>
        [Fact]
        public void ViewModelCriadoAntesDoLoopDaTela_AtualizaTudoNaThreadDaTela()
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var (vm, _, _) = Criar();
            vm.SelecionarTodosCommand.Execute(null);

            using (var tela = new ThreadDeTela())
            {
                var foraDaTela = new ConcurrentQueue<string>();
                tela.Executar(() =>
                {
                    vm.Log.CollectionChanged += (s, e) => { if (!tela.EstaNaThread) foraDaTela.Enqueue("Log"); };
                    vm.PropertyChanged += (s, e) => { if (!tela.EstaNaThread) foraDaTela.Enqueue(e.PropertyName); };
                    foreach (var item in vm.Programas)
                        item.PropertyChanged += (s, e) => { if (!tela.EstaNaThread) foraDaTela.Enqueue("item." + e.PropertyName); };
                });

                tela.Executar(() => vm.InstalarCommand.Execute(null));
                Assert.True(SpinWait.SpinUntil(() => tela.Consultar(() => vm.InstalarCommand.Execucao.IsCompleted && !vm.Ocupado),
                    TimeSpan.FromSeconds(10)), "a instalação não terminou");

                Assert.Empty(foraDaTela);
                Assert.Equal("3 de 3 concluído(s) · 3 instalado(s) · 0 falha(s)", tela.Consultar(() => vm.ContadorTexto));
                Assert.DoesNotContain(tela.Consultar(() => vm.Log.ToList()), l => l.Contains("Erro inesperado"));
            }
        }

        /// <summary>
        /// Contexto que roda cada Post em paralelo no pool (como o do xUnit): os avisos ainda têm de ser aplicados um de
        /// cada vez e em ordem, senão o Registro corrompe e um "Instalando" atrasado sobrescreve o "Instalado".
        /// </summary>
        [Fact]
        public void ContextoQueRodaPostsEmParalelo_AvisosSaemEmOrdemEOContadorFecha()
        {
            for (var rodada = 0; rodada < 40; rodada++)
            {
                SynchronizationContext.SetSynchronizationContext(null);
                var (vm, _, _) = Criar();
                vm.SelecionarTodosCommand.Execute(null);
                SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
                try
                {
                    vm.InstalarAsync().GetAwaiter().GetResult();
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                }

                Assert.All(vm.Programas, p => Assert.Equal(EstadoInstalacao.Sucesso, p.Estado));
                Assert.Equal("3 de 3 concluído(s) · 3 instalado(s) · 0 falha(s)", vm.ContadorTexto);
                Assert.DoesNotContain(null, vm.Log);
                Assert.Equal(1 + 3 * 2 + 1, vm.Log.Count); // início + (executando, instalado) x3 + resumo
            }
        }

        // ===== 2) Pasta de rede =====

        [Fact]
        public void PastaRede_ConfigRealTrazOCaminhoDoCampus()
        {
            var carga = ConfigLoader.CarregarArquivo(Path.Combine(Dados.Pasta, "app", "config.json"));
            Assert.Equal(ContextoInstalacao.PastaRedePadrao, carga.Config.PastaInstaladores);
            Assert.Equal(@"\\10.50.11.2\informatica\NAC - Núcleo de Atendimento ao Cliente\Programas\Laboratórios - Programas",
                ContextoInstalacao.PastaRedePadrao);

            var fila = new InstallQueue(new RunnerFalso(), new CopiadorPastas(),
                new RoteadorInstalacao(new ContextoInstalacao(carga.Config.PastaInstaladores, null, AmbienteSistema.Simular("10"))));
            SynchronizationContext.SetSynchronizationContext(null);
            var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
            Assert.Equal(ContextoInstalacao.PastaRedePadrao, vm.PastaRede);
        }

        [Fact]
        public async Task PastaRede_EditadaNaTelaValeParaAInstalacaoComAspasNoCaminho()
        {
            var (vm, runner, fila) = Criar();
            vm.PastaRede = "  \"" + ContextoInstalacao.PastaRedePadrao + "\"  ";

            Assert.Equal(ContextoInstalacao.PastaRedePadrao, fila.Roteador.Contexto.PastaInstaladores);
            Assert.Null(vm.PastaRedeAcessivel);
            Assert.Contains("alterada", vm.PastaRedeStatus);
            Assert.All(vm.Programas, p => Assert.True(p.Buscando)); // caminho novo: procura de novo os instaladores

            vm.AguardarAutodescoberta();
            vm.Programas[0].Selecionado = true;
            await vm.InstalarAsync();

            var comando = Assert.Single(runner.Comandos);
            Assert.Contains(ContextoInstalacao.PastaRedePadrao, comando.CaminhoInstalador);
            if (Dados.Windows) Assert.StartsWith("\"" + ContextoInstalacao.PastaRedePadrao, comando.ToString());
        }

        [Fact]
        public async Task PastaRede_Inacessivel_MostraAlertaVermelhoSemTravar()
        {
            var (vm, _, fila) = Criar();
            var liberar = new ManualResetEventSlim();
            fila.Roteador.Contexto.PastaExiste = _ => { liberar.Wait(TimeSpan.FromSeconds(10)); return false; };

            var verificacao = vm.VerificarPastaRedeAsync();
            Assert.True(vm.VerificandoPastaRede); // spinner ao lado do campo enquanto a rede responde
            Assert.False(vm.VerificarPastaRedeCommand.CanExecute(null));
            liberar.Set();
            await verificacao;

            Assert.False(vm.VerificandoPastaRede);
            Assert.False(vm.PastaRedeAcessivel);
            Assert.True(vm.TemErro);
            Assert.Contains("Não foi possível acessar a pasta de rede", vm.MensagemErro);

            fila.Roteador.Contexto.PastaExiste = _ => true;
            await vm.VerificarPastaRedeAsync();
            Assert.True(vm.PastaRedeAcessivel);
            Assert.False(vm.TemErro);
        }

        [Fact]
        public async Task PastaRede_ExcecaoDeAcessoViraAlerta()
        {
            var (vm, _, fila) = Criar();
            fila.Roteador.Contexto.PastaExiste = _ => throw new UnauthorizedAccessException("Acesso negado.");

            await vm.VerificarPastaRedeAsync();

            Assert.False(vm.PastaRedeAcessivel);
            Assert.True(vm.TemErro);
            Assert.Contains(vm.Log, l => l.Contains("Acesso negado."));
        }

        [Fact]
        public void PastaRede_VaziaAvisaSemVerificar()
        {
            var (vm, _, _) = Criar();
            vm.PastaRede = "   ";
            vm.VerificarPastaRedeCommand.Execute(null);
            Assert.False(vm.PastaRedeAcessivel);
            Assert.Contains("Informe o caminho", vm.MensagemErro);
        }

        // ===== 3) Pesquisa =====

        [Fact]
        public void Pesquisa_FiltraEmTempoRealSemPerderAMarcacao()
        {
            var (vm, _, _) = Criar();
            Assert.Equal(vm.Programas.Count, vm.ProgramasVisiveis.Count);
            var python = vm.Programas.Single(p => p.Nome == "Python");
            python.Selecionado = true;

            vm.SearchText = "code";
            Assert.Equal(new[] { "Visual Studio Code", "Code::Blocks" }, vm.ProgramasVisiveis.Select(p => p.Nome));
            Assert.DoesNotContain(python, vm.ProgramasVisiveis);
            Assert.Equal(1, vm.TotalSelecionados); // o Python escondido continua marcado
            Assert.Contains("2 exibido(s) pela pesquisa", vm.Subtitulo);
            Assert.Equal("Selecionar os exibidos", vm.TextoSelecionarTodos);

            vm.SearchText = "PYTHÓN"; // sem diferenciar maiúsculas nem acentos
            Assert.Same(python, Assert.Single(vm.ProgramasVisiveis));
            Assert.True(vm.ProgramasVisiveis[0].Selecionado);

            vm.LimparPesquisaCommand.Execute(null);
            Assert.Equal(string.Empty, vm.SearchText);
            Assert.Equal(3, vm.ProgramasVisiveis.Count);
            Assert.True(python.Selecionado);
            Assert.Equal("Selecionar Todos do Laboratório", vm.TextoSelecionarTodos);
        }

        [Fact]
        public void Pesquisa_SelecionarTodosMarcaSoOsExibidos()
        {
            var (vm, _, _) = Criar();
            vm.SearchText = "code";

            vm.SelecionarTodosCommand.Execute(null);

            Assert.Equal(2, vm.TotalSelecionados);
            Assert.False(vm.Programas.Single(p => p.Nome == "Python").Selecionado);
        }

        [Fact]
        public void Pesquisa_SemResultadoMostraAviso()
        {
            var (vm, _, _) = Criar();
            vm.SearchText = "matlab";

            Assert.Empty(vm.ProgramasVisiveis);
            Assert.True(vm.MostrarAvisoVazio);
            Assert.Contains("\"matlab\"", vm.TextoAvisoVazio);
            Assert.False(vm.SelecionarTodosCommand.CanExecute(null));
        }

        [Fact]
        public void Pesquisa_ContinuaAoTrocarDeLaboratorio()
        {
            var (vm, _, _) = Criar();
            vm.SearchText = "zzz";
            vm.BlocoSelecionado = vm.Blocos[1];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.AguardarAutodescoberta();
            Assert.Empty(vm.ProgramasVisiveis);
            vm.SearchText = string.Empty;
            Assert.Equal(vm.Programas.Count, vm.ProgramasVisiveis.Count);
        }

        [Fact]
        public async Task Pesquisa_InstalaTambemOsMarcadosQueEstaoFiltrados()
        {
            var (vm, runner, _) = Criar();
            vm.SelecionarTodosCommand.Execute(null);
            vm.SearchText = "python";

            await vm.InstalarAsync();

            Assert.Equal(3, runner.Comandos.Count);
        }

        // ===== 4) Responsividade =====

        [Fact]
        public void Instalar_RodaAFilaForaDaThreadQueChamou()
        {
            var threadDaFila = 0;
            var runner = new RunnerFalso(c => { threadDaFila = Environment.CurrentManagedThreadId; return 0; });
            var (vm, _, fila) = Criar(runner);
            var threadDoPlano = 0;
            fila.Roteador.Contexto.ArquivoExiste = f => { threadDoPlano = Environment.CurrentManagedThreadId; return true; };
            vm.Programas[0].Selecionado = true;

            using (var tela = new ThreadDeTela())
            {
                tela.Executar(() => vm.InstalarCommand.Execute(null));
                Assert.True(SpinWait.SpinUntil(() => tela.Consultar(() => !vm.Ocupado && vm.InstalarCommand.Execucao.IsCompleted),
                    TimeSpan.FromSeconds(10)));
                Assert.NotEqual(tela.IdDaThread, threadDoPlano); // checar o arquivo na rede não trava a tela
                Assert.NotEqual(0, threadDaFila);
            }
        }

        // ===== 5) Alertas =====

        [Fact]
        public async Task Falha_MarcaOItemEmVermelhoEMostraOAlerta()
        {
            var (vm, _, _) = Criar(new RunnerFalso(c => c.Arquivo.EndsWith("VSCodeSetup.exe") ? 1603 : 0));
            vm.SelecionarTodosCommand.Execute(null);

            await vm.InstalarAsync();

            var vscode = vm.Programas.Single(p => p.Nome == "Visual Studio Code");
            Assert.True(vscode.TemFalha);
            Assert.True(vscode.MostrarMensagemErro);
            Assert.Contains("1603", vscode.Mensagem);
            Assert.False(vm.Programas.Single(p => p.Nome == "Python").TemFalha);
            Assert.True(vm.TemErro);
            Assert.Contains("1 programa(s) falharam: Visual Studio Code", vm.MensagemErro);

            vm.FecharErroCommand.Execute(null);
            Assert.False(vm.TemErro);
        }

        [Fact]
        public async Task InstaladorQueNaoAbre_ViraFalhaComMotivo()
        {
            SynchronizationContext.SetSynchronizationContext(null);
            var runner = new RunnerQueLanca(new System.ComponentModel.Win32Exception(5, "Acesso negado"));
            var carga = Dados.CarregarConfigTeste();
            var fila = new InstallQueue(runner, Path.Combine(carga.PastaConfig, "instaladores")) { VerificarArquivoExiste = false };
            var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            vm.AguardarAutodescoberta();
            vm.Programas[1].Selecionado = true;

            await vm.InstalarAsync();

            Assert.True(vm.Programas[1].TemFalha);
            Assert.Equal("Acesso negado", vm.Programas[1].Mensagem);
            Assert.Equal(1, vm.Falhas);
            Assert.True(vm.TemErro);
        }

        [Fact]
        public async Task ErroInesperadoNaFila_MarcaOsItensEAlerta()
        {
            var (vm, _, _) = Criar();
            vm.Programas[0].Selecionado = true;
            vm.Programas[1].Selecionado = true;
            var primeira = true;
            // O 1º aviso de andamento explode dentro da fila (simula um erro fora do instalador).
            vm.Programas[0].PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ProgramaItemViewModel.Estado) && primeira && vm.Programas[0].Estado == EstadoInstalacao.Instalando)
                {
                    primeira = false;
                    throw new InvalidOperationException("falha simulada");
                }
            };

            await vm.InstalarAsync();

            Assert.False(vm.Ocupado);
            Assert.True(vm.TemErro);
            Assert.Contains("falha simulada", vm.MensagemErro);
            Assert.All(vm.Programas.Where(p => p.Selecionado), p => Assert.True(p.Concluido));
        }

        // ===== Aspas no Process.Start =====

        [Theory]
        [InlineData(@"\\10.50.11.2\informatica\NAC - Núcleo\setup.exe", "\"\\\\10.50.11.2\\informatica\\NAC - Núcleo\\setup.exe\"")]
        [InlineData("\"C:\\Program Files\\x.exe\"", "\"C:\\Program Files\\x.exe\"")]
        [InlineData("  cmd.exe ", "\"cmd.exe\"")]
        public void ComAspas_EnvolveOCaminhoUmaVez(string caminho, string esperado) =>
            Assert.Equal(esperado, ProcessRunner.ComAspas(caminho));

        [Fact]
        public void CriarInfo_NoWindowsUsaOCaminhoEntreAspas()
        {
            var comando = new InstallCommand(@"\\10.50.11.2\a b\setup.exe", "/S", @"\\10.50.11.2\a b\setup.exe");
            var info = ProcessRunner.CriarInfo(comando);
            Assert.Equal(Dados.Windows ? "\"\\\\10.50.11.2\\a b\\setup.exe\"" : comando.Arquivo, info.FileName);
            Assert.Equal("/S", info.Arguments);
            Assert.False(info.UseShellExecute);
        }

        private static async Task EsperarAsync(Func<bool> condicao)
        {
            var limite = DateTime.UtcNow.AddSeconds(10);
            while (!condicao() && DateTime.UtcNow < limite) await Task.Delay(10);
            Assert.True(condicao());
        }

        private sealed class RunnerQueLanca : IProcessRunner
        {
            private readonly Exception _erro;
            public RunnerQueLanca(Exception erro) => _erro = erro;
            public Task<int> ExecutarAsync(InstallCommand comando, TimeSpan timeout, CancellationToken cancelamento) => throw _erro;
        }
    }

    /// <summary>Uma "thread de tela" de mentira: um loop com SynchronizationContext próprio, como o Dispatcher do WPF.</summary>
    public sealed class ThreadDeTela : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback, object)> _fila = new BlockingCollection<(SendOrPostCallback, object)>();
        private readonly Thread _thread;

        public ThreadDeTela()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (acao, estado) in _fila.GetConsumingEnumerable()) acao(estado);
            }) { IsBackground = true };
            _thread.Start();
        }

        public int IdDaThread => _thread.ManagedThreadId;
        public bool EstaNaThread => Thread.CurrentThread == _thread;

        public override void Post(SendOrPostCallback d, object state) => _fila.Add((d, state));

        public override void Send(SendOrPostCallback d, object state) => Executar(() => d(state));

        public void Executar(Action acao) => Consultar(() => { acao(); return true; });

        public T Consultar<T>(Func<T> consulta)
        {
            if (EstaNaThread) return consulta();
            var resultado = new TaskCompletionSource<T>();
            Post(_ =>
            {
                try { resultado.SetResult(consulta()); }
                catch (Exception ex) { resultado.SetException(ex); }
            }, null);
            return resultado.Task.GetAwaiter().GetResult();
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Dispose()
        {
            _fila.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}
