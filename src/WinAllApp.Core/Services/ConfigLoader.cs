using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Models;

namespace WinAllApp.Core.Services
{
    public sealed class ConfigLoadResult
    {
        public ConfigLoadResult(InstallerConfig config, string pastaConfig, IReadOnlyList<string> erros, IReadOnlyList<string> avisos)
        {
            Config = config;
            PastaConfig = pastaConfig;
            Erros = erros;
            Avisos = avisos;
        }

        public InstallerConfig Config { get; }

        /// <summary>Pasta do config.json, usada para resolver caminhos relativos.</summary>
        public string PastaConfig { get; }

        public IReadOnlyList<string> Erros { get; }
        public IReadOnlyList<string> Avisos { get; }
        public bool Valido => Config != null && Erros.Count == 0;
    }

    /// <summary>Resultado de uma checagem de arquivo ou pasta na rede.</summary>
    public enum StatusArquivo
    {
        Encontrado,
        NaoEncontrado,
        /// <summary>A rede não respondeu dentro do timeout (servidor desligado, VPN caída...).</summary>
        SemResposta,
        /// <summary>O acesso lançou exceção (ex.: acesso negado).</summary>
        Erro
    }

    /// <summary>O que conferir na rede para um programa: Caminho Base + caminho do JSON.</summary>
    public sealed class AlvoVerificacao
    {
        public AlvoVerificacao(Programa programa, string caminho, bool ehPasta, bool temAlternativa)
        {
            Programa = programa ?? throw new ArgumentNullException(nameof(programa));
            Caminho = caminho;
            EhPasta = ehPasta;
            TemAlternativa = temAlternativa;
        }

        public Programa Programa { get; }
        public string Caminho { get; }

        /// <summary>copia_pasta: confere uma pasta (Directory.Exists) e não um arquivo.</summary>
        public bool EhPasta { get; }

        /// <summary>"gerenciador" com wingetId/chocoId: mesmo fora da rede ainda instala pelo gerenciador de pacotes.</summary>
        public bool TemAlternativa { get; }
    }

    public sealed class ResultadoVerificacao
    {
        public ResultadoVerificacao(AlvoVerificacao alvo, string caminho, StatusArquivo status, string detalhe = null,
            bool pastaBaseInacessivel = false)
        {
            Alvo = alvo;
            Caminho = caminho;
            Status = status;
            Detalhe = detalhe;
            PastaBaseInacessivel = pastaBaseInacessivel;
        }

        /// <summary>Nulo quando a checagem foi de um caminho avulso (ex.: a própria pasta de rede).</summary>
        public AlvoVerificacao Alvo { get; }
        public string Caminho { get; }
        public StatusArquivo Status { get; }

        /// <summary>Mensagem da exceção quando <see cref="Status"/> é Erro.</summary>
        public string Detalhe { get; }

        /// <summary>O arquivo nem foi consultado: a pasta de rede inteira não respondeu ou não existe.</summary>
        public bool PastaBaseInacessivel { get; }

        public bool Encontrado => Status == StatusArquivo.Encontrado;
    }

    /// <summary>Lê e valida o config.json (usa apenas o serializador nativo do .NET, sem dependências externas).</summary>
    public static class ConfigLoader
    {
        public static ConfigLoadResult CarregarArquivo(string caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) throw new ArgumentException("Caminho do config não informado.", nameof(caminho));

            var completo = Path.GetFullPath(caminho);
            if (!File.Exists(completo))
                return Falha($"Arquivo de configuração não encontrado: {completo}", Path.GetDirectoryName(completo));

            return CarregarTexto(File.ReadAllText(completo, Encoding.UTF8), Path.GetDirectoryName(completo));
        }

