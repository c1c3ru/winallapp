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
                    Processar();
                    var caixas = CheckBoxes(listaProgramas);
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
                    Capturar(janela, "04-concluido.png");
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
