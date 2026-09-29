using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WinAllApp.Core.Services.Padronizacao
{
    /// <summary>Tópico 8: testes de rede e internet (só diagnóstico, não muda nada no sistema).</summary>
    public interface IDiagnosticoRede
    {
        Task<bool> RespondeAsync(string host, TimeSpan timeout, CancellationToken cancelamento);
        Task<bool> PastaAcessivelAsync(string pasta, TimeSpan timeout, CancellationToken cancelamento);
        Task<bool> InternetAsync(TimeSpan timeout, CancellationToken cancelamento);
    }

    /// <summary>Ping e TCP do próprio .NET, sem ferramentas externas.</summary>
    public sealed class DiagnosticoRede : IDiagnosticoRede
    {
        /// <summary>O mesmo endereço que o Windows usa para saber se há internet (NCSI).</summary>
        public const string HostInternet = "www.msftconnecttest.com";

        public async Task<bool> RespondeAsync(string host, TimeSpan timeout, CancellationToken cancelamento)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            try
            {
                using (var ping = new Ping())
                {
                    var resposta = await ping.SendPingAsync(host, (int)timeout.TotalMilliseconds).ConfigureAwait(false);
                    if (resposta.Status == IPStatus.Success) return true;
                }
            }
            catch (PingException) { }
            catch (SocketException) { }

            // Servidor com ICMP bloqueado: a porta do compartilhamento (SMB, 445) também serve de resposta.
            return await PortaAbertaAsync(host, 445, timeout, cancelamento).ConfigureAwait(false);
        }

        public async Task<bool> PastaAcessivelAsync(string pasta, TimeSpan timeout, CancellationToken cancelamento)
        {
            if (string.IsNullOrWhiteSpace(pasta)) return false;
            var consulta = Task.Run(() => Directory.Exists(pasta), cancelamento);
            if (await Task.WhenAny(consulta, Task.Delay(timeout, cancelamento)).ConfigureAwait(false) != consulta)
            {
                _ = consulta.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                return false;
            }
            return await consulta.ConfigureAwait(false);
        }

        public Task<bool> InternetAsync(TimeSpan timeout, CancellationToken cancelamento) =>
            PortaAbertaAsync(HostInternet, 80, timeout, cancelamento);

        public static async Task<bool> PortaAbertaAsync(string host, int porta, TimeSpan timeout, CancellationToken cancelamento)
        {
            using (var cliente = new TcpClient())
            {
                var conexao = cliente.ConnectAsync(host, porta);
                if (await Task.WhenAny(conexao, Task.Delay(timeout, cancelamento)).ConfigureAwait(false) != conexao)
                {
                    _ = conexao.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                    return false;
                }
                try
                {
                    await conexao.ConfigureAwait(false);
                    return cliente.Connected;
                }
                catch (SocketException) { return false; }
            }
        }

        /// <summary>"\\10.50.11.2\informatica\..." → "10.50.11.2"; null se não for caminho de rede.</summary>
        public static string ServidorDoCaminho(string caminho)
        {
            var texto = (caminho ?? string.Empty).Trim();
            if (!texto.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            var resto = texto.Substring(2);
            var fim = resto.IndexOf('\\');
            var host = fim < 0 ? resto : resto.Substring(0, fim);
            return host.Length == 0 ? null : host;
        }
    }
}