        public static ConfigLoadResult CarregarTexto(string json, string pastaConfig)
        {
            InstallerConfig config;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(InstallerConfig));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? string.Empty)))
                {
                    config = (InstallerConfig)serializer.ReadObject(ms);
                }
            }
            catch (Exception ex)
            {
                return Falha($"config.json inválido: {ex.Message}", pastaConfig);
            }

            if (config == null) return Falha("config.json vazio.", pastaConfig);

            var erros = new List<string>();
            var avisos = new List<string>();
            Validar(config, erros, avisos);
            return new ConfigLoadResult(config, pastaConfig, erros, avisos);
        }

        private static void Validar(InstallerConfig config, List<string> erros, List<string> avisos)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in config.Programas)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) { erros.Add($"Programa \"{p.Nome}\" sem id."); continue; }
                if (!ids.Add(p.Id)) erros.Add($"Programa com id duplicado: {p.Id}.");
                if (!string.IsNullOrWhiteSpace(p.Categoria) && !CategoriaResolver.TentarConverter(p.Categoria, out _))
                    erros.Add($"Programa {p.Id} com categoria desconhecida: \"{p.Categoria}\" (use gerenciador, offline_licenciado, offline_gratuito ou copia_pasta).");
                if (!string.IsNullOrWhiteSpace(p.WindowsMinimo) && AmbienteSistema.ConverterVersao(p.WindowsMinimo) == null)
                    erros.Add($"Programa {p.Id} com windowsMinimo inválido: \"{p.WindowsMinimo}\" (use 7, 8.1, 10 ou 11).");

                var categoria = CategoriaResolver.Resolver(p);
                var temPacote = !string.IsNullOrWhiteSpace(p.WingetId) || !string.IsNullOrWhiteSpace(p.ChocoId);
                if (string.IsNullOrWhiteSpace(p.Instalador))
                {
                    if (categoria == CategoriaInstalacao.CopiaPasta) erros.Add($"Programa {p.Id} (cópia de pasta) sem a pasta de origem em \"instalador\".");
                    else if (categoria != CategoriaInstalacao.Gerenciador || !temPacote) erros.Add($"Programa {p.Id} sem caminho de instalador.");
                }
                else if (categoria != CategoriaInstalacao.CopiaPasta && string.IsNullOrWhiteSpace(p.Argumentos)
                         && InstallCommandBuilder.ResolverTipo(p) == TipoInstalador.Exe)
                {
                    avisos.Add($"Programa {p.Id} (.exe) sem argumentos silenciosos; a instalação pode abrir janelas.");
                }

                if (categoria == CategoriaInstalacao.Gerenciador && !temPacote)
                    avisos.Add($"Programa {p.Id} é \"gerenciador\" mas não tem wingetId nem chocoId; só a pasta de rede será usada.");
            }

            if (config.Blocos.Count == 0) erros.Add("Nenhum bloco definido.");

            var blocos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in config.Blocos)
            {
                if (string.IsNullOrWhiteSpace(b.Id)) { erros.Add($"Bloco \"{b.Nome}\" sem id."); continue; }
                if (!blocos.Add(b.Id)) erros.Add($"Bloco com id duplicado: {b.Id}.");
                if (b.Laboratorios.Count == 0) avisos.Add($"Bloco {b.Id} não tem laboratórios.");

                var labs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lab in b.Laboratorios)
                {
                    if (string.IsNullOrWhiteSpace(lab.Id)) { erros.Add($"Laboratório \"{lab.Nome}\" do bloco {b.Id} sem id."); continue; }
                    if (!labs.Add(lab.Id)) erros.Add($"Laboratório duplicado no bloco {b.Id}: {lab.Id}.");
                    foreach (var pid in lab.Programas.Where(pid => !ids.Contains(pid)))
                        erros.Add($"Laboratório {b.Id}/{lab.Id} referencia programa inexistente: {pid}.");
                }
            }
        }

        // ===== Autodescoberta: os arquivos do config.json existem na pasta de rede? =====

        /// <summary>Tempo máximo de cada consulta à rede antes de desistir ("sem resposta").</summary>
        public static readonly TimeSpan TimeoutVerificacaoPadrao = TimeSpan.FromSeconds(5);

        /// <summary>Quantas consultas à rede rodam ao mesmo tempo.</summary>
        public const int VerificacoesSimultaneas = 8;

        /// <summary>
        /// Caminho Base + caminho do JSON, pela mesma regra do roteador. Nulo quando o programa não tem nada na rede
        /// (ex.: "gerenciador" só com wingetId/chocoId). Não acessa o disco.
        /// </summary>
        public static AlvoVerificacao CaminhoNaRede(Programa programa, string pastaBase)
        {
            if (programa == null) throw new ArgumentNullException(nameof(programa));
            if (string.IsNullOrWhiteSpace(programa.Instalador)) return null;

            var categoria = CategoriaResolver.Resolver(programa);
            var temAlternativa = categoria == CategoriaInstalacao.Gerenciador
                                 && (!string.IsNullOrWhiteSpace(programa.WingetId) || !string.IsNullOrWhiteSpace(programa.ChocoId));
            string caminho;
            try
            {
                caminho = InstallCommandBuilder.ResolverCaminho(programa, pastaBase);
            }
            catch (Exception)
            {
                // Caractere inválido digitado no campo: o caminho vai como está e a checagem dá "não encontrado".
                caminho = (pastaBase ?? string.Empty).TrimEnd('\\', '/') + "\\" + programa.Instalador;
            }
            return new AlvoVerificacao(programa, caminho, categoria == CategoriaInstalacao.CopiaPasta, temAlternativa);
        }

        /// <summary>
        /// Roda <paramref name="existe"/> (File.Exists ou Directory.Exists) fora da thread que chamou e desiste depois de
        /// <paramref name="timeout"/>: um servidor desligado não prende ninguém. A consulta que passou do tempo continua
        /// sozinha em segundo plano e o resultado dela é descartado.
        /// </summary>
        public static async Task<ResultadoVerificacao> VerificarCaminhoAsync(string caminho, Func<string, bool> existe, TimeSpan timeout,
            CancellationToken cancelamento, AlvoVerificacao alvo = null)
        {
            if (existe == null) throw new ArgumentNullException(nameof(existe));
            cancelamento.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(caminho)) return new ResultadoVerificacao(alvo, caminho, StatusArquivo.NaoEncontrado);

            var busca = Task.Run(() => existe(caminho));
            using (var fimDaEspera = CancellationTokenSource.CreateLinkedTokenSource(cancelamento))
            {
                var espera = Task.Delay(timeout, fimDaEspera.Token);
                var primeira = await Task.WhenAny(busca, espera).ConfigureAwait(false);
                fimDaEspera.Cancel();
                if (primeira != busca)
                {
                    // Observa a exceção de uma consulta abandonada (senão ela aparece como "não observada").
                    busca.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                    cancelamento.ThrowIfCancellationRequested();
                    return new ResultadoVerificacao(alvo, caminho, StatusArquivo.SemResposta);
                }
            }

            try
            {
                var achou = await busca.ConfigureAwait(false);
                return new ResultadoVerificacao(alvo, caminho, achou ? StatusArquivo.Encontrado : StatusArquivo.NaoEncontrado);
            }
            catch (Exception ex)
            {
                return new ResultadoVerificacao(alvo, caminho, StatusArquivo.Erro, ex.Message);
            }
        }

        /// <summary>
        /// Confere todos os alvos em paralelo (<see cref="VerificacoesSimultaneas"/> por vez, Task.WhenAll), cada um com o
        /// próprio timeout, e avisa <paramref name="progresso"/> a cada resultado. Antes, testa a pasta base: se ela não
        /// responde ou não existe, os arquivos dentro dela já saem como não encontrados, sem uma consulta presa por arquivo.
        /// </summary>
        public static async Task<IReadOnlyList<ResultadoVerificacao>> VerificarCaminhosAsync(
            IEnumerable<AlvoVerificacao> alvos,
            string pastaBase,
            Func<string, bool> arquivoExiste,
            Func<string, bool> pastaExiste,
            TimeSpan timeoutPorCaminho,
            IProgress<ResultadoVerificacao> progresso,
            CancellationToken cancelamento)
        {
            if (arquivoExiste == null) throw new ArgumentNullException(nameof(arquivoExiste));
            if (pastaExiste == null) throw new ArgumentNullException(nameof(pastaExiste));
            var lista = (alvos ?? Enumerable.Empty<AlvoVerificacao>()).Where(a => a != null).ToList();
            if (lista.Count == 0) return Array.Empty<ResultadoVerificacao>();

            ResultadoVerificacao baseInacessivel = null;
            if (!string.IsNullOrWhiteSpace(pastaBase) && lista.Any(a => DentroDaPasta(a.Caminho, pastaBase)))
            {
                var daBase = await VerificarCaminhoAsync(pastaBase, pastaExiste, timeoutPorCaminho, cancelamento).ConfigureAwait(false);
                if (!daBase.Encontrado) baseInacessivel = daBase;
            }

            using (var vagas = new SemaphoreSlim(VerificacoesSimultaneas))
            {
                var tarefas = lista.Select(async alvo =>
                {
                    ResultadoVerificacao resultado;
                    if (baseInacessivel != null && DentroDaPasta(alvo.Caminho, pastaBase))
                    {
                        resultado = new ResultadoVerificacao(alvo, alvo.Caminho, baseInacessivel.Status, baseInacessivel.Detalhe, pastaBaseInacessivel: true);
                    }
                    else
                    {
                        await vagas.WaitAsync(cancelamento).ConfigureAwait(false);
                        try
                        {
                            resultado = await VerificarCaminhoAsync(alvo.Caminho, alvo.EhPasta ? pastaExiste : arquivoExiste,
                                timeoutPorCaminho, cancelamento, alvo).ConfigureAwait(false);
                        }
                        finally
                        {
                            vagas.Release();
                        }
                    }
                    progresso?.Report(resultado);
                    return resultado;
                }).ToList();

                return await Task.WhenAll(tarefas).ConfigureAwait(false);
            }
        }

        private static bool DentroDaPasta(string caminho, string pasta)
        {
            if (string.IsNullOrWhiteSpace(caminho) || string.IsNullOrWhiteSpace(pasta)) return false;
            string raiz;
            try
            {
                raiz = Path.GetFullPath(pasta.Trim()).TrimEnd('\\', '/');
            }
            catch (Exception)
            {
                raiz = pasta.Trim().TrimEnd('\\', '/');
            }
            return caminho.StartsWith(raiz + "\\", StringComparison.OrdinalIgnoreCase)
                   || caminho.StartsWith(raiz + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static ConfigLoadResult Falha(string erro, string pasta) =>
            new ConfigLoadResult(null, pasta, new[] { erro }, Array.Empty<string>());
    }
}
