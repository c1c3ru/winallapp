using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>Runner falso: registra os comandos que seriam passados ao Process.Start.</summary>
    public sealed class RunnerFalso : IProcessRunner
    {
        private readonly Func<InstallCommand, int> _codigo;

        public RunnerFalso(Func<InstallCommand, int> codigo = null) => _codigo = codigo ?? (_ => 0);

        public ConcurrentQueue<InstallCommand> Comandos { get; } = new ConcurrentQueue<InstallCommand>();

        /// <summary>Quando definido, cada execução só termina depois que este sinal for liberado.</summary>
        public TaskCompletionSource<bool> Portao { get; set; }

        public int EmExecucao;

        public async Task<int> ExecutarAsync(InstallCommand comando, TimeSpan timeout, CancellationToken cancelamento)
        {
            Comandos.Enqueue(comando);
            Interlocked.Increment(ref EmExecucao);
            try
            {
                if (Portao != null)
                {
                    using (cancelamento.Register(() => Portao.TrySetCanceled()))
                        await Portao.Task.ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
                return _codigo(comando);
            }
            finally
            {
                Interlocked.Decrement(ref EmExecucao);
            }
        }
    }

    /// <summary>Teste que só roda no Windows (ex.: executa .bat de verdade).</summary>
    public sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Skip = "Requer Windows (executa instaladores .bat reais via cmd.exe).";
        }
    }

    public static class Dados
    {
        public static string Pasta => Path.Combine(AppContext.BaseDirectory, "TestData");

        public static ConfigLoadResult CarregarConfigTeste() =>
            ConfigLoader.CarregarArquivo(Path.Combine(Pasta, "config.teste.json"));

        public static string NovaPastaTemporaria()
        {
            var pasta = Path.Combine(Path.GetTempPath(), "winallapp-testes", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(pasta);
            return pasta;
        }

        public static bool Windows => OperatingSystem.IsWindows();

        /// <summary>
        /// Cria um instalador fictício que registra "nome argumentos" em log.txt e sai com o código pedido.
        /// No Windows é um .bat (tipo "bat", via cmd.exe); nos demais SOs um script .sh executável (tipo "exe").
        /// </summary>
        public static Programa CriarInstaladorFicticio(string pasta, string id, string argumentos, int codigoSaida = 0, int segundos = 0)
        {
            var log = Path.Combine(pasta, "log.txt");
            if (OperatingSystem.IsWindows())
            {
                var arquivo = Path.Combine(pasta, id + "-setup.bat");
                var espera = segundos > 0 ? $"ping -n {segundos + 1} 127.0.0.1 >nul\r\n" : string.Empty;
                File.WriteAllText(arquivo,
                    "@echo off\r\n" +
                    $"echo {id} %*>> \"{log}\"\r\n" +
                    espera +
                    $"exit /b {codigoSaida}\r\n");
                return new Programa { Id = id, Nome = id, Instalador = Path.GetFileName(arquivo), Tipo = "bat", Argumentos = argumentos };
            }
            else
            {
                var arquivo = Path.Combine(pasta, id + "-setup.sh");
                var espera = segundos > 0 ? $"sleep {segundos}\n" : string.Empty;
                File.WriteAllText(arquivo,
                    "#!/bin/sh\n" +
                    $"echo \"{id} $*\" >> \"{log}\"\n" +
                    espera +
                    $"exit {codigoSaida}\n");
                File.SetUnixFileMode(arquivo, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                return new Programa { Id = id, Nome = id, Instalador = Path.GetFileName(arquivo), Tipo = "exe", Argumentos = argumentos };
            }
        }

        public static List<string> LerLog(string pasta)
        {
            var log = Path.Combine(pasta, "log.txt");
            var linhas = new List<string>();
            if (!File.Exists(log)) return linhas;
            foreach (var l in File.ReadAllLines(log))
                if (!string.IsNullOrWhiteSpace(l)) linhas.Add(l.Trim());
            return linhas;
        }
    }
}
