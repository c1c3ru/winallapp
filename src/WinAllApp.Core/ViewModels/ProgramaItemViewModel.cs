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

        public ProgramaItemViewModel(Programa programa, AmbienteSistema ambiente = null)
        {
            Programa = programa ?? throw new ArgumentNullException(nameof(programa));
            Categoria = CategoriaResolver.Resolver(programa);
            AvisoCompatibilidade = ambiente == null ? null : EstrategiaBase.AvisoCompatibilidade(programa, ambiente);
        }

        public CategoriaInstalacao Categoria { get; }
        public string CategoriaTexto => CategoriaResolver.Rotulo(Categoria);

        /// <summary>Ex.: "Versão incompatível com o SO..." no Windows 7. Vazio quando roda neste Windows.</summary>
        public string AvisoCompatibilidade { get; }
        public bool TemAvisoCompatibilidade => !string.IsNullOrWhiteSpace(AvisoCompatibilidade);

        public Programa Programa { get; }
        public string Nome => Programa.Nome ?? Programa.Id;
        public string Versao => Programa.Versao;
        public string Detalhe
        {
            get
            {
                var partes = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Programa.Versao)) partes.Add("v" + Programa.Versao);
                partes.Add(CategoriaResolver.Rotulo(CategoriaResolver.Resolver(Programa)));
                if (!string.IsNullOrWhiteSpace(Programa.Instalador)) partes.Add(Programa.Instalador);
                if (!string.IsNullOrWhiteSpace(Programa.WingetId)) partes.Add("winget: " + Programa.WingetId);
                if (!string.IsNullOrWhiteSpace(Programa.ChocoId)) partes.Add("choco: " + Programa.ChocoId);
                return string.Join(" · ", partes);
            }
        }
        public string Observacao => Programa.Observacao;
        public bool TemObservacao => !string.IsNullOrWhiteSpace(Programa.Observacao);

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
                    case EstadoInstalacao.Sucesso:
                        return Categoria == CategoriaInstalacao.OfflineLicenciado ? "Instalado (ativar licença)"
                            : Categoria == CategoriaInstalacao.CopiaPasta ? "Copiado" : "Instalado";
                    case EstadoInstalacao.SucessoReiniciar: return "Instalado (reiniciar)";
                    case EstadoInstalacao.Falha: return "Falhou";
                    case EstadoInstalacao.Cancelado: return "Cancelado";
                    case EstadoInstalacao.Incompativel: return "Incompatível com o SO";
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
