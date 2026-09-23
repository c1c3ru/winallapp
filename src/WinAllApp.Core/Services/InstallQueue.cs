using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Models;

namespace WinAllApp.Core.Services
{
    public enum EstadoInstalacao
    {
        Pendente,
        Instalando,
        Sucesso,
        SucessoReiniciar,
        Falha,
        Cancelado,
        /// <summary>A versão configurada não roda neste Windows (ex.: Python 3.9+ no Windows 7).</summary>
        Incompativel
    }

    public sealed class InstallProgress
    {
        public InstallProgress(int indice, int total, Programa programa, EstadoInstalacao estado, string mensagem, int? codigoSaida = null)
        {
            Indice = indice;
            Total = total;
            Programa = programa;
            Estado = estado;
            Mensagem = mensagem;
            CodigoSaida = codigoSaida;
        }

        /// <summary>Posição (base 1) do programa na fila.</summary>
        public int Indice { get; }
        public int Total { get; }
        public Programa Programa { get; }
        public EstadoInstalacao Estado { get; }
        public string Mensagem { get; }
        public int? CodigoSaida { get; }
    }

    public sealed class InstallResult
    {
        public InstallResult(Programa programa, EstadoInstalacao estado, int? codigoSaida, string mensagem)
        {
            Programa = programa;
            Estado = estado;
            CodigoSaida = codigoSaida;
            Mensagem = mensagem;
        }

        public Programa Programa { get; }
        public EstadoInstalacao Estado { get; }
        public int? CodigoSaida { get; }
        public string Mensagem { get; }
        public bool Sucesso => Estado == EstadoInstalacao.Sucesso || Estado == EstadoInstalacao.SucessoReiniciar;
    }

    /// <summary>
    /// Executa os instaladores escolhidos um de cada vez (instaladores MSI não rodam em paralelo),
    /// sem bloquear a UI, reportando o andamento de cada item.
    /// </summary>
    public sealed class InstallQueue
    {
        private static readonly int[] CodigosSucessoPadrao = { 0, 1641, 3010 };
        private static readonly int[] CodigosReiniciar = { 1641, 3010 };

        private readonly IProcessRunner _runner;
        private readonly ICopiadorPastas _copiador;
        private readonly RoteadorInstalacao _roteador;
        private readonly string _pastaInstaladores;

        /// <summary>Compatibilidade: todos os programas vêm da pasta de rede, sem gerenciadores de pacote.</summary>
        public InstallQueue(IProcessRunner runner, string pastaInstaladores)
            : this(runner, new CopiadorPastas(), null)
        {
            _pastaInstaladores = pastaInstaladores;
        }

        public InstallQueue(IProcessRunner runner, ICopiadorPastas copiador, RoteadorInstalacao roteador)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _copiador = copiador ?? throw new ArgumentNullException(nameof(copiador));
            _roteador = roteador;
        }

