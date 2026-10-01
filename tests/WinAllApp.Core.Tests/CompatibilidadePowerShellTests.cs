using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>
    /// Os scripts .ps1 que o WinAllApp roda nas máquinas dos laboratórios (simulação e modelos da pasta exemplos)
    /// precisam funcionar no Windows 7, que vem com o PowerShell 2.0, e no 10/11 (PowerShell 5.1, que roda tudo do 2.0).
    /// O CI só tem Windows novo, então este teste barra os recursos que não existem no 2.0.
    /// </summary>
    public class CompatibilidadePowerShellTests
    {
        private static readonly (string Padrao, string Motivo)[] Proibidos =
        {
            (@"\$PSScriptRoot\b", "$PSScriptRoot é do PowerShell 3.0; use Split-Path -Parent $MyInvocation.MyCommand.Path"),
            (@"\$PSCommandPath\b", "$PSCommandPath é do PowerShell 3.0; use $MyInvocation.MyCommand.Path"),
            (@"\[ordered\]", "[ordered] é do PowerShell 3.0"),
            (@"\[pscustomobject\]", "[pscustomobject] é do PowerShell 3.0; use New-Object PSObject -Property"),
            (@"::new\(", "::new() é do PowerShell 5.0; use New-Object"),
            (@"\s-(not)?in\s", "-in/-notin são do PowerShell 3.0; use -contains/-notcontains"),
            (@"\s-sh[lr]\s", "-shl/-shr são do PowerShell 3.0"),
            (@"^\s*class\s+\w+", "class é do PowerShell 5.0"),
            (@"^\s*using\s+(namespace|module)", "using é do PowerShell 5.0"),
            (@"\.(Where|ForEach)\(", ".Where()/.ForEach() são do PowerShell 4.0"),
            (@"\?\?|\?\.", "?? e ?. são do PowerShell 7"),
            (@"Get-Content\b.*\s-Raw\b", "Get-Content -Raw é do PowerShell 3.0; use [IO.File]::ReadAllText"),
            (@"\s-NoNewline\b", "-NoNewline em Set-Content/Out-File é do PowerShell 5.0"),
            (@"UTF8NoBOM", "UTF8NoBOM é do PowerShell 6"),
            (@"\b(Get-CimInstance|Invoke-CimMethod|Invoke-WebRequest|Invoke-RestMethod|ConvertTo-Json|ConvertFrom-Json|Get-FileHash|Expand-Archive|Compress-Archive|New-TemporaryFile|Get-Clipboard|Set-Clipboard)\b",
                "cmdlet que não existe no PowerShell 2.0 (use Get-WmiObject, System.Net.WebClient etc.)"),
            (@"^\s*#requires\s+-version\s+[3-9]", "#requires pede versão acima da 2.0"),
        };

        public static IEnumerable<object[]> Scripts()
        {
            var pastas = new[] { Path.Combine(Dados.Pasta, "app", "mock-installers"), Path.Combine(Dados.Pasta, "exemplos") };
            return pastas.SelectMany(p => Directory.GetFiles(p, "*.ps1", SearchOption.AllDirectories))
                .Select(f => new object[] { Path.GetRelativePath(Dados.Pasta, f) });
        }

        [Fact]
        public void HaScriptsParaConferir()
        {
            var nomes = Scripts().Select(s => Path.GetFileName((string)s[0])).ToList();
            Assert.Contains("vscode-setup.ps1", nomes);
            Assert.Contains("winget.ps1", nomes);
            Assert.Contains("setup-licenciado.ps1", nomes);
            Assert.Contains("copiar-portatil.ps1", nomes);
        }

        [Theory]
        [MemberData(nameof(Scripts))]
        public void Script_UsaSoRecursosDoPowerShell20(string relativo)
        {
            var bytes = File.ReadAllBytes(Path.Combine(Dados.Pasta, relativo));

            // Só ASCII: sem BOM, o PowerShell 2.0/5.1 lê o arquivo na página de código do sistema e acentos quebrariam.
            var indice = System.Array.FindIndex(bytes, b => b > 127);
            Assert.True(indice < 0, $"{relativo}: caractere não ASCII no byte {indice}.");

            var linhas = File.ReadAllLines(Path.Combine(Dados.Pasta, relativo))
                .Select((texto, i) => (Texto: texto, Numero: i + 1))
                .Where(l => !l.Texto.TrimStart().StartsWith("#") || l.Texto.TrimStart().StartsWith("#requires"));

            var problemas = (from l in linhas
                             from p in Proibidos
                             where Regex.IsMatch(l.Texto, p.Padrao, RegexOptions.IgnoreCase)
                             select $"{relativo}:{l.Numero}: {p.Motivo}").ToList();
            Assert.True(problemas.Count == 0, string.Join("\n", problemas));
        }

        [Fact]
        public void Detector_PegaRecursoNovo()
        {
            Assert.Contains(Proibidos, p => Regex.IsMatch("$pasta = $PSScriptRoot", p.Padrao, RegexOptions.IgnoreCase));
            Assert.Contains(Proibidos, p => Regex.IsMatch("if ($x -in $lista) {}", p.Padrao, RegexOptions.IgnoreCase));
            Assert.DoesNotContain(Proibidos, p => Regex.IsMatch("$pasta = Split-Path -Parent $MyInvocation.MyCommand.Path", p.Padrao, RegexOptions.IgnoreCase));
        }
    }
}
