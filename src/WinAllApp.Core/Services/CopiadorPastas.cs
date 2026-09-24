using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WinAllApp.Core.Services
{
    /// <summary>Cópia de pasta (abstraída para os testes).</summary>
    public interface ICopiadorPastas
    {
        /// <summary>Copia <paramref name="origem"/> para <paramref name="destino"/> (subpastas incluídas, sobrescrevendo) e devolve o nº de arquivos.</summary>
        Task<int> CopiarAsync(string origem, string destino, CancellationToken cancelamento);
    }

    /// <summary>
    /// Equivalente a "robocopy origem destino /E" feito em código: funciona igual do Windows 7 ao 11
    /// e não depende de ferramenta externa nem de escape de caminhos na linha de comando.
    /// </summary>
    public sealed class CopiadorPastas : ICopiadorPastas
    {
        public Task<int> CopiarAsync(string origem, string destino, CancellationToken cancelamento) =>
            Task.Run(() => Copiar(new DirectoryInfo(origem), destino, cancelamento), cancelamento);

        private static int Copiar(DirectoryInfo origem, string destino, CancellationToken cancelamento)
        {
            if (!origem.Exists) throw new DirectoryNotFoundException("Pasta não encontrada: " + origem.FullName);
            Directory.CreateDirectory(destino);

            var total = 0;
            foreach (var arquivo in origem.GetFiles())
            {
                cancelamento.ThrowIfCancellationRequested();
                var alvo = Path.Combine(destino, arquivo.Name);
                if (File.Exists(alvo)) File.SetAttributes(alvo, FileAttributes.Normal);
                arquivo.CopyTo(alvo, overwrite: true);
                total++;
            }

            foreach (var sub in origem.GetDirectories())
                total += Copiar(sub, Path.Combine(destino, sub.Name), cancelamento);

            return total;
        }
    }
}
