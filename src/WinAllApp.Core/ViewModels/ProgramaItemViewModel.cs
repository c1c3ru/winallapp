using System;
using System.Globalization;
using System.Text;
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
                if (!Set(ref _estado, value)) return;
                OnPropertyChanged(nameof(EstadoTexto));
                OnPropertyChanged(nameof(TemFalha));
                OnPropertyChanged(nameof(Concluido));
                OnPropertyChanged(nameof(MostrarMensagemErro));
            }
        }

        /// <summary>Falhou: a linha ganha o ícone e o texto vermelhos com o motivo.</summary>
        public bool TemFalha => Estado == EstadoInstalacao.Falha;

        /// <summary>Já saiu da fila (instalado, falhou, cancelado ou incompatível).</summary>
        public bool Concluido => Estado != EstadoInstalacao.Pendente && Estado != EstadoInstalacao.Instalando;

        public bool MostrarMensagemErro => TemFalha && !string.IsNullOrWhiteSpace(Mensagem);

        /// <summary>Verdadeiro quando o nome, o id, a versão ou a categoria contêm o texto (sem diferenciar maiúsculas e acentos).</summary>
        public bool Corresponde(string pesquisa)
        {
            if (string.IsNullOrWhiteSpace(pesquisa)) return true;
            var termo = pesquisa.Trim();
            return Contem(Nome, termo) || Contem(Programa.Id, termo) || Contem(Programa.Versao, termo) || Contem(CategoriaTexto, termo);
        }

        private static bool Contem(string texto, string termo) =>
            !string.IsNullOrEmpty(texto) && SemAcentos(texto).IndexOf(SemAcentos(termo), StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>"Lógica" → "Logica": a pesquisa não depende de acento (igual no .NET 4.8 e nos testes).</summary>
        public static string SemAcentos(string texto)
        {
            var decomposto = (texto ?? string.Empty).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposto.Length);
            foreach (var c in decomposto)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
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
            set
            {
                if (Set(ref _mensagem, value)) OnPropertyChanged(nameof(MostrarMensagemErro));
            }
        }
    }
}