        public TimeSpan TimeoutPadrao { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>Permite pular a checagem de existência do instalador (útil em testes com runner falso).</summary>
        public bool VerificarArquivoExiste { get; set; } = true;

        /// <summary>O roteador em uso (cria um padrão, sem gerenciadores, quando a fila foi montada só com a pasta).</summary>
        public RoteadorInstalacao Roteador => _roteador ?? CriarRoteadorPadrao();

        private RoteadorInstalacao CriarRoteadorPadrao()
        {
            var contexto = new ContextoInstalacao(_pastaInstaladores, null, new AmbienteSistema());
            if (!VerificarArquivoExiste)
            {
                contexto.ArquivoExiste = _ => true;
                contexto.PastaExiste = _ => true;
            }
            return new RoteadorInstalacao(contexto);
        }

        public async Task<IReadOnlyList<InstallResult>> ExecutarAsync(
            IEnumerable<Programa> programas,
            Action<InstallProgress> progresso,
            CancellationToken cancelamento)
        {
            var fila = (programas ?? Enumerable.Empty<Programa>()).ToList();
            var resultados = new List<InstallResult>(fila.Count);
            var roteador = Roteador;

            for (var i = 0; i < fila.Count; i++)
            {
                var programa = fila[i];
                var indice = i + 1;

                if (cancelamento.IsCancellationRequested)
                {
                    resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Cancelado, "Cancelado antes de iniciar.", null));
                    continue;
                }

                PlanoInstalacao plano;
                try
                {
                    plano = roteador.Planejar(programa);
                }
                catch (Exception ex)
                {
                    resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Falha, "Erro ao planejar: " + ex.Message, null));
                    continue;
                }

                switch (plano.Acao)
                {
                    case AcaoInstalacao.Incompativel:
                        resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Incompativel, plano.Motivo, null));
                        continue;
                    case AcaoInstalacao.Bloqueado:
                        resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Falha, plano.Motivo, null));
                        continue;
                }

                progresso?.Invoke(new InstallProgress(indice, fila.Count, programa, EstadoInstalacao.Instalando, "Executando: " + plano.Descricao));

                try
                {
                    resultados.Add(plano.Acao == AcaoInstalacao.CopiarPasta
                        ? await CopiarAsync(plano, progresso, indice, fila.Count, cancelamento).ConfigureAwait(false)
                        : await ExecutarProcessoAsync(plano, progresso, indice, fila.Count, cancelamento).ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Cancelado, "Instalação cancelada.", null));
                }
                catch (Exception ex)
                {
                    resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Falha, ex.Message, null));
                }
            }

            return resultados;
        }

        private async Task<InstallResult> ExecutarProcessoAsync(PlanoInstalacao plano, Action<InstallProgress> progresso, int indice, int total,
            CancellationToken cancelamento)
        {
            var programa = plano.Programa;
            var timeout = programa.TimeoutMinutos > 0 ? TimeSpan.FromMinutes(programa.TimeoutMinutos) : TimeoutPadrao;
            var codigo = await _runner.ExecutarAsync(plano.Comando, timeout, cancelamento).ConfigureAwait(false);

            // Os códigos do config valem para o instalador da rede; winget/choco usam os padrões.
            var aceitos = plano.Fonte == FonteInstalacao.Rede && programa.CodigosSucesso != null && programa.CodigosSucesso.Count > 0
                ? programa.CodigosSucesso.AsEnumerable()
                : CodigosSucessoPadrao;
            var lembrete = string.IsNullOrEmpty(plano.Motivo) ? string.Empty : " " + plano.Motivo;
            var via = plano.Fonte == FonteInstalacao.Rede ? "pasta de rede" : plano.Fonte.ToString();

            if (plano.CodigosSucessoExtras.Contains(codigo))
                return Reportar(progresso, indice, total, programa, EstadoInstalacao.Sucesso, $"Já estava instalado ({via}, código {codigo}).{lembrete}", codigo);
            if (!aceitos.Contains(codigo))
                return Reportar(progresso, indice, total, programa, EstadoInstalacao.Falha, $"Falhou com código {codigo} ({via}).", codigo);
            if (CodigosReiniciar.Contains(codigo))
                return Reportar(progresso, indice, total, programa, EstadoInstalacao.SucessoReiniciar, $"Instalado via {via} (código {codigo}: reinicialização necessária).{lembrete}", codigo);
            return Reportar(progresso, indice, total, programa, EstadoInstalacao.Sucesso, $"Instalado via {via} (código {codigo}).{lembrete}", codigo);
        }

        private async Task<InstallResult> CopiarAsync(PlanoInstalacao plano, Action<InstallProgress> progresso, int indice, int total,
            CancellationToken cancelamento)
        {
            var arquivos = await _copiador.CopiarAsync(plano.Origem, plano.Destino, cancelamento).ConfigureAwait(false);
            return Reportar(progresso, indice, total, plano.Programa, EstadoInstalacao.Sucesso,
                $"Pasta copiada para {plano.Destino} ({arquivos} arquivo(s)).", 0);
        }

        private static InstallResult Reportar(Action<InstallProgress> progresso, int indice, int total, Programa programa,
            EstadoInstalacao estado, string mensagem, int? codigo)
        {
            progresso?.Invoke(new InstallProgress(indice, total, programa, estado, mensagem, codigo));
            return new InstallResult(programa, estado, codigo, mensagem);
        }
    }
}
