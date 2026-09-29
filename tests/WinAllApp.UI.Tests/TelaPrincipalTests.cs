using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.UI.Tests
{
    /// <summary>
    /// Abre a janela WPF de verdade (XAML embutido no WinAllApp.exe), opera os controles como um usuário
    /// e confere o filtro por laboratório e a fila de instalação. Salva capturas da tela em TestResults.
    /// </summary>
    public class TelaPrincipalTests
    {
        private sealed class RunnerFalso : IProcessRunner
        {
            public ConcurrentQueue<InstallCommand> Comandos { get; } = new ConcurrentQueue<InstallCommand>();

            public async Task<int> ExecutarAsync(InstallCommand comando, TimeSpan timeout, CancellationToken cancelamento)
            {
                Comandos.Enqueue(comando);
                await Task.Delay(1500, cancelamento).ConfigureAwait(false);
                return comando.Arquivo.EndsWith("codeblocks-setup.exe", StringComparison.OrdinalIgnoreCase) ? 3010 : 0;
            }
        }

        [Fact]
        public void Usuario_EscolheBlocoELaboratorio_MarcaProgramasEInstala()
        {
            RodarEmSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                var carga = ConfigLoader.CarregarArquivo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "config.teste.json"));
                Assert.True(carga.Valido, string.Join("; ", carga.Erros));
                var runner = new RunnerFalso();
                var fila = new InstallQueue(runner, carga.PastaConfig) { VerificarArquivoExiste = false };
                var vm = new MainViewModel(new LabCatalog(carga.Config), fila);

                var janela = WindowFactory.CriarJanelaPrincipal(vm);
                janela.ShowActivated = false;
                janela.ShowInTaskbar = false;
                janela.Show();
                try
                {
                    Processar();
                    Capturar(janela, "01-inicial.png");

                    var comboBlocos = (ComboBox)janela.FindName("ComboBlocos");
                    var listaLabs = (ListBox)janela.FindName("ListaLaboratorios");
                    var listaProgramas = (ItemsControl)janela.FindName("ListaProgramas");
                    var selecionarTodos = (Button)janela.FindName("BotaoSelecionarTodos");
                    var instalar = (Button)janela.FindName("BotaoInstalar");

                    Assert.Equal(2, comboBlocos.Items.Count);
                    Assert.Empty(listaProgramas.Items);
                    Assert.False(instalar.IsEnabled);

                    // 1) Bloco BL2 → só Matemática aparece; ao abrir, 2 programas.
                    comboBlocos.SelectedIndex = 1;
                    Processar();
                    Assert.Single(listaLabs.Items);
                    listaLabs.SelectedIndex = 0;
                    Processar();
                    Assert.Equal(new[] { "GeoGebra", "GNU Octave" }, CheckBoxes(listaProgramas).Select(Rotulo));

                    // 2) Bloco BL1 → LCC com 3 programas, cada um com a sua checkbox.
                    comboBlocos.SelectedIndex = 0;
                    Processar();
                    listaLabs.SelectedIndex = 0;
                    Processar(() => vm.Programas.All(p => !p.Buscando), TimeSpan.FromSeconds(10));
                    var caixas = CheckBoxes(listaProgramas);
                    Assert.All(caixas, c => Assert.True(c.IsEnabled)); // os 3 instaladores foram encontrados (✔)
                    Assert.Equal(new[] { "Visual Studio Code", "Python", "Code::Blocks" }, caixas.Select(Rotulo));
                    Assert.All(caixas, c => Assert.False(c.IsChecked == true));

                    // 3) "Selecionar Todos do Laboratório" e desmarcar o Python na própria checkbox.
                    Assert.True(selecionarTodos.IsEnabled);
                    selecionarTodos.Command.Execute(null);
                    Processar();
                    Assert.All(CheckBoxes(listaProgramas), c => Assert.True(c.IsChecked == true));
                    CheckBoxes(listaProgramas)[1].IsChecked = false;
                    Processar();
                    Assert.Equal(2, vm.TotalSelecionados);
                    Assert.True(instalar.IsEnabled);
                    Capturar(janela, "02-lcc-selecionado.png");

                    // 4) Instalar: a janela continua respondendo enquanto a fila roda.
                    instalar.Command.Execute(null);
                    Processar();
                    Assert.True(vm.Ocupado);
                    Assert.False(listaProgramas.IsEnabled);
                    Assert.True(((FrameworkElement)janela.FindName("SpinnerInstalacao")).IsVisible);
                    Capturar(janela, "03-instalando.png");

                    Processar(() => !vm.Ocupado, TimeSpan.FromSeconds(20));
                    Assert.False(vm.Ocupado);

                    var comandos = runner.Comandos.ToList();
                    Assert.Equal(2, comandos.Count);
                    Assert.EndsWith("VSCodeSetup.exe", comandos[0].Arquivo);
                    Assert.Equal("/VERYSILENT /NORESTART", comandos[0].Argumentos);
                    Assert.EndsWith("codeblocks-setup.exe", comandos[1].Arquivo);
                    Assert.Equal("/S", comandos[1].Argumentos);
                    Assert.Equal(EstadoInstalacao.SucessoReiniciar, vm.Programas[2].Estado);
                    Assert.False(((FrameworkElement)janela.FindName("SpinnerInstalacao")).IsVisible);
                    Assert.Equal("2 de 2 concluído(s) · 2 instalado(s) · 0 falha(s)", ((TextBlock)janela.FindName("TextoContador")).Text);
                    Capturar(janela, "04-concluido.png");
                }
                finally
                {
                    janela.Close();
                }
            });
        }

        [Fact]
        public void Onboarding_TresPassosComLogo_FechaAoConcluir()
        {
            RodarEmSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var preferencias = new PreferenciasUsuario(Path.Combine(Path.GetTempPath(), "winallapp-ui-" + Guid.NewGuid().ToString("N"), "p.ini"));
                var vm = new OnboardingViewModel(@"\\servidor\instaladores", preferencias);
                var janela = WindowFactory.CriarOnboarding(vm);
                janela.ShowActivated = false;
                janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                janela.Show();
                try
                {
                    Processar();
                    var logo = (Image)janela.FindName("ImagemLogo");
                    var titulo = (TextBlock)janela.FindName("TextoTituloPasso");
                    var texto = (TextBlock)janela.FindName("TextoPasso");
                    var avancar = (Button)janela.FindName("BotaoAvancar");
                    var voltar = (Button)janela.FindName("BotaoVoltar");

                    Assert.NotNull(logo.Source);
                    Assert.True(logo.ActualWidth > 0 && logo.ActualHeight > 0);
                    Assert.Equal("1. Escolha o bloco e o laboratório", titulo.Text);
                    Assert.False(voltar.IsEnabled);
                    Assert.Equal("Avançar", avancar.Content);
                    Assert.True(texto.ActualHeight > 0);
                    Capturar(janela, "05-onboarding-passo1.png");

                    avancar.Command.Execute(null);
                    Processar();
                    Assert.Equal("2. Confira a pasta de rede", titulo.Text);
                    Assert.Contains(@"\\servidor\instaladores", texto.Text);
                    Assert.True(voltar.IsEnabled);
                    Capturar(janela, "06-onboarding-passo2.png");

                    avancar.Command.Execute(null);
                    Processar();
                    Assert.Equal("3. Instale em lote", titulo.Text);
                    Assert.Equal("Concluir", avancar.Content);
                    Capturar(janela, "07-onboarding-passo3.png");

                    avancar.Command.Execute(null);
                    Processar();
                    Assert.False(janela.IsVisible);
                    Assert.True(preferencias.OnboardingConcluido);
                }
                finally
                {
                    if (janela.IsVisible) janela.Close();
                }
            });
        }

        [Fact]
        public void TelaPrincipal_MostraLogoPastaDeRedeESistemaEAvisoDeWindows7()
        {
            RodarEmSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var args = new[] { "--simulacao", "--simular-windows", "7" };
                var carga = Program.CarregarConfig(args, out _);
                var vm = Program.CriarViewModel(carga, Program.CriarAmbiente(args, true, carga.PastaConfig));
                var janela = WindowFactory.CriarJanelaPrincipal(vm);
                janela.ShowActivated = false;
                janela.ShowInTaskbar = false;
                janela.Show();
                try
                {
                    vm.VerificarPastaRedeCommand.Execute(null);
                    Processar(() => vm.PastaRedeAcessivel.HasValue, TimeSpan.FromSeconds(10));
                    vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL1");
                    vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "WIN7");
                    Processar(() => !vm.VerificandoArquivos, TimeSpan.FromSeconds(10));
                    Assert.All(vm.Programas, p => Assert.True(p.Encontrado, p.StatusBusca)); // .bat da simulação existem

                    Assert.NotNull(((Image)janela.FindName("ImagemLogo")).Source);
                    Assert.True(vm.PastaRedeAcessivel == true, vm.PastaRedeStatus);
                    Assert.StartsWith("Pasta de rede acessível", ((TextBlock)janela.FindName("TextoPastaRede")).Text);
                    Assert.Contains("Windows 7", ((TextBlock)janela.FindName("TextoAmbiente")).Text);
                    Assert.True(((Button)janela.FindName("BotaoTutorial")).IsEnabled);
                    Assert.Contains(vm.Programas, p => p.TemAvisoCompatibilidade);
                    Capturar(janela, "08-windows7-avisos.png");
                }
                finally
                {
                    janela.Close();
                }
            });
        }

        /// <summary>
        /// Igual ao .exe: o ViewModel nasce SEM contexto de UI (Program.Main cria antes de app.Run) e o contexto do WPF
        /// só existe depois. Confere o campo de rede, a pesquisa (sem perder a marcação), o spinner, o contador e os
        /// alertas vermelhos de uma falha, tudo com a janela respondendo.
        /// </summary>
        [Fact]
        public void Usuario_PesquisaInstalaComFalhaEVeOContadorEOAlerta()
        {
            RodarEmSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(null);
                var carga = ConfigLoader.CarregarArquivo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "config.teste.json"));
                var runner = new RunnerComFalha();
                var fila = new InstallQueue(runner, ContextoInstalacao.PastaRedePadrao) { VerificarArquivoExiste = false };
                var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                var janela = WindowFactory.CriarJanelaPrincipal(vm);
                janela.ShowActivated = false;
                janela.ShowInTaskbar = false;
                janela.Show();
                try
                {
                    var campoRede = (TextBox)janela.FindName("CampoPastaRede");
                    var pesquisa = (TextBox)janela.FindName("CampoPesquisa");
                    var listaProgramas = (ItemsControl)janela.FindName("ListaProgramas");
                    var faixaErro = (FrameworkElement)janela.FindName("FaixaErro");
                    var spinnerRede = (FrameworkElement)janela.FindName("SpinnerRede");

                    // Campo de rede com o caminho do campus (espaços e acentos preservados).
                    Assert.Equal(ContextoInstalacao.PastaRedePadrao, campoRede.Text);

                    // Verificar a rede roda em segundo plano: spinner girando e a janela processando mensagens.
                    fila.Roteador.Contexto.PastaExiste = _ => { Thread.Sleep(1500); return false; };
                    ((Button)janela.FindName("BotaoVerificarRede")).Command.Execute(null);
                    Processar();
                    Assert.True(spinnerRede.IsVisible);
                    Processar(() => vm.PastaRedeAcessivel.HasValue, TimeSpan.FromSeconds(10));
                    Assert.False(spinnerRede.IsVisible);
                    Assert.True(faixaErro.IsVisible);
                    Assert.Contains("Não foi possível acessar a pasta de rede", ((TextBlock)janela.FindName("TextoErro")).Text);
                    Assert.StartsWith("Pasta de rede INACESSÍVEL", ((TextBlock)janela.FindName("TextoPastaRede")).Text);
                    Capturar(janela, "09-rede-inacessivel.png");

                    // Editar o campo vale para a instalação.
                    campoRede.Text = @"\\10.50.11.2\informatica\Outra Pasta";
                    Processar();
                    Assert.Equal(@"\\10.50.11.2\informatica\Outra Pasta", fila.Roteador.Contexto.PastaInstaladores);
                    Assert.False(faixaErro.IsVisible);
                    campoRede.Text = ContextoInstalacao.PastaRedePadrao;
                    fila.Roteador.Contexto.PastaExiste = _ => true; // a rede voltou

                    vm.BlocoSelecionado = vm.Blocos[0];
                    vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
                    Processar(() => !vm.VerificandoArquivos && vm.Programas.All(p => p.Encontrado), TimeSpan.FromSeconds(10));
                    Assert.All(vm.Programas, p => Assert.True(p.Encontrado, p.StatusBusca));

                    // Marca o Python, filtra por "code": ele some da lista mas continua marcado.
                    CheckBoxes(listaProgramas).Single(c => Rotulo(c) == "Python").IsChecked = true;
                    pesquisa.Text = "code";
                    Processar();
                    Assert.Equal(new[] { "Visual Studio Code", "Code::Blocks" }, CheckBoxes(listaProgramas).Select(Rotulo));
                    Assert.Equal("Selecionar os exibidos", ((Button)janela.FindName("BotaoSelecionarTodos")).Content);
                    ((Button)janela.FindName("BotaoSelecionarTodos")).Command.Execute(null);
                    Processar();
                    Assert.Equal(3, vm.TotalSelecionados);
                    Capturar(janela, "10-pesquisa.png");

                    pesquisa.Text = "pyth";
                    Processar();
                    Assert.True(Assert.Single(CheckBoxes(listaProgramas)).IsChecked == true);
                    ((Button)janela.FindName("BotaoLimparPesquisa")).Command.Execute(null);
                    Processar();
                    Assert.Equal(string.Empty, pesquisa.Text);
                    Assert.All(CheckBoxes(listaProgramas), c => Assert.True(c.IsChecked == true));

                    // Instalar: spinner no rodapé e na linha em andamento; contador avança; VS Code falha.
                    ((Button)janela.FindName("BotaoInstalar")).Command.Execute(null);
                    Processar(() => vm.Concluidos >= 1, TimeSpan.FromSeconds(10));
                    Assert.True(vm.Ocupado);
                    Assert.True(((FrameworkElement)janela.FindName("SpinnerInstalacao")).IsVisible);
                    Assert.Contains(" de 3 concluído(s)", ((TextBlock)janela.FindName("TextoContador")).Text);
                    Capturar(janela, "11-instalando-spinner.png");

                    Processar(() => !vm.Ocupado, TimeSpan.FromSeconds(30));
                    Assert.False(vm.Ocupado);
                    Assert.Equal("3 de 3 concluído(s) · 2 instalado(s) · 1 falha(s)", ((TextBlock)janela.FindName("TextoContador")).Text);
                    Assert.True(faixaErro.IsVisible);
                    Assert.Contains("1 programa(s) falharam: Visual Studio Code", ((TextBlock)janela.FindName("TextoErro")).Text);
                    Assert.True(vm.Programas[0].TemFalha);
                    Assert.DoesNotContain(vm.Log, l => l.Contains("Erro inesperado"));
                    Assert.Equal(3, runner.Comandos.Count);
                    Assert.All(runner.Comandos, c => Assert.StartsWith(ContextoInstalacao.PastaRedePadrao, c.CaminhoInstalador));
                    Capturar(janela, "12-falha-alerta.png");
                }
                finally
                {
                    janela.Close();
                }
            });
        }

        /// <summary>
        /// Autodescoberta na janela real: com a rede lenta a lista mostra "procurando" (spinner) e a janela continua
        /// respondendo; depois cada linha ganha ✔ verde (checkbox habilitada) ou ❌ vermelho (checkbox bloqueada).
        /// </summary>
        [Fact]
        public void Autodescoberta_MostraBuscandoDepoisCheckOuXEBloqueiaOQueFalta()
        {
            RodarEmSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(null); // como no .exe: ViewModel antes do loop da tela
                var carga = ConfigLoader.CarregarArquivo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "config.teste.json"));
                var fila = new InstallQueue(new RunnerFalso(), ContextoInstalacao.PastaRedePadrao);
                var liberar = new ManualResetEventSlim();
                fila.Roteador.Contexto.PastaExiste = _ => true;
                fila.Roteador.Contexto.ArquivoExiste = f =>
                {
                    liberar.Wait(TimeSpan.FromSeconds(20)); // servidor lento
                    return !f.EndsWith("python-setup.exe", StringComparison.OrdinalIgnoreCase);
                };
                var vm = new MainViewModel(new LabCatalog(carga.Config), fila);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                var janela = WindowFactory.CriarJanelaPrincipal(vm);
                janela.ShowActivated = false;
                janela.ShowInTaskbar = false;
                janela.Show();
                try
                {
                    var listaProgramas = (ItemsControl)janela.FindName("ListaProgramas");
                    var spinnerDescoberta = (FrameworkElement)janela.FindName("SpinnerDescoberta");
                    var textoDisponibilidade = (TextBlock)janela.FindName("TextoDisponibilidade");
                    var selecionarTodos = (Button)janela.FindName("BotaoSelecionarTodos");

                    vm.BlocoSelecionado = vm.Blocos[0];
                    vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");

                    // Rede lenta: a janela processa mensagens, mostra "procurando" e nada pode ser marcado ainda.
                    var inicio = DateTime.UtcNow;
                    Processar();
                    Assert.True(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(3), "A janela travou durante a busca na rede.");
                    Assert.True(spinnerDescoberta.IsVisible);
                    Assert.Equal("Procurando os instaladores na rede…", textoDisponibilidade.Text);
                    Assert.All(CheckBoxes(listaProgramas), c =>
                    {
                        Assert.False(c.IsEnabled);
                        Assert.True(Icone(c, "SpinnerBusca").IsVisible);
                    });
                    Assert.False(selecionarTodos.IsEnabled);
                    Capturar(janela, "13-autodescoberta-procurando.png");

                    liberar.Set();
                    Processar(() => !vm.VerificandoArquivos, TimeSpan.FromSeconds(15));
                    Processar();

                    Assert.False(spinnerDescoberta.IsVisible);
                    Assert.Equal("2 de 3 encontrado(s) na rede · 1 não encontrado(s)", textoDisponibilidade.Text);
                    foreach (var caixa in CheckBoxes(listaProgramas))
                    {
                        var item = (ProgramaItemViewModel)caixa.DataContext;
                        var achou = item.Programa.Id != "python";
                        Assert.Equal(achou, caixa.IsEnabled);
                        Assert.Equal(achou, Icone(caixa, "IconeEncontrado").IsVisible);
                        Assert.Equal(!achou, Icone(caixa, "IconeNaoEncontrado").IsVisible);
                        Assert.False(Icone(caixa, "SpinnerBusca").IsVisible);
                    }
                    var python = vm.Programas.Single(p => p.Programa.Id == "python");
                    Assert.StartsWith("Não encontrado na rede: " + ContextoInstalacao.PastaRedePadrao, python.StatusBusca);

                    // "Selecionar Todos" pula o que não foi encontrado.
                    Assert.True(selecionarTodos.IsEnabled);
                    selecionarTodos.Command.Execute(null);
                    Processar();
                    Assert.Equal(2, vm.TotalSelecionados);
                    Assert.False(python.Selecionado);
                    Assert.False(CheckBoxes(listaProgramas).Single(c => Rotulo(c) == "Python").IsChecked == true);
                    Capturar(janela, "14-autodescoberta-check-x.png");
                }
                finally
                {
                    liberar.Set();
                    janela.Close();
                }
            });
        }

        /// <summary>O ícone (✔, ❌ ou spinner) de uma linha, achado pelo estilo do XAML.</summary>
        private static FrameworkElement Icone(CheckBox caixa, string estilo)
        {
            var alvo = caixa.FindResource(estilo);
            var pilha = new Stack<DependencyObject>();
            pilha.Push(caixa);
            while (pilha.Count > 0)
            {
                var atual = pilha.Pop();
                if (atual is FrameworkElement fe && ReferenceEquals(fe.Style, alvo)) return fe;
                for (var i = VisualTreeHelper.GetChildrenCount(atual) - 1; i >= 0; i--) pilha.Push(VisualTreeHelper.GetChild(atual, i));
            }
            throw new InvalidOperationException("Ícone não encontrado: " + estilo);
        }

        private sealed class RunnerComFalha : IProcessRunner
        {
            public ConcurrentQueue<InstallCommand> Comandos { get; } = new ConcurrentQueue<InstallCommand>();

            public async Task<int> ExecutarAsync(InstallCommand comando, TimeSpan timeout, CancellationToken cancelamento)
            {
                Comandos.Enqueue(comando);
                await Task.Delay(1200, cancelamento).ConfigureAwait(false);
                return comando.Arquivo.EndsWith("VSCodeSetup.exe", StringComparison.OrdinalIgnoreCase) ? 1603 : 0;
            }
        }

        /// <summary>
        /// Tela "Padronizar Windows" em modo simulação (nada é executado no sistema): o técnico preenche nome, senha,
        /// perfil e impressora por IP, confirma e vê cada etapa do checklist terminar com ✔.
        /// </summary>
        [Fact]
        public void Padronizacao_PreencheOChecklistEExecutaEmSimulacao()
        {
            RodarEmSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

                var vm = Program.CriarPadronizacao(AmbienteSistema.Simular("11"), simulacao: true);
                string pergunta = null;
                vm.Confirmar = texto => { pergunta = texto; return true; };
                var janela = WindowFactory.CriarPadronizacao(vm);
                janela.ShowActivated = false;
                janela.ShowInTaskbar = false;
                janela.Show();
                try
                {
                    Processar();
                    var executar = (Button)janela.FindName("BotaoPadronizar");
                    Assert.False(executar.IsEnabled);
                    Assert.True(((FrameworkElement)janela.FindName("TextoErros")).IsVisible);
                    Assert.Contains("Windows 11", ((TextBlock)janela.FindName("TextoWindows")).Text);

                    ((ComboBox)janela.FindName("ComboBloco")).SelectedItem = "BL2";
                    ((TextBox)janela.FindName("CaixaLocal")).Text = "lia";
                    ((TextBox)janela.FindName("CaixaNumero")).Text = "7";
                    ((PasswordBox)janela.FindName("CaixaSenha")).Password = "Senha!2025";
                    ((RadioButton)janela.FindName("OpcaoBolsista")).IsChecked = true;
                    Processar();
                    Assert.Equal("BL2-LIA-07", vm.NomeComputador);
                    Assert.Equal("Senha!2025", vm.SenhaInformatica);
                    Assert.True(vm.PerfilBolsista);
                    Assert.True(executar.IsEnabled);

                    // Impressora marcada sem IP bloqueia; com IP válido libera.
                    var ip = (TextBox)janela.FindName("CaixaIpImpressora");
                    Assert.False(ip.IsEnabled);
                    ((CheckBox)janela.FindName("CaixaImpressora")).IsChecked = true;
                    Processar();
                    Assert.True(ip.IsEnabled);
                    Assert.False(executar.IsEnabled);
                    ip.Text = "10.50.12.34";
                    Processar();
                    Assert.True(executar.IsEnabled);
                    Assert.False(((FrameworkElement)janela.FindName("TextoErros")).IsVisible);
                    Capturar(janela, "15-padronizacao-formulario.png");

                    executar.Command.Execute(null);
                    Processar(() => vm.Etapas.Count > 0 && !vm.Executando, TimeSpan.FromSeconds(40));
                    Processar(() => vm.Etapas.All(e => e.Status == Core.Services.Padronizacao.StatusEtapa.Concluida), TimeSpan.FromSeconds(5));

                    Assert.Contains("4 Criar as contas Informatica e Bolsista", pergunta);
                    Assert.Contains("9.6 Adicionar a impressora 10.50.12.34", pergunta);
                    Assert.Equal(11, vm.Etapas.Count);
                    Assert.All(vm.Etapas, e => Assert.Equal(Core.Services.Padronizacao.StatusEtapa.Concluida, e.Status));
                    Assert.Equal("11 de 11 etapa(s) concluída(s). Reinicie o computador para aplicar o novo nome e as políticas.",
                        ((TextBlock)janela.FindName("TextoResumoFinal")).Text);
                    Assert.Equal(11, ((ListBox)janela.FindName("ListaEtapas")).Items.Count);
                    Capturar(janela, "16-padronizacao-concluida.png");
                }
                finally
                {
                    janela.Close();
                }
            });
        }

        private static void RodarEmSta(Action acao)
        {
            Exception erro = null;
            var thread = new Thread(() =>
            {
                try { acao(); }
                catch (Exception ex) { erro = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromMinutes(2))) throw new TimeoutException("Teste de UI não terminou.");
            if (erro != null) throw new Exception("Falha no teste de UI: " + erro.Message, erro);
        }

        /// <summary>Processa a fila do Dispatcher (bindings, layout e continuações async) como faria o loop da UI.</summary>
        private static void Processar(Func<bool> ate = null, TimeSpan? limite = null)
        {
            var fim = DateTime.UtcNow + (limite ?? TimeSpan.FromMilliseconds(300));
            do
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                if (ate != null && ate()) break;
                Thread.Sleep(15);
            } while (DateTime.UtcNow < fim);
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }

        private static List<CheckBox> CheckBoxes(DependencyObject raiz)
        {
            var lista = new List<CheckBox>();
            var pilha = new Stack<DependencyObject>();
            pilha.Push(raiz);
            while (pilha.Count > 0)
            {
                var atual = pilha.Pop();
                if (atual is CheckBox cb) lista.Add(cb);
                var n = VisualTreeHelper.GetChildrenCount(atual);
                for (var i = n - 1; i >= 0; i--) pilha.Push(VisualTreeHelper.GetChild(atual, i));
            }
            return lista;
        }

        private static string Rotulo(CheckBox caixa) => ((ProgramaItemViewModel)caixa.DataContext).Nome;

        private static void Capturar(Window janela, string arquivo)
        {
            try
            {
                var pasta = Environment.GetEnvironmentVariable("WINALLAPP_CAPTURAS")
                            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capturas");
                Directory.CreateDirectory(pasta);
                var conteudo = (FrameworkElement)janela.Content;
                var largura = (int)Math.Ceiling(conteudo.ActualWidth);
                var altura = (int)Math.Ceiling(conteudo.ActualHeight);
                if (largura == 0 || altura == 0) return;

                var bmp = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
                var fundo = new DrawingVisual();
                using (var dc = fundo.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, largura, altura));
                bmp.Render(fundo);
                bmp.Render(conteudo);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = File.Create(Path.Combine(pasta, arquivo))) png.Save(fs);
            }
            catch (Exception)
            {
                // Captura é só evidência visual; não deve derrubar o teste.
            }
        }
    }
}
