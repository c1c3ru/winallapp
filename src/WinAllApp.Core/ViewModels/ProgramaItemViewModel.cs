using System;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;

namespace WinAllApp.Core.ViewModels
{
    /// <summary>Uma linha da lista: checkbox + nome + estado da instalação.</summary>
    public sealed class ProgramaItemViewModel : ObservableObject
    {
        private bool _selecionado;
        private EstadoInstalacao _estado = EstadoInstalacao.Pendente;
        private string _mensagem;

        public ProgramaItemViewModel(Programa programa)
        {
            Programa = programa ?? throw new ArgumentNullException(nameof(programa));
        }

        public Programa Programa { get; }
        public string Nome => Programa.Nome ?? Programa.Id;
        public string Versao => Programa.Versao;
        public string Detalhe => string.IsNullOrWhiteSpace(Programa.Versao) ? Programa.Instalador : $"v{Programa.Versao} · {Programa.Instalador}";

        public event EventHandler SelecaoAlterada;

        public bool Selecionado
        {
            get => _selecionado;
            set
            {
                if (Set(ref _selecionado, value)) SelecaoAlterada?.Invoke(this, EventArgs.Empty);
            }
        }

        public EstadoInstalacao Estado
        {
            get => _estado;
            set
            {
                if (Set(ref _estado, value)) OnPropertyChanged(nameof(EstadoTexto));
            }
        }

        /// <summary>Texto curto exibido na coluna de estado.</summary>
        public string EstadoTexto
        {
            get
            {
                switch (Estado)
                {
                    case EstadoInstalacao.Instalando: return "Instalando…";
                    case EstadoInstalacao.Sucesso: return "Instalado";
                    case EstadoInstalacao.SucessoReiniciar: return "Instalado (reiniciar)";
                    case EstadoInstalacao.Falha: return "Falhou";
                    case EstadoInstalacao.Cancelado: return "Cancelado";
                    default: return string.Empty;
                }
            }
        }

        public string Mensagem
        {
            get => _mensagem;
            set => Set(ref _mensagem, value);
        }
    }
}
