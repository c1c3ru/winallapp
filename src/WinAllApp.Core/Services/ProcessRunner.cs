using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WinAllApp.Core.Services
{
    /// <summary>Abstração do disparo de processos, para a fila poder ser testada sem instaladores reais.</summary>
    public interface IProcessRunner
    {
        /// <summary>Executa o comando e devolve o código de saída sem bloquear a thread chamadora.</summary>
        Task<int> ExecutarAsync(InstallCommand comando, TimeSpan timeout, CancellationToken cancelamento);
    }

    /// <summary>Implementação real: Process.Start com espera assíncrona pelo evento Exited.</summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        public Task<int> ExecutarAsync(InstallCommand comando, TimeSpan timeout, CancellationToken cancelamento)
        {
            if (comando == null) throw new ArgumentNullException(nameof(comando));
            cancelamento.ThrowIfCancellationRequested();

            var processo = new Process { StartInfo = CriarInfo(comando), EnableRaisingEvents = true };
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            processo.Exited += (s, e) =>
            {
                int codigo;
                try { codigo = processo.ExitCode; }
                catch (Exception ex) { tcs.TrySetException(ex); return; }
                tcs.TrySetResult(codigo);
            };

            try
            {
                processo.Start();
            }
            catch
            {
                processo.Dispose();
                throw;
            }

            CancellationTokenSource timeoutCts = null;
            CancellationTokenSource combinado;
            if (timeout > TimeSpan.Zero)
            {
                timeoutCts = new CancellationTokenSource(timeout);
                combinado = CancellationTokenSource.CreateLinkedTokenSource(cancelamento, timeoutCts.Token);
            }
            else
            {
                combinado = CancellationTokenSource.CreateLinkedTokenSource(cancelamento);
            }

            var registro = combinado.Token.Register(() =>
            {
                // Define o resultado antes de encerrar, para o evento Exited do Kill não ser lido como sucesso.
                if (timeoutCts != null && timeoutCts.IsCancellationRequested && !cancelamento.IsCancellationRequested)
                    tcs.TrySetException(new TimeoutException($"Tempo limite de {timeout.TotalMinutes:0.#} min excedido."));
                else
                    tcs.TrySetCanceled();
                Encerrar(processo);
            });

            tcs.Task.ContinueWith(_ =>
            {
                registro.Dispose();
                combinado.Dispose();
                timeoutCts?.Dispose();
                processo.Dispose();
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        /// <summary>
        /// Monta o ProcessStartInfo. No Windows o executável vai entre aspas duplas ("\\10.50.11.2\...\Laboratórios - Programas\setup.exe"),
        /// para espaços em caminhos UNC não quebrarem a linha de comando do CreateProcess.
        /// </summary>
        public static ProcessStartInfo CriarInfo(InstallCommand comando)
        {
            if (comando == null) throw new ArgumentNullException(nameof(comando));
            var windows = Path.DirectorySeparatorChar == '\\';
            return new ProcessStartInfo(windows ? ComAspas(comando.Arquivo) : comando.Arquivo, comando.Argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = comando.PastaTrabalho ?? Environment.CurrentDirectory
            };
        }

        /// <summary>Envolve o caminho em aspas duplas (sem duplicar se já vier entre aspas).</summary>
        public static string ComAspas(string caminho)
        {
            var limpo = (caminho ?? string.Empty).Trim();
            if (limpo.Length >= 2 && limpo[0] == '"' && limpo[limpo.Length - 1] == '"') return limpo;
            return "\"" + limpo.Trim('"') + "\"";
        }

        private static void Encerrar(Process processo)
        {
            try
            {
                if (!processo.HasExited) processo.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}
