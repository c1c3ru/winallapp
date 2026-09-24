using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;

namespace WinAllApp
{
    /// <summary>Carrega as telas XAML embutidas no executável e liga o ViewModel.</summary>
    public static class WindowFactory
    {
        public const string RecursoJanelaPrincipal = "WinAllApp.Views.MainWindow.xaml";
        public const string RecursoOnboarding = "WinAllApp.Views.OnboardingWindow.xaml";
        public const string RecursoLogo = "WinAllApp.Imagens.logo-ifce.png";

        public static Window CriarJanelaPrincipal(object viewModel)
        {
            var janela = (Window)CarregarXaml(RecursoJanelaPrincipal);
            AplicarIdentidadeVisual(janela);
            janela.DataContext = viewModel;
            return janela;
        }

        /// <summary>Cria a janela do tutorial (sem exibir). Ela se fecha sozinha ao concluir ou pular.</summary>
        public static Window CriarOnboarding(OnboardingViewModel viewModel)
        {
            var janela = (Window)CarregarXaml(RecursoOnboarding);
            AplicarIdentidadeVisual(janela);
            janela.DataContext = viewModel;
            viewModel.Concluido += (s, e) => janela.Close();
            return janela;
        }

        /// <summary>Mostra o tutorial como janela modal sobre a principal.</summary>
        public static void MostrarOnboarding(Window dona, PreferenciasUsuario preferencias)
        {
            var pastaRede = (dona.DataContext as MainViewModel)?.PastaRede;
            var janela = CriarOnboarding(new OnboardingViewModel(pastaRede, preferencias));
            if (dona.IsVisible) janela.Owner = dona;
            else janela.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            janela.ShowDialog();
        }

        /// <summary>
        /// Logo do IFCE no elemento "ImagemLogo" (se houver). O ícone da janela e da barra de tarefas
        /// vem do próprio WinAllApp.exe (ApplicationIcon = Imagens\ifce.ico).
        /// </summary>
        public static void AplicarIdentidadeVisual(Window janela)
        {
            if (janela.FindName("ImagemLogo") is Image logo) logo.Source = CarregarImagem(RecursoLogo);
        }

        public static ImageSource CarregarImagem(string recurso)
        {
            var assembly = typeof(WindowFactory).Assembly;
            using (var stream = assembly.GetManifestResourceStream(recurso))
            {
                if (stream == null) throw new InvalidOperationException($"Imagem não encontrada: {recurso}");
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }

        public static object CarregarXaml(string recurso)
        {
            var assembly = typeof(WindowFactory).Assembly;
            using (var stream = assembly.GetManifestResourceStream(recurso))
            {
                if (stream == null) throw new InvalidOperationException($"Recurso XAML não encontrado: {recurso}");
                return XamlReader.Load(stream);
            }
        }
    }
}
