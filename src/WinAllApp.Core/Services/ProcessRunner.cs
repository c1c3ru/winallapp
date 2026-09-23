using System;
using System.Diagnostics;
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

            var info = new ProcessStartInfo(comando.Arquivo, comando.Argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = comando.PastaTrabalho ?? Environment.CurrentDirectory
            };

            var processo = new Process { StartInfo = info, EnableRaisingEvents = true };
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
