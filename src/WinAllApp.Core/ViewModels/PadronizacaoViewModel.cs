using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.Services.Padronizacao;

namespace WinAllApp.Core.ViewModels
{
    /// <summary>Uma linha da lista de etapas na tela de padronização.</summary>
    public sealed class EtapaItemViewModel : ObservableObject
    {
        private StatusEtapa _status = StatusEtapa.Pendente;
        private string _resumo = "Aguardando";

        public EtapaItemViewModel(EtapaPadronizacao etapa)
        {
            Etapa = etapa;
        }

        public EtapaPadronizacao Etapa { get; }
        public string Topico => Etapa.Topico;
        public string Titulo => Etapa.Titulo;
        public string Rotulo => $"{Etapa.Topico} · {Etapa.Titulo}";

        /// <summary>Já recebeu o resultado final (concluída, aviso, falha ou ignorada).</summary>
        internal bool Finalizada { get; set; }

        public StatusEtapa Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public string Resumo
        {
            get => _resumo;
            set => Set(ref _resumo, value);
        }
    }

    /// <summary>Tela "Padronizar este computador": preenche as opções do checklist e roda o motor.</summary>
    public sealed class PadronizacaoViewModel : ObservableObject
    {
        private readonly MotorPadronizacao _motor;
        private readonly OpcoesPadronizacao _opcoes = new OpcoesPadronizacao();
        private CancellationTokenSource _cancelamento;
        private bool _executando;
        private string _resumoFinal = string.Empty;
        private readonly StringBuilder _registro = new StringBuilder();

        public PadronizacaoViewModel(MotorPadronizacao motor, AmbienteSistema ambiente, string executavelEmUso = null)
        {
            _motor = motor ?? throw new ArgumentNullException(nameof(motor));
            Versao = SistemaPadronizacao.Detectar(ambiente);
            TextoWindows = ambiente == null
                ? "Windows não identificado"
                : $"{SistemaPadronizacao.Nome(Versao)} · {ambiente.NomeWindows} build {ambiente.BuildWindows}" + (ambiente.Simulado ? " [SIMULADO]" : string.Empty);
            if (Versao == VersaoPadronizacao.NaoSuportado)
                TextoWindows = $"{ambiente?.NomeWindows}: a padronização é só para Windows 10 e 11.";
            _opcoes.ExecutavelEmUso = executavelEmUso;

            ExecutarCommand = new AsyncRelayCommand(ExecutarAsync, () => PodeExecutar);
            CancelarCommand = new RelayCommand(() => _cancelamento?.Cancel(), () => Executando);
        }

        public VersaoPadronizacao Versao { get; }
        public string TextoWindows { get; }
        public bool Suportado => Versao != VersaoPadronizacao.NaoSuportado;

        public IReadOnlyList<string> Blocos => OpcoesPadronizacao.Blocos;

        /// <summary>Pergunta ao técnico antes de mexer no sistema (a tela liga numa MessageBox; nulo = não pergunta).</summary>
        public Func<string, bool> Confirmar { get; set; }

        public ObservableCollection<EtapaItemViewModel> Etapas { get; } = new ObservableCollection<EtapaItemViewModel>();

        public AsyncRelayCommand ExecutarCommand { get; }
        public RelayCommand CancelarCommand { get; }

        public string Bloco
        {
            get => _opcoes.Bloco;
            set { _opcoes.Bloco = value; Mudou(); }
        }

        public string Local
        {
            get => _opcoes.Local;
            set { _opcoes.Local = value; Mudou(); }
        }

        public string Numero
        {
            get => _opcoes.Numero;
            set { _opcoes.Numero = value; Mudou(); }
        }

        public string NomeComputador => _opcoes.NomeComputador;

        /// <summary>Preenchida pelo PasswordBox da tela (não há binding de senha no WPF).</summary>
        public string SenhaInformatica
        {
            get => _opcoes.SenhaInformatica;
            set { _opcoes.SenhaInformatica = value ?? string.Empty; Mudou(); }
        }

        public bool PerfilAluno
        {
            get => _opcoes.Perfil == PerfilDoUsuario.Aluno;
            set { if (value) { _opcoes.Perfil = PerfilDoUsuario.Aluno; Mudou(); } }
        }

        public bool PerfilBolsista
        {
            get => _opcoes.Perfil == PerfilDoUsuario.Bolsista;
            set { if (value) { _opcoes.Perfil = PerfilDoUsuario.Bolsista; Mudou(); } }
        }

        public string ChaveProduto
        {
            get => _opcoes.ChaveProduto;
            set { _opcoes.ChaveProduto = value ?? string.Empty; Mudou(); }
        }

        public bool AdicionarImpressora
        {
            get => _opcoes.AdicionarImpressora;
            set { _opcoes.AdicionarImpressora = value; Mudou(); }
        }

        public string IpImpressora
        {
            get => _opcoes.IpImpressora;
            set { _opcoes.IpImpressora = value ?? string.Empty; Mudou(); }
        }

        public string NomeImpressora
        {
            get => _opcoes.NomeImpressora;
            set { _opcoes.NomeImpressora = value ?? string.Empty; Mudou(); }
        }

        public bool LimparAreaDeTrabalho
        {
            get => _opcoes.LimparAreaDeTrabalho;
            set { _opcoes.LimparAreaDeTrabalho = value; Mudou(); }
        }

        public string PastaPapeisDeParede
        {
            get => _opcoes.PastaPapeisDeParede;
            set { _opcoes.PastaPapeisDeParede = value ?? string.Empty; Mudou(); }
        }

        /// <summary>Para testes e simulação: onde as imagens são copiadas.</summary>
        public string PastaLocalPapeis
        {
            get => _opcoes.PastaLocalPapeis;
            set => _opcoes.PastaLocalPapeis = value;
        }

