using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
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
                if (string.IsNullOrWhiteSpace(p.Instalador)) erros.Add($"Programa {p.Id} sem caminho de instalador.");
                else if (string.IsNullOrWhiteSpace(p.Argumentos) && InstallCommandBuilder.ResolverTipo(p) == TipoInstalador.Exe)
                    avisos.Add($"Programa {p.Id} (.exe) sem argumentos silenciosos; a instalação pode abrir janelas.");
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

        private static ConfigLoadResult Falha(string erro, string pasta) =>
            new ConfigLoadResult(null, pasta, new[] { erro }, Array.Empty<string>());
    }
}
