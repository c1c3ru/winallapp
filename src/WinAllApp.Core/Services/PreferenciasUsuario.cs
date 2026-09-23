using System;
using System.IO;
using System.Linq;
using System.Text;

namespace WinAllApp.Core.Services
{
    /// <summary>
    /// Preferências do técnico em %APPDATA%\WinAllApp\preferencias.ini (o .exe pode estar num pendrive ou na rede,
    /// então nada é gravado ao lado dele). Hoje guarda só se o tutorial de boas-vindas já foi concluído.
    /// </summary>
    public sealed class PreferenciasUsuario
    {
        private const string ChaveOnboarding = "onboardingConcluido";

        public PreferenciasUsuario(string arquivo = null)
        {
            Arquivo = arquivo ?? Path.Combine(PastaAppData(), "WinAllApp", "preferencias.ini");
        }

        /// <summary>
        /// Usa a variável %APPDATA% quando existe (GetFolderPath ignora a variável e sempre lê o perfil do Windows),
        /// assim um APPDATA redirecionado (perfil móvel, script, teste) é respeitado.
        /// </summary>
        public static string PastaAppData()
        {
            var variavel = Environment.GetEnvironmentVariable("APPDATA");
            return string.IsNullOrWhiteSpace(variavel)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : variavel;
        }

        public string Arquivo { get; }

        public bool OnboardingConcluido
        {
            get => string.Equals(Ler(ChaveOnboarding), "1", StringComparison.Ordinal);
            set => Gravar(ChaveOnboarding, value ? "1" : "0");
        }

        private string Ler(string chave)
        {
            try
            {
                if (!File.Exists(Arquivo)) return null;
                var linha = File.ReadAllLines(Arquivo, Encoding.UTF8)
                    .FirstOrDefault(l => l.StartsWith(chave + "=", StringComparison.OrdinalIgnoreCase));
                return linha?.Substring(chave.Length + 1).Trim();
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>Falha ao gravar não impede o uso do aplicativo: o tutorial só volta a aparecer.</summary>
        private void Gravar(string chave, string valor)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Arquivo));
                var linhas = File.Exists(Arquivo)
                    ? File.ReadAllLines(Arquivo, Encoding.UTF8).Where(l => !l.StartsWith(chave + "=", StringComparison.OrdinalIgnoreCase)).ToList()
                    : new System.Collections.Generic.List<string> { "[WinAllApp]" };
                linhas.Add(chave + "=" + valor);
                File.WriteAllLines(Arquivo, linhas, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
