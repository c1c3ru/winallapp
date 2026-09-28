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
        private string _pastaRede;
        private string _pastaRedeStatus;
        private bool? _pastaRedeAcessivel;
        private bool _verificandoPastaRede;
        private string _searchText = string.Empty;
        private string _mensagemErro;
        private bool _erroDaRede;
        private IReadOnlyList<ProgramaItemViewModel> _itensDaFila = Array.Empty<ProgramaItemViewModel>();

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
            ProgramasVisiveis = new ObservableCollection<ProgramaItemViewModel>();
            Log = new ObservableCollection<string>();
            _pastaRede = Contexto.PastaInstaladores;

            SelecionarTodosCommand = new RelayCommand(SelecionarExibidos, () => !Ocupado && ProgramasVisiveis.Count > 0);
            LimparSelecaoCommand = new RelayCommand(() => DefinirSelecao(false), () => !Ocupado && TotalSelecionados > 0);
            InstalarCommand = new AsyncRelayCommand(InstalarAsync, () => !Ocupado && TotalSelecionados > 0);
            CancelarCommand = new RelayCommand(Cancelar, () => Ocupado && _cancelamento != null && !_cancelamento.IsCancellationRequested);
            VerificarPastaRedeCommand = new AsyncRelayCommand(VerificarPastaRedeAsync, () => !_verificandoPastaRede && !Ocupado);
            AbrirTutorialCommand = new RelayCommand(() => TutorialSolicitado?.Invoke(this, EventArgs.Empty));
            LimparPesquisaCommand = new RelayCommand(() => SearchText = string.Empty, () => TemPesquisa);
            FecharErroCommand = new RelayCommand(LimparErro, () => TemErro);
            PastaRedeStatus = "Pasta de rede: ainda não verificada.";

            StatusTexto = "Escolha um bloco e depois um laboratório.";
            if (Blocos.Count == 1) BlocoSelecionado = Blocos[0];
        }

        public ObservableCollection<Bloco> Blocos { get; }
        public ObservableCollection<Laboratorio> Laboratorios { get; }
        /// <summary>Todos os programas do laboratório escolhido (a instalação usa os marcados daqui).</summary>
        public ObservableCollection<ProgramaItemViewModel> Programas { get; }

        /// <summary>
        /// O que a lista mostra: os mesmos objetos de <see cref="Programas"/> filtrados por <see cref="SearchText"/>.
        /// Como são os mesmos objetos, a marcação continua quando o item some e volta pelo filtro.
        /// </summary>
        public ObservableCollection<ProgramaItemViewModel> ProgramasVisiveis { get; }

        public ObservableCollection<string> Log { get; }

        public RelayCommand SelecionarTodosCommand { get; }
        public RelayCommand LimparSelecaoCommand { get; }
        public AsyncRelayCommand InstalarCommand { get; }
        public RelayCommand CancelarCommand { get; }
        public AsyncRelayCommand VerificarPastaRedeCommand { get; }
        public RelayCommand AbrirTutorialCommand { get; }
        public RelayCommand LimparPesquisaCommand { get; }
        public RelayCommand FecharErroCommand { get; }

        /// <summary>Disparado pelo botão "Tutorial"; a janela abre o onboarding.</summary>
        public event EventHandler TutorialSolicitado;

        /// <summary>Máquina detectada (ou simulada) usada no roteamento winget/Chocolatey.</summary>
        public AmbienteSistema Ambiente => _fila.Roteador.Contexto.Ambiente;

        public string ResumoAmbiente => Ambiente.Resumo();

        private ContextoInstalacao Contexto => _fila.Roteador.Contexto;

        /// <summary>
        /// Campo "Pasta de rede" (fonte primária dos instaladores). Vem do config.json e pode ser editado antes de instalar;
        /// o texto digitado vai direto para o roteador (sem validar credenciais: um erro de acesso aparece no item e no alerta).
        /// </summary>
        public string PastaRede
        {
            get => _pastaRede;
            set
            {
                if (Ocupado)
                {
                    OnPropertyChanged();
                    return;
                }
                if (!Set(ref _pastaRede, value)) return;
                Contexto.PastaInstaladores = NormalizarPasta(value);
                PastaRedeAcessivel = null;
                PastaRedeStatus = "Pasta de rede alterada: clique em Verificar para testar o acesso.";
                if (_erroDaRede) LimparErro();
            }
        }

        /// <summary>Tira espaços e aspas das pontas (caminho colado do Explorer entre aspas).</summary>
        public static string NormalizarPasta(string pasta) => (pasta ?? string.Empty).Trim().Trim('"').Trim();

        /// <summary>Verdadeiro enquanto a pasta de rede é testada em segundo plano (mostra o spinner ao lado do campo).</summary>
        public bool VerificandoPastaRede
        {
            get => _verificandoPastaRede;
            private set
            {
                if (!Set(ref _verificandoPastaRede, value)) return;
                VerificarPastaRedeCommand.NotificarMudanca();
            }
        }

        public string PastaRedeStatus
        {
            get => _pastaRedeStatus;
            private set => Set(ref _pastaRedeStatus, value);
        }

        /// <summary>null = ainda não verificada.</summary>
        public bool? PastaRedeAcessivel
        {
            get => _pastaRedeAcessivel;
            private set => Set(ref _pastaRedeAcessivel, value);
        }

        /// <summary>Confere, sem travar a tela, se a pasta de rede responde (UNC inacessível pode demorar).</summary>
        public async Task VerificarPastaRedeAsync()
        {
            if (VerificandoPastaRede) return;
            var pasta = Contexto.PastaInstaladores;
            if (string.IsNullOrWhiteSpace(pasta))
            {
                PastaRedeAcessivel = false;
                PastaRedeStatus = "Pasta de rede não informada.";
                MostrarErro("Informe o caminho da pasta de rede no campo \"Pasta de rede\".", daRede: true);
                return;
            }

            VerificandoPastaRede = true;
            PastaRedeStatus = "Verificando a pasta de rede…";
            var existe = Contexto.PastaExiste;
            bool ok;
            string detalhe = null;
            try
            {
                // Um caminho UNC fora do ar pode levar dezenas de segundos: roda fora da thread da tela.
                ok = await Task.Run(() => existe(pasta));
            }
            catch (Exception ex)
            {
                ok = false;
                detalhe = ex.Message;
            }
            finally
            {
                VerificandoPastaRede = false;
            }

            if (!string.Equals(pasta, Contexto.PastaInstaladores, StringComparison.Ordinal))
            {
                // O técnico trocou o caminho enquanto a verificação rodava: o resultado é de outro caminho.
                PastaRedeStatus = "Pasta de rede alterada: clique em Verificar para testar o acesso.";
                return;
            }

            PastaRedeAcessivel = ok;
            PastaRedeStatus = ok ? "Pasta de rede acessível: " + pasta : "Pasta de rede INACESSÍVEL: " + pasta;
            AdicionarLog(PastaRedeStatus + (detalhe == null ? string.Empty : " (" + detalhe + ")"));
            if (ok)
            {
                if (_erroDaRede) LimparErro();
            }
            else
            {
                MostrarErro("Não foi possível acessar a pasta de rede: " + pasta +
                            ". Confira o caminho no campo \"Pasta de rede\" e se este computador está na rede do campus.", daRede: true);
            }
        }

        // ===== Pesquisa =====

        /// <summary>Texto da barra de pesquisa: filtra a lista em tempo real (nome, id, versão ou categoria).</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!Set(ref _searchText, value ?? string.Empty)) return;
                AplicarFiltro();
            }
        }

        public bool TemPesquisa => !string.IsNullOrWhiteSpace(SearchText);

        public string TextoSelecionarTodos => TemPesquisa ? "Selecionar os exibidos" : "Selecionar Todos do Laboratório";

        // ===== Contador da instalação =====

        /// <summary>Quantos programas entraram na última fila.</summary>
        public int TotalNaFila => _itensDaFila.Count;

        /// <summary>Quantos já saíram da fila (instalados, com falha, cancelados ou incompatíveis).</summary>
        public int Concluidos => _itensDaFila.Count(i => i.Concluido);

        public int Instalados => _itensDaFila.Count(i => i.Estado == EstadoInstalacao.Sucesso || i.Estado == EstadoInstalacao.SucessoReiniciar);

        public int Falhas => _itensDaFila.Count(i => i.Estado == EstadoInstalacao.Falha);

        public bool TemContador => TotalNaFila > 0;

        /// <summary>Deixa o contador vermelho quando algum programa falhou.</summary>
        public bool TemFalhas => Falhas > 0;

        /// <summary>Ex.: "2 de 5 concluído(s) · 2 instalado(s) · 0 falha(s)".</summary>
        public string ContadorTexto => TemContador
            ? $"{Concluidos} de {TotalNaFila} concluído(s) · {Instalados} instalado(s) · {Falhas} falha(s)"
            : string.Empty;

        // ===== Alerta de erro =====

        /// <summary>Texto da faixa vermelha de alerta (pasta de rede inacessível, falhas na instalação, erro inesperado).</summary>
        public string MensagemErro
        {
            get => _mensagemErro;
            private set
            {
                if (!Set(ref _mensagemErro, value)) return;
                OnPropertyChanged(nameof(TemErro));
                FecharErroCommand.NotificarMudanca();
            }
        }

        public bool TemErro => !string.IsNullOrWhiteSpace(MensagemErro);

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
                OnPropertyChanged(nameof(MostrarAvisoVazio));
                OnPropertyChanged(nameof(TextoAvisoVazio));
            }
        }

        /// <summary>Verdadeiro quando não há lista para mostrar (nenhum laboratório ou laboratório sem programas específicos).</summary>
        public bool MostrarAvisoVazio => ProgramasVisiveis.Count == 0;

        public string TextoAvisoVazio
        {
            get
            {
                if (LaboratorioSelecionado == null) return "Selecione um laboratório no menu à esquerda para ver os programas daquela sala.";
                if (Programas.Count > 0 && TemPesquisa)
                    return $"Nenhum programa deste laboratório corresponde a \"{SearchText.Trim()}\".";
                return string.IsNullOrWhiteSpace(LaboratorioSelecionado.Observacao)
                    ? "Nenhum programa específico cadastrado para este laboratório."
                    : LaboratorioSelecionado.Observacao;
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
                var filtro = TemPesquisa ? $" · {ProgramasVisiveis.Count} exibido(s) pela pesquisa" : string.Empty;
                return $"{BlocoSelecionado?.Nome}{sala} · {Programas.Count} programa(s){filtro}";
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

            // Contexto da tela capturado aqui (o comando roda na thread da UI). O do construtor pode ser nulo:
            // no .exe o ViewModel nasce em Program.Main, antes de app.Run instalar o contexto do WPF.
            var ui = SynchronizationContext.Current ?? _ui;

            foreach (var item in Programas)
            {
                item.Estado = EstadoInstalacao.Pendente;
                item.Mensagem = null;
            }

            LimparErro();
            _itensDaFila = itens;
            AtualizarContador();

            var porPrograma = itens.ToDictionary(i => i.Programa, i => i);
            var cancelamento = new CancellationTokenSource();
            _cancelamento = cancelamento;
            Ocupado = true;
            Progresso = 0;
            StatusTexto = $"Instalando {itens.Count} programa(s) em {Titulo}…";
            AdicionarLog($"Início: {itens.Count} programa(s) de {BlocoSelecionado?.Id}/{LaboratorioSelecionado?.Id}.");

            IReadOnlyList<InstallResult> resultados = Array.Empty<InstallResult>();
            var programas = itens.Select(i => i.Programa).ToList();
            try
            {
                // Task.Run: a checagem dos arquivos na rede e o Process.Start também saem da thread da tela.
                // Cada aviso de andamento volta para a UI por NaUi (listas e contador só mudam na thread da tela).
                resultados = await Task.Run(() => _fila.ExecutarAsync(
                    programas,
                    p => NaUi(ui, () => AplicarProgresso(p, porPrograma)),
                    cancelamento.Token));
            }
            catch (Exception ex)
            {
                AdicionarLog($"Erro inesperado: {ex.Message}");
                foreach (var item in itens.Where(i => !i.Concluido))
                {
                    item.Estado = EstadoInstalacao.Falha;
                    item.Mensagem = "Instalação interrompida: " + ex.Message;
                }
                MostrarErro("Erro inesperado na instalação: " + ex.Message);
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

        /// <summary>Acrescenta uma linha informativa ao registro (ex.: qual config foi carregado).</summary>
        public void RegistrarMensagem(string mensagem) => AdicionarLog(mensagem);

        private void AplicarProgresso(InstallProgress p, Dictionary<Programa, ProgramaItemViewModel> porPrograma)
        {
            if (porPrograma.TryGetValue(p.Programa, out var item))
            {
                item.Estado = p.Estado;
                item.Mensagem = p.Mensagem;
            }

            AtualizarContador();
            if (p.Estado == EstadoInstalacao.Instalando)
                StatusTexto = $"[{p.Indice}/{p.Total}] Instalando {p.Programa.Nome}…";
            AdicionarLog($"[{p.Indice}/{p.Total}] {p.Programa.Nome}: {p.Mensagem}");
        }

        private void Concluir(IReadOnlyList<InstallResult> resultados)
        {
            // O resumo sai dos itens da fila (a mesma fonte do contador), e não só da lista devolvida pela fila:
            // se ela abortar no meio, os itens interrompidos já foram marcados como falha.
            var cancelados = _itensDaFila.Count(i => i.Estado == EstadoInstalacao.Cancelado);
            var incompativeis = _itensDaFila.Count(i => i.Estado == EstadoInstalacao.Incompativel);
            AtualizarContador();
            Progresso = 100;
            StatusTexto = $"Concluído: {Instalados} instalado(s), {Falhas} falha(s), {cancelados} cancelado(s)."
                          + (incompativeis > 0 ? $" {incompativeis} incompatível(is) com este Windows." : string.Empty);
            AdicionarLog(StatusTexto);

            var falharam = _itensDaFila.Where(i => i.TemFalha).Select(i => i.Nome).ToList();
            if (falharam.Count > 0 && !TemErro)
                MostrarErro($"{falharam.Count} programa(s) falharam: {string.Join(", ", falharam)}. " +
                            "O motivo aparece em vermelho em cada item (e no Registro da instalação).");
            Ocupado = false;
        }

        private void AtualizarContador()
        {
            OnPropertyChanged(nameof(TotalNaFila));
            OnPropertyChanged(nameof(Concluidos));
            OnPropertyChanged(nameof(Instalados));
            OnPropertyChanged(nameof(Falhas));
            OnPropertyChanged(nameof(TemContador));
            OnPropertyChanged(nameof(TemFalhas));
            OnPropertyChanged(nameof(ContadorTexto));
            Progresso = TotalNaFila == 0 ? 0 : 100.0 * Concluidos / TotalNaFila;
        }

        private void MostrarErro(string mensagem, bool daRede = false)
        {
            _erroDaRede = daRede;
            MensagemErro = mensagem;
            AdicionarLog("ERRO: " + mensagem);
        }

        private void LimparErro()
        {
            _erroDaRede = false;
            MensagemErro = null;
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
                var item = new ProgramaItemViewModel(p, Ambiente);
                item.SelecaoAlterada += ItemSelecaoAlterada;
                Programas.Add(item);
            }

            // O contador fala da última fila; ao trocar de laboratório ele recomeça.
            _itensDaFila = Array.Empty<ProgramaItemViewModel>();
            AtualizarContador();

            if (lab != null) StatusTexto = $"{Programas.Count} programa(s) em {lab.Nome}. Marque os que deseja instalar.";
            AplicarFiltro();
        }

        /// <summary>Refaz a lista exibida a partir de Programas (LINQ), mantendo a ordem e os mesmos objetos.</summary>
        private void AplicarFiltro()
        {
            ProgramasVisiveis.Clear();
            foreach (var item in Programas.Where(i => i.Corresponde(SearchText))) ProgramasVisiveis.Add(item);

            OnPropertyChanged(nameof(TemPesquisa));
            OnPropertyChanged(nameof(TextoSelecionarTodos));
            OnPropertyChanged(nameof(MostrarAvisoVazio));
            OnPropertyChanged(nameof(TextoAvisoVazio));
            LimparPesquisaCommand.NotificarMudanca();
            NotificarSelecao();
        }

        /// <summary>Marca o que está na tela: sem pesquisa, o laboratório inteiro; com pesquisa, só os exibidos.</summary>
        private void SelecionarExibidos()
        {
            foreach (var item in ProgramasVisiveis) item.Selecionado = true;
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
            VerificarPastaRedeCommand.NotificarMudanca();
        }

        private void AdicionarLog(string linha) => Log.Add($"{DateTime.Now:HH:mm:ss}  {linha}");

        /// <summary>Roda na thread da tela: direto se já estiver nela (ou sem contexto, nos testes); senão, posta.</summary>
        private static void NaUi(SynchronizationContext ui, Action acao)
        {
            if (ui == null || SynchronizationContext.Current == ui) acao();
            else ui.Post(_ => acao(), null);
        }
    }
}
