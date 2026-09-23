using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;

namespace WinAllApp.Core.ViewModels
{
    /// <summary>
    /// Fluxo da tela principal: 1) escolher o Bloco, 2) escolher o Laboratório,
    /// 3) marcar os programas daquela sala, 4) instalar em fila assíncrona.
    /// </summary>
    public sealed class MainViewModel : ObservableObject
    {
        private readonly LabCatalog _catalogo;
        private readonly InstallQueue _fila;
        private readonly SynchronizationContext _ui;

        private Bloco _blocoSelecionado;
        private Laboratorio _laboratorioSelecionado;
        private bool _ocupado;
        private double _progresso;
        private string _statusTexto;
        private CancellationTokenSource _cancelamento;

        public MainViewModel(InstallerConfig config, string pastaInstaladores, IProcessRunner runner)
            : this(new LabCatalog(config), new InstallQueue(runner, pastaInstaladores))
        {
        }

        public MainViewModel(LabCatalog catalogo, InstallQueue fila)
        {
            _catalogo = catalogo ?? throw new ArgumentNullException(nameof(catalogo));
            _fila = fila ?? throw new ArgumentNullException(nameof(fila));
            _ui = SynchronizationContext.Current;

            Blocos = new ObservableCollection<Bloco>(_catalogo.Blocos);
            Laboratorios = new ObservableCollection<Laboratorio>();
            Programas = new ObservableCollection<ProgramaItemViewModel>();
            Log = new ObservableCollection<string>();

            SelecionarTodosCommand = new RelayCommand(() => DefinirSelecao(true), () => !Ocupado && Programas.Count > 0);
            LimparSelecaoCommand = new RelayCommand(() => DefinirSelecao(false), () => !Ocupado && TotalSelecionados > 0);
            InstalarCommand = new AsyncRelayCommand(InstalarAsync, () => !Ocupado && TotalSelecionados > 0);
            CancelarCommand = new RelayCommand(Cancelar, () => Ocupado && _cancelamento != null && !_cancelamento.IsCancellationRequested);

            StatusTexto = "Escolha um bloco e depois um laboratório.";
            if (Blocos.Count == 1) BlocoSelecionado = Blocos[0];
        }

        public ObservableCollection<Bloco> Blocos { get; }
        public ObservableCollection<Laboratorio> Laboratorios { get; }
        public ObservableCollection<ProgramaItemViewModel> Programas { get; }
        public ObservableCollection<string> Log { get; }

        public RelayCommand SelecionarTodosCommand { get; }
        public RelayCommand LimparSelecaoCommand { get; }
        public AsyncRelayCommand InstalarCommand { get; }
        public RelayCommand CancelarCommand { get; }

        public Bloco BlocoSelecionado
        {
            get => _blocoSelecionado;
            set
            {
                if (Ocupado || !Set(ref _blocoSelecionado, value)) return;

                Laboratorios.Clear();
                if (value != null)
                    foreach (var lab in _catalogo.ObterLaboratorios(value.Id)) Laboratorios.Add(lab);

                LaboratorioSelecionado = null;
                StatusTexto = value == null ? "Escolha um bloco." : $"{value.Nome}: escolha um laboratório.";
            }
        }

        public Laboratorio LaboratorioSelecionado
        {
            get => _laboratorioSelecionado;
            set
            {
                if (Ocupado || !Set(ref _laboratorioSelecionado, value)) return;
                CarregarProgramas(value);
                OnPropertyChanged(nameof(Titulo));
                OnPropertyChanged(nameof(Subtitulo));
                OnPropertyChanged(nameof(TemLaboratorio));
            }
        }

        public bool TemLaboratorio => LaboratorioSelecionado != null;

        public string Titulo => LaboratorioSelecionado == null
            ? "Nenhum laboratório selecionado"
            : LaboratorioSelecionado.Nome ?? LaboratorioSelecionado.Id;

        public string Subtitulo
        {
            get
            {
                if (LaboratorioSelecionado == null) return "Use o menu à esquerda: primeiro o bloco, depois o laboratório.";
                var sala = string.IsNullOrWhiteSpace(LaboratorioSelecionado.Sala) ? string.Empty : $" · Sala {LaboratorioSelecionado.Sala}";
                return $"{BlocoSelecionado?.Nome}{sala} · {Programas.Count} programa(s)";
            }
        }

        public int TotalSelecionados => Programas.Count(p => p.Selecionado);

        public string ResumoSelecao => $"{TotalSelecionados} de {Programas.Count} selecionado(s)";

        public bool Ocupado
        {
            get => _ocupado;
            private set
            {
                if (!Set(ref _ocupado, value)) return;
                OnPropertyChanged(nameof(PodeNavegar));
                AtualizarComandos();
            }
        }

        /// <summary>Durante a instalação a navegação e as checkboxes ficam travadas.</summary>
        public bool PodeNavegar => !Ocupado;

        /// <summary>0 a 100.</summary>
        public double Progresso
        {
            get => _progresso;
            private set => Set(ref _progresso, value);
        }

        public string StatusTexto
        {
            get => _statusTexto;
            private set => Set(ref _statusTexto, value);
        }

