using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WinAllApp.Core.Services;
using WinAllApp.Core.ViewModels;
using Xunit;

namespace WinAllApp.Core.Tests
{
    /// <summary>
    /// Simula o que o usuário faz na tela, usando o mesmo ViewModel ligado ao XAML:
    /// escolhe bloco → laboratório → marca programas → clica em Instalar.
    /// </summary>
    public class FluxoDoUsuarioTests
    {
        private static (MainViewModel vm, RunnerFalso runner) Criar(RunnerFalso runner = null)
        {
            var carga = Dados.CarregarConfigTeste();
            runner = runner ?? new RunnerFalso();
            var fila = new InstallQueue(runner, Path.Combine(carga.PastaConfig, "instaladores")) { VerificarArquivoExiste = false };
            return (new MainViewModel(new LabCatalog(carga.Config), fila), runner);
        }

        [Fact]
        public void EscolherBloco_MostraApenasOsLaboratoriosDoBloco()
        {
            var (vm, _) = Criar();

            Assert.Equal(2, vm.Blocos.Count);
            Assert.Empty(vm.Laboratorios);

            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL2");

            Assert.Equal(new[] { "MAT" }, vm.Laboratorios.Select(l => l.Id));
            Assert.Empty(vm.Programas);
            Assert.False(vm.TemLaboratorio);
        }

        [Fact]
        public void EscolherLaboratorio_CarregaSoOsProgramasDaSalaDesmarcados()
        {
            var (vm, _) = Criar();
            vm.BlocoSelecionado = vm.Blocos[0];

            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");

            Assert.Equal(new[] { "Visual Studio Code", "Python", "Code::Blocks" }, vm.Programas.Select(p => p.Nome));
            Assert.All(vm.Programas, p => Assert.False(p.Selecionado));
            Assert.False(vm.InstalarCommand.CanExecute(null));
            Assert.Equal("LCC", vm.Titulo);
        }

        [Fact]
        public void TrocarDeLaboratorio_SubstituiALista()
        {
            var (vm, _) = Criar();
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.SelecionarTodosCommand.Execute(null);

            vm.BlocoSelecionado = vm.Blocos[1];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];

            Assert.Equal(new[] { "GeoGebra", "GNU Octave" }, vm.Programas.Select(p => p.Nome));
            Assert.Equal(0, vm.TotalSelecionados);
        }

        [Fact]
        public void SelecionarTodosDoLaboratorio_MarcaTodosELimparDesmarca()
        {
            var (vm, _) = Criar();
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];

            vm.SelecionarTodosCommand.Execute(null);
            Assert.Equal(3, vm.TotalSelecionados);
            Assert.Equal("3 de 3 selecionado(s)", vm.ResumoSelecao);
            Assert.True(vm.InstalarCommand.CanExecute(null));

