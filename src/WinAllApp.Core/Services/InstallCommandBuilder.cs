using System;
using System.IO;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services.Padronizacao;

namespace WinAllApp.Core.Services
{
    public enum TipoInstalador
    {
        Exe,
        Msi,
        /// <summary>Arquivo em lotes (.bat/.cmd), via cmd.exe. Mantido por compatibilidade.</summary>
        Script,
        /// <summary>Script do Windows PowerShell (.ps1). Deve ser compatível com o PowerShell 2.0 do Windows 7.</summary>
        PowerShell
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

        /// <summary>Executável iniciado (o próprio instalador, msiexec.exe, powershell.exe ou cmd.exe).</summary>
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
            // "tipo" também aceita a categoria (ex.: "winget", "copia_pasta"): aí o tipo do arquivo vem da extensão.
            if (tipo != "exe" && tipo != "msi" && tipo != "bat" && tipo != "cmd" && tipo != "ps1" && tipo != "powershell")
                tipo = (Path.GetExtension(programa.Instalador ?? string.Empty) ?? string.Empty).TrimStart('.').ToLowerInvariant();

            switch (tipo)
            {
                case "msi": return TipoInstalador.Msi;
                case "bat":
                case "cmd": return TipoInstalador.Script;
                case "ps1":
                case "powershell": return TipoInstalador.PowerShell;
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

        /// <summary>Pasta local das cópias (copia_pasta): expande %VARIAVEIS% e resolve caminho relativo à pasta do config.json.</summary>
        public static string ResolverPastaDestinoCopias(InstallerConfig config, string pastaConfig)
        {
            var pasta = config.PastaDestinoCopias;
            if (string.IsNullOrWhiteSpace(pasta)) return ContextoInstalacao.PastaDestinoCopiasPadrao;
            pasta = Environment.ExpandEnvironmentVariables(pasta.Trim());
            if (Path.IsPathRooted(pasta) || string.IsNullOrWhiteSpace(pastaConfig)) return pasta;
            return Path.GetFullPath(Path.Combine(pastaConfig, pasta));
        }

        /// <summary>
        /// Linha do powershell.exe para rodar um .ps1. -File (e não -Command) devolve o "exit N" do script como código
        /// de saída, e existe desde o PowerShell 2.0 (Windows 7). -ExecutionPolicy Bypass vale só para este processo:
        /// não muda a política do computador e dispensa assinar os scripts da pasta de rede.
        /// </summary>
        public static InstallCommand ConstruirPowerShell(string caminho, string argumentos)
        {
            var args = (argumentos ?? string.Empty).Trim();
            var linha = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{caminho}\"";
            return new InstallCommand(ExecutorPowerShell.CaminhoPowerShell(), args.Length == 0 ? linha : linha + " " + args, caminho);
        }

        /// <summary>Comando de uma ferramenta (winget.exe, choco.exe). Na simulação o substituto é um .ps1 (ou .bat).</summary>
        public static InstallCommand ConstruirFerramenta(string caminho, string argumentos)
        {
            var extensao = (Path.GetExtension(caminho) ?? string.Empty).ToLowerInvariant();
            if (extensao == ".ps1") return ConstruirPowerShell(caminho, argumentos);
            if (extensao == ".bat" || extensao == ".cmd")
                return new InstallCommand("cmd.exe", $"/c \"\"{caminho}\" {argumentos}\"", caminho);
            return new InstallCommand(caminho, argumentos, caminho);
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

                case TipoInstalador.PowerShell:
                    return ConstruirPowerShell(caminho, args);

                default:
                    return new InstallCommand(caminho, args, caminho);
            }
        }
    }
}