        /// <summary>Instala somente os itens marcados do laboratório atual, sem bloquear a thread da UI.</summary>
        public async Task<IReadOnlyList<InstallResult>> InstalarAsync()
        {
            var itens = Programas.Where(p => p.Selecionado).ToList();
            if (Ocupado || itens.Count == 0) return Array.Empty<InstallResult>();

            foreach (var item in Programas)
            {
                item.Estado = EstadoInstalacao.Pendente;
                item.Mensagem = null;
            }

            var porPrograma = itens.ToDictionary(i => i.Programa, i => i);
            var cancelamento = new CancellationTokenSource();
            _cancelamento = cancelamento;
            Ocupado = true;
            Progresso = 0;
            StatusTexto = $"Instalando {itens.Count} programa(s) em {Titulo}…";
            AdicionarLog($"Início: {itens.Count} programa(s) de {BlocoSelecionado?.Id}/{LaboratorioSelecionado?.Id}.");

            IReadOnlyList<InstallResult> resultados = Array.Empty<InstallResult>();
            try
            {
                resultados = await _fila.ExecutarAsync(
                    itens.Select(i => i.Programa),
                    p => NaUi(() => AplicarProgresso(p, porPrograma)),
                    cancelamento.Token);
            }
            catch (Exception ex)
            {
                AdicionarLog($"Erro inesperado: {ex.Message}");
            }
            finally
            {
                // Este await não usa ConfigureAwait(false): o final roda de volta na thread da UI.
                _cancelamento = null;
                cancelamento.Dispose();
                Concluir(resultados);
            }

            return resultados;
        }

        /// <summary>Mostra no registro os avisos da validação do config.json (ex.: .exe sem parâmetro silencioso).</summary>
        public void RegistrarAvisos(IEnumerable<string> avisos)
        {
            foreach (var aviso in avisos ?? Enumerable.Empty<string>()) AdicionarLog("Aviso: " + aviso);
        }

        private void AplicarProgresso(InstallProgress p, Dictionary<Programa, ProgramaItemViewModel> porPrograma)
        {
            if (porPrograma.TryGetValue(p.Programa, out var item))
            {
                item.Estado = p.Estado;
                item.Mensagem = p.Mensagem;
            }

            var concluidos = p.Estado == EstadoInstalacao.Instalando ? p.Indice - 1 : p.Indice;
            Progresso = p.Total == 0 ? 0 : 100.0 * concluidos / p.Total;
            if (p.Estado == EstadoInstalacao.Instalando)
                StatusTexto = $"[{p.Indice}/{p.Total}] Instalando {p.Programa.Nome}…";
            AdicionarLog($"[{p.Indice}/{p.Total}] {p.Programa.Nome}: {p.Mensagem}");
        }

        private void Concluir(IReadOnlyList<InstallResult> resultados)
        {
            var ok = resultados.Count(r => r.Sucesso);
            var falhas = resultados.Count(r => r.Estado == EstadoInstalacao.Falha);
            var cancelados = resultados.Count(r => r.Estado == EstadoInstalacao.Cancelado);
            Progresso = 100;
            StatusTexto = $"Concluído: {ok} instalado(s), {falhas} falha(s), {cancelados} cancelado(s).";
            AdicionarLog(StatusTexto);
            Ocupado = false;
        }

        private void Cancelar()
        {
            if (_cancelamento == null || _cancelamento.IsCancellationRequested) return;
            _cancelamento.Cancel();
            StatusTexto = "Cancelando… o instalador atual será encerrado.";
            AdicionarLog("Cancelamento solicitado pelo usuário.");
            AtualizarComandos();
        }

        private void CarregarProgramas(Laboratorio lab)
        {
            foreach (var antigo in Programas) antigo.SelecaoAlterada -= ItemSelecaoAlterada;
            Programas.Clear();

            foreach (var p in _catalogo.ObterProgramas(lab))
            {
                var item = new ProgramaItemViewModel(p);
                item.SelecaoAlterada += ItemSelecaoAlterada;
                Programas.Add(item);
            }

            if (lab != null) StatusTexto = $"{Programas.Count} programa(s) em {lab.Nome}. Marque os que deseja instalar.";
            NotificarSelecao();
        }

        private void DefinirSelecao(bool marcado)
        {
            foreach (var item in Programas) item.Selecionado = marcado;
        }

        private void ItemSelecaoAlterada(object sender, EventArgs e) => NotificarSelecao();

        private void NotificarSelecao()
        {
            OnPropertyChanged(nameof(TotalSelecionados));
            OnPropertyChanged(nameof(ResumoSelecao));
            OnPropertyChanged(nameof(Subtitulo));
            AtualizarComandos();
        }

        private void AtualizarComandos()
        {
            SelecionarTodosCommand.NotificarMudanca();
            LimparSelecaoCommand.NotificarMudanca();
            InstalarCommand.NotificarMudanca();
            CancelarCommand.NotificarMudanca();
        }

        private void AdicionarLog(string linha) => Log.Add($"{DateTime.Now:HH:mm:ss}  {linha}");

        private void NaUi(Action acao)
        {
            if (_ui == null || SynchronizationContext.Current == _ui) acao();
            else _ui.Post(_ => acao(), null);
        }
    }
}