            vm.LimparSelecaoCommand.Execute(null);
            Assert.Equal(0, vm.TotalSelecionados);
            Assert.False(vm.InstalarCommand.CanExecute(null));
        }

        [Fact]
        public async Task Instalar_ExecutaApenasOsSelecionadosComParametrosSilenciosos()
        {
            var (vm, runner) = Criar();
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios.Single(l => l.Id == "LCC");
            vm.SelecionarTodosCommand.Execute(null);
            vm.Programas.Single(p => p.Programa.Id == "python").Selecionado = false; // usuário desmarca um

            vm.InstalarCommand.Execute(null);
            await vm.InstalarCommand.Execucao;

            var comandos = runner.Comandos.ToList();
            Assert.Equal(2, comandos.Count);
            Assert.EndsWith("VSCodeSetup.exe", comandos[0].Arquivo);
            Assert.Equal("/VERYSILENT /NORESTART", comandos[0].Argumentos);
            Assert.EndsWith("codeblocks-setup.exe", comandos[1].Arquivo);
            Assert.Equal("/S", comandos[1].Argumentos);

            Assert.Equal(EstadoInstalacao.Sucesso, vm.Programas.Single(p => p.Programa.Id == "vscode").Estado);
            Assert.Equal(EstadoInstalacao.Pendente, vm.Programas.Single(p => p.Programa.Id == "python").Estado);
            Assert.False(vm.Ocupado);
            Assert.Equal(100, vm.Progresso);
            Assert.StartsWith("Concluído: 2 instalado(s)", vm.StatusTexto);
        }

        [Fact]
        public async Task Instalar_Matematica_UsaMsiexecSilencioso()
        {
            var (vm, runner) = Criar();
            vm.BlocoSelecionado = vm.Blocos.Single(b => b.Id == "BL2");
            vm.LaboratorioSelecionado = vm.Laboratorios.Single();
            vm.SelecionarTodosCommand.Execute(null);

            await vm.InstalarAsync();

            var comandos = runner.Comandos.ToList();
            Assert.Equal(2, comandos.Count);
            Assert.Equal("msiexec.exe", comandos[0].Arquivo);
            Assert.EndsWith("GeoGebra.msi\" /qn /norestart", comandos[0].Argumentos);
            Assert.EndsWith("octave-setup.exe", comandos[1].Arquivo);
        }

        [Fact]
        public async Task Instalar_NaoBloqueiaEMantemNavegacaoTravadaAteTerminar()
        {
            var runner = new RunnerFalso { Portao = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            var (vm, _) = Criar(runner);
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.Programas[0].Selecionado = true;

            var tarefa = vm.InstalarAsync();

            // O método devolve o controle imediatamente: a "UI" não fica congelada.
            Assert.False(tarefa.IsCompleted);
            Assert.True(vm.Ocupado);
            Assert.False(vm.PodeNavegar);
            Assert.False(vm.InstalarCommand.CanExecute(null));
            Assert.True(vm.CancelarCommand.CanExecute(null));

            // Troca de bloco é ignorada durante a instalação.
            var blocoAtual = vm.BlocoSelecionado;
            vm.BlocoSelecionado = vm.Blocos[1];
            Assert.Same(blocoAtual, vm.BlocoSelecionado);

            runner.Portao.SetResult(true);
            await tarefa;

            Assert.False(vm.Ocupado);
            Assert.True(vm.PodeNavegar);
            Assert.Equal(EstadoInstalacao.Sucesso, vm.Programas[0].Estado);
        }

        [Fact]
        public async Task Cancelar_InterrompeOAtualEMarcaORestanteComoCancelado()
        {
            var runner = new RunnerFalso { Portao = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) };
            var (vm, _) = Criar(runner);
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.SelecionarTodosCommand.Execute(null);

            var tarefa = vm.InstalarAsync();
            vm.CancelarCommand.Execute(null);
            var resultados = await tarefa;

            Assert.Equal(3, resultados.Count);
            Assert.All(resultados, r => Assert.Equal(EstadoInstalacao.Cancelado, r.Estado));
            Assert.Single(runner.Comandos); // só o primeiro chegou a ser disparado
            Assert.False(vm.Ocupado);
        }

        [Fact]
        public async Task CodigosDeSaida_FalhaEReinicioSaoReportados()
        {
            var runner = new RunnerFalso(c => c.Arquivo.EndsWith("VSCodeSetup.exe") ? 1603 : 3010);
            var (vm, _) = Criar(runner);
            vm.BlocoSelecionado = vm.Blocos[0];
            vm.LaboratorioSelecionado = vm.Laboratorios[0];
            vm.SelecionarTodosCommand.Execute(null);

            await vm.InstalarAsync();

            Assert.Equal(EstadoInstalacao.Falha, vm.Programas[0].Estado);
            Assert.Equal("Falhou", vm.Programas[0].EstadoTexto);
            Assert.Equal(EstadoInstalacao.SucessoReiniciar, vm.Programas[1].Estado);
            Assert.StartsWith("Concluído: 2 instalado(s), 1 falha(s)", vm.StatusTexto);
        }
    }
}
