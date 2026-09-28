using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace WinAllApp.Core.Tests
{
    /// <summary>
    /// Usa o ProcessRunner real (Process.Start) com instaladores fictícios rápidos:
    /// .bat no Windows, .sh no Linux/macOS. Nenhum software real é baixado ou instalado.
    /// </summary>
    public class ProcessoRealTests
    {
        private readonly ITestOutputHelper _saida;

        public ProcessoRealTests(ITestOutputHelper saida) => _saida = saida;

        [Fact]
        public async Task FilaReal_DisparaSoOsSelecionadosComArgumentosSilenciosos()
        {
            var pasta = Dados.NovaPastaTemporaria();
            var config = new InstallerConfig
            {
                PastaInstaladores = pasta,
                Programas =
                {
                    Dados.CriarInstaladorFicticio(pasta, "vscode", "/VERYSILENT /NORESTART"),
                    Dados.CriarInstaladorFicticio(pasta, "python", "/quiet InstallAllUsers=1"),
                    Dados.CriarInstaladorFicticio(pasta, "codeblocks", "/S"),
                    Dados.CriarInstaladorFicticio(pasta, "geogebra", "/qn"),
                    Dados.CriarInstaladorFicticio(pasta, "octave", "/S")
                },
                Blocos =
                {
                    new Bloco { Id = "BL1", Nome = "Bloco 1", Laboratorios = { new Laboratorio { Id = "LCC", Nome = "LCC", Programas = { "vscode", "python", "codeblocks" } } } },
                    new Bloco { Id = "BL2", Nome = "Bloco 2", Laboratorios = { new Laboratorio { Id = "MAT", Nome = "Matemática", Programas = { "geogebra", "octave" } } } }
                }
            };

            SynchronizationContext.SetSynchronizationContext(null);
            var vm = new MainViewModel(config, pasta, new ProcessRunner());
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.AguardarAutodescoberta();
            vm.Programas[0].Selecionado = true;  // vscode
            vm.Programas[2].Selecionado = true;  // codeblocks

            var relogio = Stopwatch.StartNew();
            var resultados = await vm.InstalarAsync();
            relogio.Stop();

            foreach (var linha in vm.Log) _saida.WriteLine(linha);

            Assert.All(resultados, r => Assert.True(r.Sucesso, r.Mensagem));
            Assert.Equal(new[] { "vscode /VERYSILENT /NORESTART", "codeblocks /S" }, Dados.LerLog(pasta));
            Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(30));
        }

        [Fact]
        public async Task FilaReal_UiContinuaLivreEnquantoInstaladorLentoRoda()
        {
            var pasta = Dados.NovaPastaTemporaria();
            var lento = Dados.CriarInstaladorFicticio(pasta, "lento", "/S", segundos: 2);
            var fila = new InstallQueue(new ProcessRunner(), pasta);

            var relogio = Stopwatch.StartNew();
            var tarefa = fila.ExecutarAsync(new[] { lento }, null, default);
            var tempoAteDevolverControle = relogio.Elapsed;

            Assert.False(tarefa.IsCompleted);
            Assert.True(tempoAteDevolverControle < TimeSpan.FromSeconds(1), $"Bloqueou por {tempoAteDevolverControle}.");

            var resultados = await tarefa;
            Assert.True(resultados.Single().Sucesso);
            Assert.True(relogio.Elapsed >= TimeSpan.FromSeconds(1.5));
        }

        [Fact]
        public async Task FilaReal_CodigoDeErroEArquivoAusenteViramFalha()
        {
            var pasta = Dados.NovaPastaTemporaria();
            // Códigos de saída POSIX têm 8 bits; 1603 viraria 67 no Linux, então o teste usa um código pequeno.
            var falha = Dados.CriarInstaladorFicticio(pasta, "falha", "/S", codigoSaida: 5);
            var ausente = new Programa { Id = "ausente", Nome = "ausente", Instalador = "nao-existe.exe", Argumentos = "/S" };
            var fila = new InstallQueue(new ProcessRunner(), pasta);

            var resultados = await fila.ExecutarAsync(new[] { falha, ausente }, null, default);

            Assert.Equal(EstadoInstalacao.Falha, resultados[0].Estado);
            Assert.Equal(5, resultados[0].CodigoSaida);
            Assert.Equal(EstadoInstalacao.Falha, resultados[1].Estado);
            Assert.Contains("não encontrado", resultados[1].Mensagem);
        }

        [Fact]
        public async Task FilaReal_TimeoutEncerraOInstalador()
        {
            var pasta = Dados.NovaPastaTemporaria();
            var travado = Dados.CriarInstaladorFicticio(pasta, "travado", "/S", segundos: 20);
            var runner = new ProcessRunner();
            var comando = InstallCommandBuilder.Construir(travado, pasta);

            var relogio = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => runner.ExecutarAsync(comando, TimeSpan.FromMilliseconds(500), default));
            Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Simulação completa no Windows com o config.simulacao.json e os .bat da pasta mock-installers
        /// (os mesmos que acompanham o executável).
        /// </summary>
        [WindowsFact]
        public async Task Simulacao_ConfigDoAplicativoComBatsFicticios()
        {
            var pastaApp = Dados.NovaPastaTemporaria();
            File.Copy(Path.Combine(Dados.Pasta, "app", "config.simulacao.json"), Path.Combine(pastaApp, "config.simulacao.json"));
            Directory.CreateDirectory(Path.Combine(pastaApp, "mock-installers"));
            foreach (var bat in Directory.GetFiles(Path.Combine(Dados.Pasta, "app", "mock-installers"), "*.bat"))
                File.Copy(bat, Path.Combine(pastaApp, "mock-installers", Path.GetFileName(bat)));

            var carga = ConfigLoader.CarregarArquivo(Path.Combine(pastaApp, "config.simulacao.json"));
            Assert.True(carga.Valido, string.Join("; ", carga.Erros));
            var pastaInstaladores = InstallCommandBuilder.ResolverPastaInstaladores(carga.Config, carga.PastaConfig);
            SynchronizationContext.SetSynchronizationContext(null);
            var vm = new MainViewModel(carga.Config, pastaInstaladores, new ProcessRunner());

            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL1");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "FALHAS");
            vm.AguardarAutodescoberta();

            // Autodescoberta: o instalador ausente aparece com ❌ e não entra na fila.
            var ausente = vm.Programas.Single(p => p.Programa.Id == "inexistente");
            Assert.True(ausente.NaoEncontrado);
            Assert.EndsWith("nao-existe.exe", ausente.StatusBusca);
            vm.SelecionarTodosCommand.Execute(null);
            Assert.False(ausente.Selecionado);
            var resultados = await vm.InstalarAsync();

            foreach (var linha in vm.Log) _saida.WriteLine(linha);

            Assert.Equal(2, resultados.Count);
            Assert.Equal(EstadoInstalacao.Falha, resultados[0].Estado);            // exit 1603
            Assert.Equal(EstadoInstalacao.SucessoReiniciar, resultados[1].Estado); // exit 3010
            Assert.Equal(EstadoInstalacao.Pendente, ausente.Estado);

            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL2");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            vm.AguardarAutodescoberta();
            vm.SelecionarTodosCommand.Execute(null);
            resultados = await vm.InstalarAsync();

            Assert.All(resultados, r => Assert.Equal(EstadoInstalacao.Sucesso, r.Estado));
            var log = File.ReadAllLines(Path.Combine(pastaApp, "mock-installers", "instalacoes-simuladas.log"));
            Assert.Contains(log, l => l.Contains("vscode-setup.bat /VERYSILENT /NORESTART"));
            Assert.Contains(log, l => l.Contains("python-setup.bat /quiet InstallAllUsers=1"));
            Assert.Contains(log, l => l.Contains("codeblocks-setup.bat /S"));
        }
    }
}