        public IReadOnlyList<string> Erros => Suportado ? _opcoes.Validar() : new[] { TextoWindows };

        public string TextoErros => string.Join(Environment.NewLine, Erros);

        public bool TemErros => Erros.Count > 0;

        public bool Executando
        {
            get => _executando;
            private set
            {
                if (!Set(ref _executando, value)) return;
                OnPropertyChanged(nameof(PodeEditar));
                OnPropertyChanged(nameof(PodeExecutar));
                ExecutarCommand.NotificarMudanca();
                CancelarCommand.NotificarMudanca();
            }
        }

        public bool PodeEditar => !Executando;

        public bool PodeExecutar => !Executando && !TemErros;

        public string ResumoFinal
        {
            get => _resumoFinal;
            private set => Set(ref _resumoFinal, value);
        }

        public string Registro
        {
            get { lock (_registro) return _registro.ToString(); }
        }

        public event EventHandler Concluida;

        private void Mudou([System.Runtime.CompilerServices.CallerMemberName] string propriedade = null)
        {
            OnPropertyChanged(propriedade);
            if (propriedade == nameof(PerfilAluno) || propriedade == nameof(PerfilBolsista))
            {
                OnPropertyChanged(nameof(PerfilAluno));
                OnPropertyChanged(nameof(PerfilBolsista));
            }
            OnPropertyChanged(nameof(NomeComputador));
            OnPropertyChanged(nameof(Erros));
            OnPropertyChanged(nameof(TextoErros));
            OnPropertyChanged(nameof(TemErros));
            OnPropertyChanged(nameof(PodeExecutar));
            ExecutarCommand?.NotificarMudanca();
        }

        private void Registrar(string texto)
        {
            lock (_registro) _registro.AppendLine($"[{DateTime.Now:HH:mm:ss}] {texto}");
            OnPropertyChanged(nameof(Registro));
        }

        private async Task ExecutarAsync()
        {
            if (TemErros) return;
            IReadOnlyList<EtapaPadronizacao> plano;
            try
            {
                plano = _motor.Planejar(_opcoes, Versao);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
            {
                Registrar("Não foi possível montar a padronização: " + ex.Message);
                return;
            }

            var pergunta = "Isto vai alterar este computador:\n\n"
                           + string.Join("\n", plano.Select(e => $"• {e.Topico} {e.Titulo}"))
                           + "\n\nO novo nome vale depois de reiniciar. Continuar?";
            if (Confirmar != null && !Confirmar(pergunta)) return;

            Etapas.Clear();
            var itens = plano.ToDictionary(e => e, e => new EtapaItemViewModel(e));
            foreach (var item in itens.Values) Etapas.Add(item);

            ResumoFinal = string.Empty;
            Executando = true;
            _cancelamento = new CancellationTokenSource();
            Registrar($"Padronização iniciada no {SistemaPadronizacao.Nome(Versao)}: {NomeComputador}, contas Informatica e {_opcoes.ContaDoUsuario}.");

            // Progress criado aqui (thread da tela): cada status volta para a tela, mesmo com o motor rodando em segundo plano.
            var progresso = new Progress<KeyValuePair<EtapaPadronizacao, ResultadoEtapa>>(par =>
            {
                var item = itens[par.Key];
                var final = par.Value.Status != StatusEtapa.Executando && par.Value.Status != StatusEtapa.Pendente;
                // Sem contexto de tela os avisos chegam por threads diferentes e fora de ordem:
                // "Executando" nunca sobrescreve um resultado que já chegou.
                lock (item)
                {
                    if (!final && item.Finalizada) return;
                    if (final) item.Finalizada = true;
                    item.Status = par.Value.Status;
                    item.Resumo = par.Value.Resumo;
                }
                if (final)
                    Registrar($"{par.Key.Topico} {par.Key.Titulo}: {Rotulo(par.Value.Status)}. {par.Value.Resumo}");
            });

            try
            {
                var cancelamento = _cancelamento.Token;
                var resultados = await Task.Run(() => _motor.ExecutarAsync(plano, progresso, cancelamento)).ConfigureAwait(true);
                ResumoFinal = Resumir(resultados);
                Registrar(ResumoFinal);
            }
            finally
            {
                _cancelamento.Dispose();
                _cancelamento = null;
                Executando = false;
                Concluida?.Invoke(this, EventArgs.Empty);
            }
        }

        public static string Rotulo(StatusEtapa status)
        {
            switch (status)
            {
                case StatusEtapa.Concluida: return "concluída";
                case StatusEtapa.ConcluidaComAviso: return "concluída com aviso";
                case StatusEtapa.Falhou: return "FALHOU";
                case StatusEtapa.Ignorada: return "não executada";
                case StatusEtapa.Executando: return "executando";
                default: return "aguardando";
            }
        }

        private static string Resumir(IReadOnlyList<ResultadoEtapa> resultados)
        {
            var ok = resultados.Count(r => r.Status == StatusEtapa.Concluida);
            var avisos = resultados.Count(r => r.Status == StatusEtapa.ConcluidaComAviso);
            var falhas = resultados.Count(r => r.Status == StatusEtapa.Falhou);
            var ignoradas = resultados.Count(r => r.Status == StatusEtapa.Ignorada);
            var texto = $"{ok} de {resultados.Count} etapa(s) concluída(s)";
            if (avisos > 0) texto += $", {avisos} com aviso";
            if (falhas > 0) texto += $", {falhas} com falha";
            if (ignoradas > 0) texto += $", {ignoradas} não executada(s)";
            return texto + ". Reinicie o computador para aplicar o novo nome e as políticas.";
        }
    }
}
