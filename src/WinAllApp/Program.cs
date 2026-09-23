using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;

namespace WinAllApp
{
    public static class Program
    {
        /// <summary>
        /// Uso:
        ///   WinAllApp.exe                    usa o config.json ao lado do .exe ou, se não existir, o embutido
        ///   WinAllApp.exe --config x.json    usa outro arquivo de configuração
        ///   WinAllApp.exe --simulacao        testa com instaladores fictícios (nada é instalado)
        ///   WinAllApp.exe --extrair-config   grava o config.json embutido ao lado do .exe para edição
        /// </summary>
        [STAThread]
        public static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.DispatcherUnhandledException += OnErroNaoTratado;

            if (TemOpcao(args, "--extrair-config")) return ExtrairConfig();

            ConfigLoadResult carga;
            string origem;
            try
            {
                carga = CarregarConfig(args, out origem);
            }
            catch (Exception ex)
            {
                Erro("Não foi possível preparar a configuração:\n\n" + ex.Message);
                return 1;
            }

            if (!carga.Valido)
            {
                Erro("Não foi possível carregar a configuração (" + origem + "):\n\n" + string.Join("\n", carga.Erros.Take(15)));
                return 1;
            }

            var pastaInstaladores = InstallCommandBuilder.ResolverPastaInstaladores(carga.Config, carga.PastaConfig);
            var viewModel = new MainViewModel(carga.Config, pastaInstaladores, new ProcessRunner());
            viewModel.RegistrarMensagem("Configuração: " + origem);
            viewModel.RegistrarMensagem("Pasta dos instaladores: " + pastaInstaladores);
            viewModel.RegistrarAvisos(carga.Avisos);

            var janela = WindowFactory.CriarJanelaPrincipal(viewModel);
            if (origem.StartsWith("SIMULAÇÃO", StringComparison.Ordinal)) janela.Title += " — MODO SIMULAÇÃO";
            return app.Run(janela);
        }

        public static string PastaDoExecutavel => AppDomain.CurrentDomain.BaseDirectory;

        public static ConfigLoadResult CarregarConfig(string[] args, out string origem)
        {
            if (TemOpcao(args, "--simulacao"))
            {
                var pasta = RecursosEmbutidos.ExtrairSimulacao(Path.Combine(Path.GetTempPath(), "WinAllApp-simulacao"));
                origem = "SIMULAÇÃO (" + pasta + ")";
                return ConfigLoader.CarregarArquivo(Path.Combine(pasta, "config.simulacao.json"));
            }

            var informado = ArquivoInformado(args);
            if (informado != null)
            {
                origem = informado;
                return ConfigLoader.CarregarArquivo(informado);
            }

            var aoLado = Path.Combine(PastaDoExecutavel, "config.json");
            if (File.Exists(aoLado))
            {
                origem = aoLado;
                return ConfigLoader.CarregarArquivo(aoLado);
            }

            origem = "embutida no WinAllApp.exe (use --extrair-config para editar)";
            return ConfigLoader.CarregarTexto(RecursosEmbutidos.LerTexto(RecursosEmbutidos.ConfigPadrao), PastaDoExecutavel);
        }

        public static string ArquivoInformado(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    return Path.GetFullPath(args[i + 1]);
                if (args[i].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(args[i]);
            }
            return null;
        }

        private static bool TemOpcao(string[] args, string opcao) =>
            args.Any(a => string.Equals(a, opcao, StringComparison.OrdinalIgnoreCase)
                          || string.Equals(a, "/" + opcao.TrimStart('-'), StringComparison.OrdinalIgnoreCase));

        private static int ExtrairConfig()
        {
            var destino = Path.Combine(PastaDoExecutavel, "config.json");
            if (File.Exists(destino))
            {
                MessageBox.Show("Já existe um config.json nesta pasta; nada foi alterado:\n\n" + destino,
                    "WinAllApp", MessageBoxButton.OK, MessageBoxImage.Information);
                return 0;
            }

            try
            {
                File.WriteAllText(destino, RecursosEmbutidos.LerTexto(RecursosEmbutidos.ConfigPadrao), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Erro("Não foi possível gravar o config.json:\n\n" + ex.Message);
                return 1;
            }

            MessageBox.Show("config.json gravado. Edite-o e abra o WinAllApp.exe de novo:\n\n" + destino,
                "WinAllApp", MessageBoxButton.OK, MessageBoxImage.Information);
            return 0;
        }

        private static void Erro(string mensagem) =>
            MessageBox.Show(mensagem, "WinAllApp", MessageBoxButton.OK, MessageBoxImage.Error);

        private static void OnErroNaoTratado(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Erro("Erro inesperado:\n\n" + e.Exception.Message);
            e.Handled = true;
        }
    }
}
