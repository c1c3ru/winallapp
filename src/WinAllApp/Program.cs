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
        ///   WinAllApp.exe --simulacao --simular-windows 7   força o Windows detectado (7, 8.1, 10 ou 11) na simulação;
        ///                                    com --sem-tls12 / --sem-dotnet48 simula os pré-requisitos do Chocolatey ausentes
        ///   WinAllApp.exe --tutorial         mostra o tutorial de boas-vindas de novo
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

            var simulacao = origem.StartsWith("SIMULAÇÃO", StringComparison.Ordinal);
            AmbienteSistema ambiente;
            try
            {
                ambiente = CriarAmbiente(args, simulacao, carga.PastaConfig);
            }
            catch (ArgumentException ex)
            {
                Erro(ex.Message);
                return 1;
            }

            var viewModel = CriarViewModel(carga, ambiente);
            viewModel.RegistrarMensagem("Configuração: " + origem);
            viewModel.RegistrarMensagem("Pasta de rede (fonte primária): " + viewModel.PastaRede);
            viewModel.RegistrarMensagem("Sistema: " + ambiente.Resumo());
            if (!ambiente.UsaWinget && !ambiente.PreRequisitosChocoOk)
                viewModel.RegistrarMensagem("Aviso: sem .NET 4.8 e TLS 1.2 o Chocolatey não será usado; só a pasta de rede.");
            viewModel.RegistrarAvisos(carga.Avisos);

            var janela = WindowFactory.CriarJanelaPrincipal(viewModel);
            if (simulacao) janela.Title += " — MODO SIMULAÇÃO";

            var tutorial = new PreferenciasUsuario();
            janela.Loaded += (s, e) =>
            {
                viewModel.VerificarPastaRedeCommand.Execute(null);
                if (DeveMostrarOnboarding(args, tutorial)) WindowFactory.MostrarOnboarding(janela, tutorial);
            };
            viewModel.TutorialSolicitado += (s, e) => WindowFactory.MostrarOnboarding(janela, tutorial);
            return app.Run(janela);
        }

        public static string PastaDoExecutavel => AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>Na primeira execução (ou com --tutorial) o tutorial de boas-vindas abre sobre a janela principal.</summary>
        public static bool DeveMostrarOnboarding(string[] args, PreferenciasUsuario preferencias) =>
            TemOpcao(args, "--tutorial") || !preferencias.OnboardingConcluido;

        /// <summary>Monta a fila com o roteador das 4 categorias (rede primeiro; winget/Chocolatey conforme o Windows).</summary>
        public static MainViewModel CriarViewModel(ConfigLoadResult carga, AmbienteSistema ambiente)
        {
            var pastaInstaladores = InstallCommandBuilder.ResolverPastaInstaladores(carga.Config, carga.PastaConfig);
            var destinoCopias = InstallCommandBuilder.ResolverPastaDestinoCopias(carga.Config, carga.PastaConfig);
            var roteador = new RoteadorInstalacao(new ContextoInstalacao(pastaInstaladores, destinoCopias, ambiente));
            var fila = new InstallQueue(new ProcessRunner(), new CopiadorPastas(), roteador);
            return new MainViewModel(new LabCatalog(carga.Config), fila);
        }

        /// <summary>
        /// Máquina real: detectada pelo registro. Simulação: Windows escolhido em --simular-windows (padrão 10),
        /// com winget.bat/choco.bat fictícios no lugar das ferramentas reais.
        /// </summary>
        public static AmbienteSistema CriarAmbiente(string[] args, bool simulacao, string pastaConfig)
        {
            if (!simulacao) return DetectorAmbiente.Detectar();

            var mocks = Path.Combine(pastaConfig, "mock-installers");
            return AmbienteSistema.Simular(ValorDaOpcao(args, "--simular-windows") ?? "10",
                caminhoWinget: Path.Combine(mocks, "winget.bat"),
                caminhoChoco: Path.Combine(mocks, "choco.bat"),
                dotNet48: !TemOpcao(args, "--sem-dotnet48"),
                tls12: !TemOpcao(args, "--sem-tls12"));
        }

        public static string ValorDaOpcao(string[] args, string opcao)
        {
            for (var i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], opcao, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        public static ConfigLoadResult CarregarConfig(string[] args, out string origem)
        {
            if (TemOpcao(args, "--simulacao") || ValorDaOpcao(args, "--simular-windows") != null)
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
