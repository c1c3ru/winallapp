using System;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace WinAllApp.Core.Services
{
    /// <summary>
    /// O que o motor precisa saber da máquina para escolher o caminho de instalação:
    /// versão do Windows, pré-requisitos do Chocolatey (.NET 4.8 e TLS 1.2) e onde estão o winget e o choco.
    /// </summary>
    public sealed class AmbienteSistema
    {
        public static readonly Version Windows7 = new Version(6, 1);
        public static readonly Version Windows81 = new Version(6, 3);
        public static readonly Version Windows10 = new Version(10, 0);

        /// <summary>Primeiro build do Windows 10 com suporte ao winget (1809).</summary>
        public const int BuildMinimoWinget = 17763;

        public Version VersaoWindows { get; set; } = Windows10;
        public int BuildWindows { get; set; } = 19045;
        public bool DotNet48Instalado { get; set; } = true;
        public bool Tls12Ativo { get; set; } = true;

        /// <summary>Caminho do winget.exe (ou de um substituto na simulação); null quando não existe.</summary>
        public string CaminhoWinget { get; set; }

        /// <summary>Caminho do choco.exe (ou de um substituto na simulação); null quando não existe.</summary>
        public string CaminhoChoco { get; set; }

        /// <summary>Verdadeiro quando o ambiente foi forçado (--simular-windows) em vez de detectado.</summary>
        public bool Simulado { get; set; }

        /// <summary>Windows 10 ou 11: usa o winget. Abaixo disso (7, 8, 8.1): Chocolatey.</summary>
        public bool UsaWinget => VersaoWindows >= Windows10;

        public bool WingetDisponivel => UsaWinget && BuildWindows >= BuildMinimoWinget && !string.IsNullOrEmpty(CaminhoWinget);

        public bool ChocoDisponivel => !string.IsNullOrEmpty(CaminhoChoco);

        /// <summary>Chocolatey 2.x exige .NET 4.8 e downloads por TLS 1.2.</summary>
        public bool PreRequisitosChocoOk => DotNet48Instalado && Tls12Ativo;

        public string NomeWindows
        {
            get
            {
                if (VersaoWindows >= Windows10) return BuildWindows >= 22000 ? "Windows 11" : "Windows 10";
                if (VersaoWindows >= Windows81) return "Windows 8.1";
                if (VersaoWindows >= new Version(6, 2)) return "Windows 8";
                if (VersaoWindows >= Windows7) return "Windows 7";
                return "Windows " + VersaoWindows;
            }
        }

        /// <summary>Compara com o "windowsMinimo" do config ("7", "8.1", "10" ou "11").</summary>
        public bool Atende(string windowsMinimo)
        {
            var minimo = ConverterVersao(windowsMinimo);
            if (minimo == null) return true;
            if (minimo.Major == 11) return VersaoWindows >= Windows10 && BuildWindows >= 22000;
            return VersaoWindows >= minimo;
        }

        /// <summary>"7" → 6.1, "8" → 6.2, "8.1" → 6.3, "10"/"11" → 10.0 (11 marcado com Major 11). null se vazio ou inválido.</summary>
        public static Version ConverterVersao(string windows)
        {
            switch ((windows ?? string.Empty).Trim().ToLowerInvariant().Replace("windows", string.Empty).Trim())
            {
                case "": return null;
                case "7": return Windows7;
                case "8": return new Version(6, 2);
                case "8.1": return Windows81;
                case "10": return Windows10;
                case "11": return new Version(11, 0);
                default: return null;
            }
        }

        public string Resumo()
        {
            var texto = $"{NomeWindows} (versão {VersaoWindows}, build {BuildWindows})" + (Simulado ? " [SIMULADO]" : string.Empty);
            if (UsaWinget)
                return texto + (WingetDisponivel ? " · winget encontrado" : " · winget NÃO encontrado (instale o \"Instalador de Aplicativo\" da Microsoft Store)");

            return texto
                   + $" · .NET 4.8: {(DotNet48Instalado ? "sim" : "NÃO")}"
                   + $" · TLS 1.2: {(Tls12Ativo ? "ativo" : "DESATIVADO")}"
                   + $" · Chocolatey: {(ChocoDisponivel ? "encontrado" : "não encontrado")}";
        }

        /// <summary>Ambiente fictício para simulação e testes. <paramref name="windows"/>: "7", "8.1", "10" ou "11".</summary>
        public static AmbienteSistema Simular(string windows, string caminhoWinget = null, string caminhoChoco = null,
            bool dotNet48 = true, bool tls12 = true)
        {
            var versao = ConverterVersao(windows) ?? throw new ArgumentException("Windows desconhecido: " + windows, nameof(windows));
            var onze = versao.Major == 11;
            return new AmbienteSistema
            {
                VersaoWindows = onze ? Windows10 : versao,
                BuildWindows = onze ? 22631 : versao >= Windows10 ? 19045 : versao == Windows7 ? 7601 : 9600,
                DotNet48Instalado = dotNet48,
                Tls12Ativo = tls12,
                CaminhoWinget = caminhoWinget,
                CaminhoChoco = caminhoChoco,
                Simulado = true
            };
        }
    }

    /// <summary>Leitura de valores de HKEY_LOCAL_MACHINE (abstraída para os testes).</summary>
    public interface ILeitorRegistro
    {
        object Ler(string chave, string valor);
    }

    public sealed class LeitorRegistroWindows : ILeitorRegistro
    {
        public object Ler(string chave, string valor)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(chave))
                    return k?.GetValue(valor);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is System.Security.SecurityException
                                       || ex is UnauthorizedAccessException || ex is IOException)
            {
                return null;
            }
        }
    }

    /// <summary>Detecta o ambiente real pelo registro do Windows.</summary>
    public static class DetectorAmbiente
    {
        public const string ChaveVersao = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        public const string ChaveDotNet = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full";
        public const string ChaveTls12Cliente = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\TLS 1.2\Client";

        /// <summary>Valor "Release" do .NET Framework 4.8 (qualquer Windows).</summary>
        public const int ReleaseDotNet48 = 528040;

        public static AmbienteSistema Detectar() =>
            Detectar(new LeitorRegistroWindows(), Environment.GetEnvironmentVariable, File.Exists);

        public static AmbienteSistema Detectar(ILeitorRegistro registro, Func<string, string> variavel, Func<string, bool> arquivoExiste)
        {
            var versao = LerVersaoWindows(registro, out var build);
            return new AmbienteSistema
            {
                VersaoWindows = versao,
                BuildWindows = build,
                DotNet48Instalado = DotNet48Instalado(registro),
                Tls12Ativo = Tls12Ativo(registro, versao),
                CaminhoWinget = LocalizarWinget(variavel, arquivoExiste),
                CaminhoChoco = LocalizarChoco(variavel, arquivoExiste)
            };
        }

        public static Version LerVersaoWindows(ILeitorRegistro registro, out int build)
        {
            int.TryParse(Convert.ToString(registro.Ler(ChaveVersao, "CurrentBuildNumber"), CultureInfo.InvariantCulture), out build);

            // Windows 10/11 gravam a versão em DWORDs; o "CurrentVersion" deles continua "6.3" por compatibilidade.
            if (registro.Ler(ChaveVersao, "CurrentMajorVersionNumber") is int maior)
            {
                var menor = registro.Ler(ChaveVersao, "CurrentMinorVersionNumber") is int m ? m : 0;
                return new Version(maior, menor);
            }

            if (Version.TryParse(Convert.ToString(registro.Ler(ChaveVersao, "CurrentVersion"), CultureInfo.InvariantCulture), out var antiga))
                return antiga;

            // Fora do Windows (testes): assume Windows 10.
            if (build == 0) build = 19045;
            return AmbienteSistema.Windows10;
        }

        public static bool DotNet48Instalado(ILeitorRegistro registro) =>
            registro.Ler(ChaveDotNet, "Release") is int release && release >= ReleaseDotNet48;

        /// <summary>
        /// TLS 1.2 no cliente SChannel. No Windows 7 ele vem desligado: é preciso
        /// DisabledByDefault=0 na chave "TLS 1.2\Client" (atualização KB3140245 + ajuste no registro).
        /// Do Windows 8 em diante vem ligado, a não ser que alguém o tenha desativado.
        /// </summary>
        public static bool Tls12Ativo(ILeitorRegistro registro, Version versaoWindows)
        {
            var habilitado = registro.Ler(ChaveTls12Cliente, "Enabled");
            var desligadoPorPadrao = registro.Ler(ChaveTls12Cliente, "DisabledByDefault");

            if (habilitado is int h && h == 0) return false;
            if (versaoWindows < new Version(6, 2)) return desligadoPorPadrao is int d0 && d0 == 0;
            return !(desligadoPorPadrao is int d && d != 0);
        }

        public static string LocalizarWinget(Func<string, string> variavel, Func<string, bool> arquivoExiste)
        {
            var localAppData = variavel("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                var alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
                if (arquivoExiste(alias)) return alias;
            }
            return NoPath("winget.exe", variavel, arquivoExiste);
        }

        public static string LocalizarChoco(Func<string, string> variavel, Func<string, bool> arquivoExiste)
        {
            foreach (var raiz in new[] { variavel("ChocolateyInstall"), Combinar(variavel("ProgramData"), "chocolatey") })
            {
                if (string.IsNullOrEmpty(raiz)) continue;
                var choco = Path.Combine(raiz, "bin", "choco.exe");
                if (arquivoExiste(choco)) return choco;
            }
            return NoPath("choco.exe", variavel, arquivoExiste);
        }

        private static string NoPath(string exe, Func<string, string> variavel, Func<string, bool> arquivoExiste)
        {
            foreach (var pasta in (variavel("PATH") ?? string.Empty).Split(';'))
            {
                var p = pasta.Trim().Trim('"');
                if (p.Length == 0) continue;
                string candidato;
                try { candidato = Path.Combine(p, exe); }
                catch (ArgumentException) { continue; }
                if (arquivoExiste(candidato)) return candidato;
            }
            return null;
        }

        private static string Combinar(string a, string b) => string.IsNullOrEmpty(a) ? null : Path.Combine(a, b);
    }
}
