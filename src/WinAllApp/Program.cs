using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;

namespace WinAllApp
{
    public static class Program
    {
        /// <summary>
        /// Uso: WinAllApp.exe [--config caminho\config.json]
        /// Sem argumento, lê o config.json da pasta do executável.
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += OnErroNaoTratado;

            var caminhoConfig = ResolverCaminhoConfig(args);
            var carga = ConfigLoader.CarregarArquivo(caminhoConfig);
            if (!carga.Valido)
            {
                MessageBox.Show(
                    "Não foi possível carregar a configuração:\n\n" + string.Join("\n", carga.Erros.Take(15)),
                    "WinAllApp", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }

            var pastaInstaladores = InstallCommandBuilder.ResolverPastaInstaladores(carga.Config, carga.PastaConfig);
            var viewModel = new MainViewModel(carga.Config, pastaInstaladores, new ProcessRunner());
            viewModel.RegistrarAvisos(carga.Avisos);

            var janela = WindowFactory.CriarJanelaPrincipal(viewModel);
            return app.Run(janela);
        }

        internal static string ResolverCaminhoConfig(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return Path.GetFullPath(args[i + 1]);
                if (args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(args[i]);
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        private static void OnErroNaoTratado(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Erro inesperado:\n\n" + e.Exception.Message, "WinAllApp", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
