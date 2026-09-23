using System;
using System.Windows;
using System.Windows.Markup;

namespace WinAllApp
{
    /// <summary>Carrega as telas XAML embutidas no executável e liga o ViewModel.</summary>
    public static class WindowFactory
    {
        public const string RecursoJanelaPrincipal = "WinAllApp.Views.MainWindow.xaml";

        public static Window CriarJanelaPrincipal(object viewModel)
        {
            var janela = (Window)CarregarXaml(RecursoJanelaPrincipal);
            janela.DataContext = viewModel;
            return janela;
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
