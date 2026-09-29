using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinAllApp.Core.Services.Padronizacao
{
    public sealed class ResultadoPowerShell
    {
        public ResultadoPowerShell(int codigo, string saida)
        {
            Codigo = codigo;
            Saida = saida ?? string.Empty;
        }

        public int Codigo { get; }
        public string Saida { get; }
    }

    /// <summary>Roda um script do Windows PowerShell (abstraído para os testes, que não executam nada no sistema).</summary>
    public interface IExecutorPowerShell
    {
        Task<ResultadoPowerShell> ExecutarAsync(string script, IReadOnlyDictionary<string, string> variaveis,
            TimeSpan timeout, CancellationToken cancelamento);
    }

    /// <summary>
    /// powershell.exe -EncodedCommand: o script vai em Base64 (UTF-16), então aspas e caracteres acentuados passam intactos.
    /// Os valores digitados vão como variáveis de ambiente só deste processo.
    /// </summary>
    public sealed class ExecutorPowerShell : IExecutorPowerShell
    {
        public static string CaminhoPowerShell()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            // Processo de 32 bits num Windows de 64 bits: "Sysnative" evita o redirecionamento para o SysWOW64.
            var sistema = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32";
            return Path.Combine(windows, sistema, "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        public static string Argumentos(string script) =>
            "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "
            + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        public async Task<ResultadoPowerShell> ExecutarAsync(string script, IReadOnlyDictionary<string, string> variaveis,
            TimeSpan timeout, CancellationToken cancelamento)
        {
            var info = new ProcessStartInfo(CaminhoPowerShell(), Argumentos(script))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };
            if (variaveis != null)
                foreach (var par in variaveis)
                    info.EnvironmentVariables[par.Key] = par.Value ?? string.Empty;

            var saida = new StringBuilder();
            using (var processo = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                var fim = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                processo.OutputDataReceived += (s, e) => { if (e.Data != null) lock (saida) saida.AppendLine(e.Data); };
                processo.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (saida) saida.AppendLine(e.Data); };
                processo.Exited += (s, e) => fim.TrySetResult(true);

                processo.Start();
                processo.BeginOutputReadLine();
                processo.BeginErrorReadLine();

                using (var limite = CancellationTokenSource.CreateLinkedTokenSource(cancelamento))
                {
                    limite.CancelAfter(timeout);
                    var espera = Task.Delay(Timeout.Infinite, limite.Token);
                    if (await Task.WhenAny(fim.Task, espera).ConfigureAwait(false) != fim.Task)
                    {
                        try { if (!processo.HasExited) processo.Kill(); }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        cancelamento.ThrowIfCancellationRequested();
                        throw new TimeoutException($"O PowerShell não terminou em {timeout.TotalMinutes:0.#} min.");
                    }
                }

                // Garante que as últimas linhas da saída assíncrona foram lidas.
                processo.WaitForExit();
                lock (saida) return new ResultadoPowerShell(processo.ExitCode, saida.ToString());
            }
        }
    }
}

namespace WinAllApp.Core.Services.Padronizacao
{
    /// <summary>
    /// Modo simulação (--simulacao): não executa nada no sistema, só devolve sucesso para cada script,
    /// para treinar o uso da tela sem mexer no computador.
    /// </summary>
    public sealed class ExecutorPowerShellSimulado : IExecutorPowerShell
    {
        public TimeSpan Atraso { get; set; } = TimeSpan.FromMilliseconds(300);

        public async Task<ResultadoPowerShell> ExecutarAsync(string script, IReadOnlyDictionary<string, string> variaveis,
            TimeSpan timeout, CancellationToken cancelamento)
        {
            await Task.Delay(Atraso, cancelamento).ConfigureAwait(false);
            return new ResultadoPowerShell(0, "RESULTADO: [SIMULAÇÃO] nada foi alterado neste computador.");
        }
    }

    /// <summary>Rede sempre disponível, para a simulação.</summary>
    public sealed class DiagnosticoRedeSimulado : IDiagnosticoRede
    {
        public Task<bool> RespondeAsync(string host, TimeSpan timeout, CancellationToken cancelamento) => Task.FromResult(true);
        public Task<bool> PastaAcessivelAsync(string pasta, TimeSpan timeout, CancellationToken cancelamento) => Task.FromResult(true);
        public Task<bool> InternetAsync(TimeSpan timeout, CancellationToken cancelamento) => Task.FromResult(true);
    }
}
