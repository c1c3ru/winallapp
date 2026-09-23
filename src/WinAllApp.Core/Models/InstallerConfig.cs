using System.Collections.Generic;
using System.Runtime.Serialization;

namespace WinAllApp.Core.Models
{
    /// <summary>Raiz do config.json: catálogo de programas e mapa Bloco → Laboratório → programas.</summary>
    [DataContract]
    public sealed class InstallerConfig
    {
        /// <summary>
        /// Pasta onde ficam os instaladores. Pode ser absoluta, UNC (\\servidor\share)
        /// ou relativa à pasta do config.json.
        /// </summary>
        [DataMember(Name = "pastaInstaladores", Order = 0)]
        public string PastaInstaladores { get; set; }

        [DataMember(Name = "blocos", Order = 1)]
        public List<Bloco> Blocos { get; set; } = new List<Bloco>();

        /// <summary>Catálogo único; os laboratórios referenciam os programas por <see cref="Programa.Id"/>.</summary>
        [DataMember(Name = "programas", Order = 2)]
        public List<Programa> Programas { get; set; } = new List<Programa>();

        /// <summary>Pasta local onde ficam os programas do tipo copia_pasta (aceita %VARIAVEIS%). Padrão: C:\Programas.</summary>
        [DataMember(Name = "pastaDestinoCopias", Order = 3)]
        public string PastaDestinoCopias { get; set; }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            if (Blocos == null) Blocos = new List<Bloco>();
            if (Programas == null) Programas = new List<Programa>();
        }
    }

    [DataContract]
    public sealed class Bloco
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "nome", Order = 1)]
        public string Nome { get; set; }

        [DataMember(Name = "laboratorios", Order = 2)]
        public List<Laboratorio> Laboratorios { get; set; } = new List<Laboratorio>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            if (Laboratorios == null) Laboratorios = new List<Laboratorio>();
        }

        public override string ToString() => Nome ?? Id;
    }

    [DataContract]
    public sealed class Laboratorio
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "nome", Order = 1)]
        public string Nome { get; set; }

        [DataMember(Name = "sala", Order = 2)]
        public string Sala { get; set; }

        /// <summary>Nota exibida na tela (ex.: "Apenas os programas padrões").</summary>
        [DataMember(Name = "observacao", Order = 3)]
        public string Observacao { get; set; }

        /// <summary>Ids dos programas (do catálogo) instalados nesta sala.</summary>
        [DataMember(Name = "programas", Order = 4)]
        public List<string> Programas { get; set; } = new List<string>();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            if (Programas == null) Programas = new List<string>();
        }

        public override string ToString() => Nome ?? Id;
    }

    [DataContract]
    public sealed class Programa
    {
        [DataMember(Name = "id", Order = 0)]
        public string Id { get; set; }

        [DataMember(Name = "nome", Order = 1)]
        public string Nome { get; set; }

        [DataMember(Name = "versao", Order = 2)]
        public string Versao { get; set; }

        /// <summary>Caminho do instalador, absoluto ou relativo a <see cref="InstallerConfig.PastaInstaladores"/>.</summary>
        [DataMember(Name = "instalador", Order = 3)]
        public string Instalador { get; set; }

        /// <summary>exe, msi, bat ou cmd. Se vazio, é deduzido pela extensão do instalador.</summary>
        [DataMember(Name = "tipo", Order = 4)]
        public string Tipo { get; set; }

        /// <summary>Parâmetros silenciosos (ex.: /S, /quiet, /VERYSILENT). Para MSI o padrão é "/qn /norestart".</summary>
        [DataMember(Name = "argumentos", Order = 5)]
        public string Argumentos { get; set; }

        /// <summary>Tempo máximo de instalação; 0 ou ausente usa o padrão da fila.</summary>
        [DataMember(Name = "timeoutMinutos", Order = 6)]
        public int TimeoutMinutos { get; set; }

        /// <summary>Códigos de saída considerados sucesso. Padrão: 0, 1641 e 3010.</summary>
        [DataMember(Name = "codigosSucesso", Order = 7)]
        public List<int> CodigosSucesso { get; set; }

        /// <summary>Nota para o técnico (ex.: parâmetro silencioso a confirmar), exibida abaixo do nome.</summary>
        [DataMember(Name = "observacao", Order = 8)]
        public string Observacao { get; set; }

        /// <summary>
        /// Cenário de implantação: gerenciador, offline_licenciado, offline_gratuito ou copia_pasta.
        /// Se vazio, é deduzido (ver <see cref="Services.CategoriaResolver"/>).
        /// </summary>
        [DataMember(Name = "categoria", Order = 9)]
        public string Categoria { get; set; }

        /// <summary>Id do pacote no winget (Windows 10/11). Ex.: GeoGebra.Classic.</summary>
        [DataMember(Name = "wingetId", Order = 10)]
        public string WingetId { get; set; }

        /// <summary>Versão fixa no winget (opcional; sem ela instala a mais recente).</summary>
        [DataMember(Name = "wingetVersao", Order = 11)]
        public string WingetVersao { get; set; }

        /// <summary>Id do pacote no Chocolatey (usado no Windows 7/8.1). Ex.: python3.</summary>
        [DataMember(Name = "chocoId", Order = 12)]
        public string ChocoId { get; set; }

        /// <summary>Versão fixa no Chocolatey compatível com o Windows 7 (ex.: 3.8.10 para o Python).</summary>
        [DataMember(Name = "chocoVersao", Order = 13)]
        public string ChocoVersao { get; set; }

        /// <summary>
        /// Windows mínimo da versão mais recente do programa ("7", "8.1", "10" ou "11").
        /// No Windows 7, um programa com mínimo "10" e sem <see cref="ChocoVersao"/> é bloqueado com aviso.
        /// </summary>
        [DataMember(Name = "windowsMinimo", Order = 14)]
        public string WindowsMinimo { get; set; }

        /// <summary>Só para copia_pasta: pasta local de destino (aceita %VARIAVEIS%). Padrão: pastaDestinoCopias\id.</summary>
        [DataMember(Name = "destino", Order = 15)]
        public string Destino { get; set; }

        public override string ToString() => Nome ?? Id;
    }
}
