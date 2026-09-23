using System;
using System.IO;
using System.Linq;
using System.Text;

namespace WinAllApp
{
    /// <summary>Arquivos que viajam dentro do WinAllApp.exe (config padrão e kit de simulação).</summary>
    public static class RecursosEmbutidos
    {
        public const string ConfigPadrao = "WinAllApp.Config.config.json";
        public const string ConfigSimulacao = "WinAllApp.Config.config.simulacao.json";
        private const string PrefixoSimulacao = "WinAllApp.Simulacao.";

        public static string LerTexto(string recurso)
        {
            using (var stream = typeof(RecursosEmbutidos).Assembly.GetManifestResourceStream(recurso))
            {
                if (stream == null) throw new InvalidOperationException("Recurso não encontrado no executável: " + recurso);
                using (var leitor = new StreamReader(stream, Encoding.UTF8)) return leitor.ReadToEnd();
            }
        }

        /// <summary>
        /// Grava config.simulacao.json e mock-installers\*.bat em <paramref name="pasta"/> e devolve a pasta.
        /// O log de execuções anteriores é apagado para cada simulação começar limpa.
        /// </summary>
        public static string ExtrairSimulacao(string pasta)
        {
            var mocks = Path.Combine(pasta, "mock-installers");
            Directory.CreateDirectory(mocks);
            File.WriteAllText(Path.Combine(pasta, "config.simulacao.json"), LerTexto(ConfigSimulacao), new UTF8Encoding(false));

            var assembly = typeof(RecursosEmbutidos).Assembly;
            foreach (var nome in assembly.GetManifestResourceNames().Where(n => n.StartsWith(PrefixoSimulacao, StringComparison.Ordinal)))
            {
                // Quebras de linha CRLF garantidas: o cmd.exe interpreta mal .bat só com LF.
                var texto = LerTexto(nome).Replace("\r\n", "\n").Replace("\n", "\r\n");
                File.WriteAllText(Path.Combine(mocks, nome.Substring(PrefixoSimulacao.Length)), texto, new UTF8Encoding(false));
            }

            var log = Path.Combine(mocks, "instalacoes-simuladas.log");
            if (File.Exists(log)) File.Delete(log);
            return pasta;
        }
    }
}
