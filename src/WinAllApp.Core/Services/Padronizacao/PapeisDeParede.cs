using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinAllApp.Core.Services.Padronizacao
{
    public sealed class PapeisLocais
    {
        public string Adm { get; set; }
        public string Lab { get; set; }
        public string PastaDoAno { get; set; }
    }

    /// <summary>
    /// Tópico 6.1: pega "PapelParede – Adm" e "PapelParede - Lab" na pasta do ano mais recente e copia para o disco local.
    /// Nada é aberto direto do servidor de arquivos.
    /// </summary>
    public static class PapeisDeParede
    {
        public static readonly string[] ExtensoesImagem = { ".jpg", ".jpeg", ".png", ".bmp" };

        /// <summary>Imagens Públicas\WinAllApp (C:\Users\Public\Pictures\WinAllApp), legível por todas as contas.</summary>
        public static string PastaLocalPadrao()
        {
            var publico = Environment.GetEnvironmentVariable("PUBLIC");
            var imagens = string.IsNullOrEmpty(publico)
                ? Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures)
                : Path.Combine(publico, "Pictures");
            if (string.IsNullOrEmpty(imagens)) imagens = Path.GetTempPath();
            return Path.Combine(imagens, "WinAllApp");
        }

        /// <summary>Subpasta com o maior ano no começo do nome ("2025", "2025 - novo"...); null se nenhuma tiver ano.</summary>
        public static string PastaDoAnoMaisRecente(IEnumerable<string> subpastas)
        {
            return subpastas
                .Select(p => new { Caminho = p, Ano = Ano(UltimoNome(p)) })
                .Where(p => p.Ano > 0)
                .OrderByDescending(p => p.Ano)
                .ThenByDescending(p => p.Caminho, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.Caminho)
                .FirstOrDefault();
        }

        /// <summary>Último trecho do caminho, aceitando \ e / (caminhos UNC também nos testes fora do Windows).</summary>
        private static string UltimoNome(string caminho)
        {
            var limpo = (caminho ?? string.Empty).TrimEnd('\\', '/');
            return limpo.Substring(limpo.LastIndexOfAny(new[] { '\\', '/' }) + 1);
        }

        private static int Ano(string nome)
        {
            if (nome == null || nome.Length < 4) return 0;
            var inicio = nome.Substring(0, 4);
            if (!inicio.All(char.IsDigit)) return 0;
            if (nome.Length > 4 && char.IsDigit(nome[4])) return 0;
            var ano = int.Parse(inicio, CultureInfo.InvariantCulture);
            return ano >= 2000 && ano <= 2100 ? ano : 0;
        }

        /// <summary>
        /// Acha a imagem do tipo pedido ("Adm" ou "Lab") ignorando espaços, traços (-, –, —), acentos e maiúsculas:
        /// "PapelParede – Adm.jpg" e "Papel Parede-adm.PNG" servem.
        /// </summary>
        public static string Localizar(IEnumerable<string> arquivos, string tipo)
        {
            var alvo = "papelparede" + Normalizar(tipo);
            return arquivos
                .Where(a => ExtensoesImagem.Contains(Path.GetExtension(a).ToLowerInvariant()))
                .Where(a => Normalizar(Path.GetFileNameWithoutExtension(a)) == alvo)
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        public static string Normalizar(string texto)
        {
            var saida = new StringBuilder();
            foreach (var c in (texto ?? string.Empty).Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) saida.Append(char.ToLowerInvariant(c));
            }
            return saida.ToString();
        }

        /// <summary>
        /// Copia as imagens do ano mais recente para <paramref name="pastaLocal"/>. <paramref name="precisaLab"/> falso
        /// (conta Bolsista) dispensa a imagem "Lab". Roda fora da thread da tela e desiste se a rede não responder.
        /// </summary>
        public static async Task<PapeisLocais> CopiarAsync(string pastaRede, string pastaLocal, bool precisaLab,
            TimeSpan timeout, CancellationToken cancelamento)
        {
            var copia = Task.Run(() => Copiar(pastaRede, pastaLocal, precisaLab), cancelamento);
            var limite = Task.Delay(timeout, cancelamento);
            if (await Task.WhenAny(copia, limite).ConfigureAwait(false) != copia)
            {
                cancelamento.ThrowIfCancellationRequested();
                _ = copia.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                throw new TimeoutException($"A pasta dos papéis de parede não respondeu em {timeout.TotalSeconds:0} s: {pastaRede}");
            }
            return await copia.ConfigureAwait(false);
        }

        public static PapeisLocais Copiar(string pastaRede, string pastaLocal, bool precisaLab)
        {
            if (!Directory.Exists(pastaRede))
                throw new DirectoryNotFoundException("Pasta dos papéis de parede não encontrada: " + pastaRede);

            var ano = PastaDoAnoMaisRecente(Directory.GetDirectories(pastaRede))
                      ?? throw new DirectoryNotFoundException("Nenhuma pasta com ano (ex.: 2025) em: " + pastaRede);
            var arquivos = Directory.GetFiles(ano);

            var adm = Localizar(arquivos, "Adm") ?? throw new FileNotFoundException("Não há \"PapelParede – Adm\" em " + ano);
            var lab = Localizar(arquivos, "Lab");
            if (lab == null && precisaLab) throw new FileNotFoundException("Não há \"PapelParede - Lab\" em " + ano);

            Directory.CreateDirectory(pastaLocal);
            var resultado = new PapeisLocais { PastaDoAno = ano, Adm = CopiarUm(adm, pastaLocal, "PapelParede-Adm") };
            if (lab != null) resultado.Lab = CopiarUm(lab, pastaLocal, "PapelParede-Lab");
            return resultado;
        }

        private static string CopiarUm(string origem, string pastaLocal, string nome)
        {
            var destino = Path.Combine(pastaLocal, nome + Path.GetExtension(origem).ToLowerInvariant());
            File.Copy(origem, destino, overwrite: true);
            return destino;
        }
    }
}
