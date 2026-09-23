using System;
using System.Collections.Generic;
using System.IO;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>Detecção do Windows e dos pré-requisitos do Chocolatey a partir de um registro falso.</summary>
    public class AmbienteTests
    {
        private sealed class RegistroFalso : ILeitorRegistro
        {
            private readonly Dictionary<string, object> _valores = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            public RegistroFalso Com(string chave, string valor, object dado)
            {
                _valores[chave + "|" + valor] = dado;
                return this;
            }

            public object Ler(string chave, string valor) => _valores.TryGetValue(chave + "|" + valor, out var v) ? v : null;
        }

        private static RegistroFalso Windows7() => new RegistroFalso()
            .Com(DetectorAmbiente.ChaveVersao, "CurrentVersion", "6.1")
            .Com(DetectorAmbiente.ChaveVersao, "CurrentBuildNumber", "7601");

        private static RegistroFalso Windows(int build) => new RegistroFalso()
            .Com(DetectorAmbiente.ChaveVersao, "CurrentVersion", "6.3")
            .Com(DetectorAmbiente.ChaveVersao, "CurrentMajorVersionNumber", 10)
            .Com(DetectorAmbiente.ChaveVersao, "CurrentMinorVersionNumber", 0)
            .Com(DetectorAmbiente.ChaveVersao, "CurrentBuildNumber", build.ToString());

        private static AmbienteSistema Detectar(ILeitorRegistro registro, params string[] arquivos)
        {
            var existentes = new HashSet<string>(arquivos, StringComparer.OrdinalIgnoreCase);
            var variaveis = new Dictionary<string, string>
            {
                ["LOCALAPPDATA"] = @"C:\Users\tecnico\AppData\Local",
                ["ProgramData"] = @"C:\ProgramData",
                ["PATH"] = @"C:\Windows\system32;C:\Windows"
            };
            return DetectorAmbiente.Detectar(registro, v => variaveis.TryGetValue(v, out var x) ? x : null, existentes.Contains);
        }

        [Fact]
        public void Windows7_SemChavesDeTls_TlsDesativadoEUsaChocolatey()
        {
            var ambiente = Detectar(Windows7().Com(DetectorAmbiente.ChaveDotNet, "Release", 528049));

            Assert.Equal("Windows 7", ambiente.NomeWindows);
            Assert.False(ambiente.UsaWinget);
            Assert.True(ambiente.DotNet48Instalado);
            Assert.False(ambiente.Tls12Ativo);
            Assert.False(ambiente.PreRequisitosChocoOk);
            Assert.Contains("TLS 1.2: DESATIVADO", ambiente.Resumo());
        }

        [Fact]
        public void Windows7_ComTlsAtivadoNoRegistroEDotNet48()
        {
            var registro = Windows7()
                .Com(DetectorAmbiente.ChaveDotNet, "Release", 528049)
                .Com(DetectorAmbiente.ChaveTls12Cliente, "DisabledByDefault", 0)
                .Com(DetectorAmbiente.ChaveTls12Cliente, "Enabled", 1);
            var ambiente = Detectar(registro, Path.Combine(@"C:\ProgramData", "chocolatey", "bin", "choco.exe"));

            Assert.True(ambiente.Tls12Ativo);
            Assert.True(ambiente.PreRequisitosChocoOk);
            Assert.True(ambiente.ChocoDisponivel);
        }

        [Fact]
        public void Windows7_DotNetAntigo()
        {
            var ambiente = Detectar(Windows7().Com(DetectorAmbiente.ChaveDotNet, "Release", 461814)); // 4.7.2
            Assert.False(ambiente.DotNet48Instalado);
        }

        [Fact]
        public void Windows10e11_TlsLigadoPorPadraoEWingetNoWindowsApps()
        {
            var winget = Path.Combine(@"C:\Users\tecnico\AppData\Local", "Microsoft", "WindowsApps", "winget.exe");
            var dez = Detectar(Windows(19045), winget);
            var onze = Detectar(Windows(22631));

            Assert.Equal("Windows 10", dez.NomeWindows);
            Assert.True(dez.Tls12Ativo);
            Assert.True(dez.WingetDisponivel);
            Assert.Equal("Windows 11", onze.NomeWindows);
            Assert.False(onze.WingetDisponivel);
        }

        [Fact]
        public void Windows10_TlsDesligadoExplicitamente()
        {
            var ambiente = Detectar(Windows(19045).Com(DetectorAmbiente.ChaveTls12Cliente, "Enabled", 0));
            Assert.False(ambiente.Tls12Ativo);
        }

        [Theory]
        [InlineData("7", "10", false)]
        [InlineData("7", "7", true)]
        [InlineData("10", "10", true)]
        [InlineData("10", "11", false)]
        [InlineData("11", "11", true)]
        [InlineData("8.1", "10", false)]
        [InlineData("7", "", true)]
        public void WindowsMinimo(string maquina, string minimo, bool atende) =>
            Assert.Equal(atende, AmbienteSistema.Simular(maquina).Atende(minimo));
    }

    public class OnboardingTests
    {
        private static PreferenciasUsuario NovasPreferencias() =>
            new PreferenciasUsuario(Path.Combine(Dados.NovaPastaTemporaria(), "sub", "preferencias.ini"));

        [Fact]
        public void TresPassos_AvancarVoltarEConcluir()
        {
            var preferencias = NovasPreferencias();
            var vm = new OnboardingViewModel(@"\\servidor\instaladores", preferencias);
            var concluido = 0;
            vm.Concluido += (s, e) => concluido++;

            Assert.Equal(3, vm.Passos.Count);
            Assert.Equal("Passo 1 de 3", vm.Progresso);
            Assert.Contains("Bloco", vm.PassoAtual.Texto);
            Assert.False(vm.VoltarCommand.CanExecute(null));
            Assert.Equal("Avançar", vm.TextoAvancar);

            vm.AvancarCommand.Execute(null);
            Assert.Contains(@"\\servidor\instaladores", vm.PassoAtual.Texto);
            Assert.True(vm.VoltarCommand.CanExecute(null));

            vm.AvancarCommand.Execute(null);
            Assert.Equal("Instale em lote", vm.PassoAtual.Titulo);
            Assert.Equal("Concluir", vm.TextoAvancar);

            vm.VoltarCommand.Execute(null);
            Assert.Equal(2, vm.PassoAtual.Numero);
            vm.AvancarCommand.Execute(null);
            vm.AvancarCommand.Execute(null);

            Assert.Equal(1, concluido);
            Assert.True(preferencias.OnboardingConcluido);
            Assert.True(new PreferenciasUsuario(preferencias.Arquivo).OnboardingConcluido);
        }

        [Fact]
        public void Pular_SemNaoMostrarNovamente_VoltaAAparecerNaProximaVez()
        {
            var preferencias = NovasPreferencias();
            var vm = new OnboardingViewModel(null, preferencias) { NaoMostrarNovamente = false };

            vm.PularCommand.Execute(null);

            Assert.True(vm.Finalizado);
            Assert.False(preferencias.OnboardingConcluido);
        }

        [Fact]
        public void Preferencias_ArquivoInexistenteOuIlegivel_NaoQuebra()
        {
            var preferencias = new PreferenciasUsuario(Path.Combine(Dados.NovaPastaTemporaria(), "nao-existe", "p.ini"));
            Assert.False(preferencias.OnboardingConcluido);
            preferencias.OnboardingConcluido = true;
            preferencias.OnboardingConcluido = true;
            Assert.Single(File.ReadAllLines(preferencias.Arquivo), l => l.StartsWith("onboardingConcluido="));
        }
    }
}
