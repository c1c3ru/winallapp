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
        Cancelado
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
        private readonly string _pastaInstaladores;

        public InstallQueue(IProcessRunner runner, string pastaInstaladores)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _pastaInstaladores = pastaInstaladores;
        }

        public TimeSpan TimeoutPadrao { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>Permite pular a checagem de existência do instalador (útil em testes com runner falso).</summary>
        public bool VerificarArquivoExiste { get; set; } = true;

        public async Task<IReadOnlyList<InstallResult>> ExecutarAsync(
            IEnumerable<Programa> programas,
            Action<InstallProgress> progresso,
            CancellationToken cancelamento)
        {
            var fila = (programas ?? Enumerable.Empty<Programa>()).ToList();
            var resultados = new List<InstallResult>(fila.Count);

            for (var i = 0; i < fila.Count; i++)
            {
                var programa = fila[i];
                var indice = i + 1;

                if (cancelamento.IsCancellationRequested)
                {
                    resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Cancelado, "Cancelado antes de iniciar.", null));
                    continue;
                }

                var comando = InstallCommandBuilder.Construir(programa, _pastaInstaladores);
                if (VerificarArquivoExiste && !File.Exists(comando.CaminhoInstalador))
                {
                    resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Falha,
                        $"Instalador não encontrado: {comando.CaminhoInstalador}", null));
                    continue;
                }

                progresso?.Invoke(new InstallProgress(indice, fila.Count, programa, EstadoInstalacao.Instalando, $"Executando: {comando}"));

                var timeout = programa.TimeoutMinutos > 0 ? TimeSpan.FromMinutes(programa.TimeoutMinutos) : TimeoutPadrao;
                try
                {
                    var codigo = await _runner.ExecutarAsync(comando, timeout, cancelamento).ConfigureAwait(false);
                    var aceitos = programa.CodigosSucesso != null && programa.CodigosSucesso.Count > 0
                        ? (IEnumerable<int>)programa.CodigosSucesso
                        : CodigosSucessoPadrao;

                    if (!aceitos.Contains(codigo))
                        resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Falha, $"Falhou com código {codigo}.", codigo));
                    else if (CodigosReiniciar.Contains(codigo))
                        resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.SucessoReiniciar, $"Instalado (código {codigo}: reinicialização necessária).", codigo));
                    else
                        resultados.Add(Reportar(progresso, indice, fila.Count, programa, EstadoInstalacao.Sucesso, $"Instalado (código {codigo}).", codigo));
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

        private static InstallResult Reportar(Action<InstallProgress> progresso, int indice, int total, Programa programa,
            EstadoInstalacao estado, string mensagem, int? codigo)
        {
            progresso?.Invoke(new InstallProgress(indice, total, programa, estado, mensagem, codigo));
            return new InstallResult(programa, estado, codigo, mensagem);
        }
    }
}
