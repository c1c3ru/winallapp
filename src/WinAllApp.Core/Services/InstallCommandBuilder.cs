using System;
using System.IO;
using WinAllApp.Core.Models;

namespace WinAllApp.Core.Services
{
    public enum TipoInstalador
    {
        Exe,
        Msi,
        Script
    }

    /// <summary>Linha de comando pronta para Process.Start.</summary>
    public sealed class InstallCommand
    {
        public InstallCommand(string arquivo, string argumentos, string caminhoInstalador)
        {
            Arquivo = arquivo;
            Argumentos = argumentos ?? string.Empty;
            CaminhoInstalador = caminhoInstalador;
        }

        /// <summary>Executável iniciado (o próprio instalador, msiexec.exe ou cmd.exe).</summary>
        public string Arquivo { get; }

        public string Argumentos { get; }

        /// <summary>Caminho completo do instalador em disco (para checar se existe).</summary>
        public string CaminhoInstalador { get; }

        public string PastaTrabalho => Path.GetDirectoryName(CaminhoInstalador);

        public override string ToString() => string.IsNullOrEmpty(Argumentos) ? $"\"{Arquivo}\"" : $"\"{Arquivo}\" {Argumentos}";
    }

    /// <summary>Monta o comando silencioso de cada tipo de instalador.</summary>
    public static class InstallCommandBuilder
    {
        public const string ArgumentosMsiPadrao = "/qn /norestart";

        public static TipoInstalador ResolverTipo(Programa programa)
        {
            var tipo = (programa.Tipo ?? string.Empty).Trim().ToLowerInvariant();
            if (tipo.Length == 0)
                tipo = (Path.GetExtension(programa.Instalador ?? string.Empty) ?? string.Empty).TrimStart('.').ToLowerInvariant();

            switch (tipo)
            {
                case "msi": return TipoInstalador.Msi;
                case "bat":
                case "cmd": return TipoInstalador.Script;
                default: return TipoInstalador.Exe;
            }
        }

        public static string ResolverCaminho(Programa programa, string pastaInstaladores)
        {
            var instalador = programa.Instalador ?? string.Empty;
            if (Path.IsPathRooted(instalador) || string.IsNullOrWhiteSpace(pastaInstaladores)) return Path.GetFullPath(instalador);
            return Path.GetFullPath(Path.Combine(pastaInstaladores, instalador));
        }

        /// <summary>Resolve a pasta de instaladores do config (relativa à pasta do config.json quando não for absoluta).</summary>
        public static string ResolverPastaInstaladores(InstallerConfig config, string pastaConfig)
        {
            var pasta = config.PastaInstaladores;
            if (string.IsNullOrWhiteSpace(pasta)) return pastaConfig;
            if (Path.IsPathRooted(pasta) || string.IsNullOrWhiteSpace(pastaConfig)) return pasta;
            return Path.GetFullPath(Path.Combine(pastaConfig, pasta));
        }

        public static InstallCommand Construir(Programa programa, string pastaInstaladores)
        {
            if (programa == null) throw new ArgumentNullException(nameof(programa));

            var caminho = ResolverCaminho(programa, pastaInstaladores);
            var args = (programa.Argumentos ?? string.Empty).Trim();

            switch (ResolverTipo(programa))
            {
                case TipoInstalador.Msi:
                    if (args.Length == 0) args = ArgumentosMsiPadrao;
                    return new InstallCommand("msiexec.exe", $"/i \"{caminho}\" {args}", caminho);

                case TipoInstalador.Script:
                    // cmd /c ""C:\pasta com espaço\setup.bat" /args": as aspas externas preservam as internas.
                    var linha = args.Length == 0 ? $"\"{caminho}\"" : $"\"{caminho}\" {args}";
                    return new InstallCommand("cmd.exe", $"/c \"{linha}\"", caminho);

                default:
                    return new InstallCommand(caminho, args, caminho);
            }
        }
    }
}
