using System.IO;
using WinAllApp.Core.Models;
using WinAllApp.Core.Services;
using Xunit;

namespace WinAllApp.Core.Tests
{
    public class ComandoSilenciosoTests
    {
        private static readonly string Raiz = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "instaladores"));

        [Fact]
        public void Exe_UsaOProprioInstaladorComOsArgumentosDoConfig()
        {
            var cmd = InstallCommandBuilder.Construir(
                new Programa { Id = "vscode", Instalador = "VSCodeSetup.exe", Argumentos = "/VERYSILENT /NORESTART" }, Raiz);

            Assert.Equal(Path.Combine(Raiz, "VSCodeSetup.exe"), cmd.Arquivo);
            Assert.Equal("/VERYSILENT /NORESTART", cmd.Argumentos);
        }

        [Fact]
        public void Msi_UsaMsiexecComQnPorPadrao()
        {
            var cmd = InstallCommandBuilder.Construir(new Programa { Id = "geo", Instalador = "GeoGebra.msi" }, Raiz);

            Assert.Equal("msiexec.exe", cmd.Arquivo);
            Assert.Equal($"/i \"{Path.Combine(Raiz, "GeoGebra.msi")}\" /qn /norestart", cmd.Argumentos);
        }

        [Fact]
        public void Msi_RespeitaArgumentosPersonalizados()
        {
            var cmd = InstallCommandBuilder.Construir(
                new Programa { Id = "7z", Instalador = "7z.msi", Tipo = "msi", Argumentos = "/qn ALLUSERS=1" }, Raiz);

            Assert.EndsWith("\" /qn ALLUSERS=1", cmd.Argumentos);
        }

        [Fact]
        public void Bat_RodaViaCmdPreservandoAspas()
        {
            var cmd = InstallCommandBuilder.Construir(
                new Programa { Id = "x", Instalador = "pasta com espaço/setup.bat", Argumentos = "/S" }, Raiz);

            Assert.Equal("cmd.exe", cmd.Arquivo);
            Assert.Equal($"/c \"\"{Path.Combine(Raiz, "pasta com espaço", "setup.bat")}\" /S\"", cmd.Argumentos);
        }

        [Fact]
        public void CaminhoAbsoluto_IgnoraPastaDeInstaladores()
        {
            var absoluto = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "outro", "a.exe"));

            var cmd = InstallCommandBuilder.Construir(new Programa { Id = "a", Instalador = absoluto, Argumentos = "/S" }, Raiz);

            Assert.Equal(absoluto, cmd.Arquivo);
        }

        [Fact]
        public void PastaDeInstaladoresRelativa_EhResolvidaAPartirDoConfig()
        {
            var pastaConfig = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "app"));

            var pasta = InstallCommandBuilder.ResolverPastaInstaladores(
                new InstallerConfig { PastaInstaladores = "mock-installers" }, pastaConfig);

            Assert.Equal(Path.Combine(pastaConfig, "mock-installers"), pasta);
        }
    }
}
