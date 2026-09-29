using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WinAllApp.Core.Services.Padronizacao
{
    /// <summary>Windows em que a padronização roda. Os comandos mudam um pouco entre o 10 e o 11.</summary>
    public enum VersaoPadronizacao
    {
        NaoSuportado,
        Windows10,
        Windows11
    }

    /// <summary>Conta de quem usa o computador (tópico 4 do checklist), além da conta "Informatica".</summary>
    public enum PerfilDoUsuario
    {
        /// <summary>Laboratório: conta "Aluno", Usuário Padrão, sem senha, papel de parede "Lab".</summary>
        Aluno,

        /// <summary>Conta "Bolsista", Administrador, sem senha, papel de parede "Adm".</summary>
        Bolsista
    }

    public static class SistemaPadronizacao
    {
        /// <summary>Primeiro build do Windows 11.</summary>
        public const int BuildWindows11 = 22000;

        public static VersaoPadronizacao Detectar(AmbienteSistema ambiente)
        {
            if (ambiente == null || ambiente.VersaoWindows < AmbienteSistema.Windows10) return VersaoPadronizacao.NaoSuportado;
            return ambiente.BuildWindows >= BuildWindows11 ? VersaoPadronizacao.Windows11 : VersaoPadronizacao.Windows10;
        }

        public static string Nome(VersaoPadronizacao versao) =>
            versao == VersaoPadronizacao.Windows11 ? "Windows 11" : versao == VersaoPadronizacao.Windows10 ? "Windows 10" : "não suportado";
    }

    /// <summary>O que o técnico preencheu na tela de padronização.</summary>
    public sealed class OpcoesPadronizacao
    {
        public const string ContaInformatica = "Informatica";
        public const string ContaAluno = "Aluno";
        public const string ContaBolsista = "Bolsista";

        public static readonly string[] Blocos = { "ADM", "BL1", "BL2", "BL3" };

        /// <summary>Servidor de arquivos do campus (tópicos 3, 6.1 e 8 do manual).</summary>
        public const string ServidorArquivosPadrao = "10.50.11.2";

        public const string PastaPapeisDeParedePadrao =
            @"\\10.50.11.2\informatica\NAC - Núcleo de Atendimento ao Cliente\Computadores\Padronização\Papéis de parede";

        public string Bloco { get; set; } = "BL1";
        public string Local { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;

        public string SenhaInformatica { get; set; } = string.Empty;
        public PerfilDoUsuario Perfil { get; set; } = PerfilDoUsuario.Aluno;

        /// <summary>Chave da etiqueta do gabinete (opcional; vazio = só confere se já está ativado).</summary>
        public string ChaveProduto { get; set; } = string.Empty;

        public bool AdicionarImpressora { get; set; }
        public string IpImpressora { get; set; } = string.Empty;
        public string NomeImpressora { get; set; } = string.Empty;

        public bool LimparAreaDeTrabalho { get; set; } = true;

        public string PastaPapeisDeParede { get; set; } = PastaPapeisDeParedePadrao;

        /// <summary>Onde as imagens ficam no disco local (lidas por todas as contas). Vazio = Imagens Públicas\WinAllApp.</summary>
        public string PastaLocalPapeis { get; set; }

        public string ServidorArquivos { get; set; } = ServidorArquivosPadrao;

        /// <summary>Caminho do WinAllApp.exe em execução, que a limpeza da área de trabalho nunca apaga.</summary>
        public string ExecutavelEmUso { get; set; }

        public string ContaDoUsuario => Perfil == PerfilDoUsuario.Bolsista ? ContaBolsista : ContaAluno;

        public string NomeComputador => MontarNome(Bloco, Local, Numero);

        public string NomeImpressoraFinal =>
            string.IsNullOrWhiteSpace(NomeImpressora) ? "Impressora " + (IpImpressora ?? string.Empty).Trim() : NomeImpressora.Trim();

        /// <summary>Lista os problemas do que foi preenchido; vazia quando dá para rodar.</summary>
        public IReadOnlyList<string> Validar()
        {
            var erros = new List<string>();
            var nome = NomeComputador;
            if (!NomeComputadorValido(nome, out var motivo)) erros.Add("Nome do computador: " + motivo);

            if (string.IsNullOrEmpty(SenhaInformatica))
                erros.Add("Informe a senha da conta Informatica.");
            else if (SenhaInformatica.Length > 127)
                erros.Add("A senha da conta Informatica passa de 127 caracteres.");

            if (!string.IsNullOrWhiteSpace(ChaveProduto) && !ChaveValida(ChaveProduto))
                erros.Add("Chave do Windows: use o formato XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (a da etiqueta do gabinete).");

            if (AdicionarImpressora)
            {
                if (!IpValido(IpImpressora))
                    erros.Add("Impressora: informe um IP válido, como 10.50.12.34.");
                if (!NomeImpressoraValido(NomeImpressoraFinal))
                    erros.Add("Impressora: o nome não pode ter \\ , ou aspas e deve ter até 60 caracteres.");
            }

            if (string.IsNullOrWhiteSpace(PastaPapeisDeParede))
                erros.Add("Informe a pasta de rede dos papéis de parede.");

            return erros;
        }

        /// <summary>BLOCO-LOCAL-XX em maiúsculas, sem acentos nem espaços.</summary>
        public static string MontarNome(string bloco, string local, string numero)
        {
            var b = Limpar(bloco);
            var l = Limpar(local);
            var n = (numero ?? string.Empty).Trim();
            if (n.Length == 1 && char.IsDigit(n[0])) n = "0" + n;
            return b + "-" + l + "-" + n;
        }

        private static readonly Regex PadraoNome = new Regex(@"^(ADM|BL1|BL2|BL3)-([A-Z0-9]{1,8})-([0-9]{2})$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Padrão do tópico 7: BLOCO (ADM, BL1, BL2 ou BL3), LOCAL com no máximo 8 caracteres e XX com o número do computador.
        /// O resultado cabe nos 15 caracteres do nome NetBIOS.
        /// </summary>
        public static bool NomeComputadorValido(string nome, out string motivo)
        {
            motivo = null;
            var partes = (nome ?? string.Empty).Split('-');
            if (partes.Length != 3) { motivo = "use o padrão BLOCO-LOCAL-XX."; return false; }
            if (!Blocos.Contains(partes[0])) { motivo = "o bloco deve ser ADM, BL1, BL2 ou BL3."; return false; }
            if (partes[1].Length == 0) { motivo = "informe o local (sala ou laboratório)."; return false; }
            if (partes[1].Length > 8) { motivo = "o local deve ter no máximo 8 letras."; return false; }
            if (!partes[1].All(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) { motivo = "o local só pode ter letras e números."; return false; }
            if (!Regex.IsMatch(partes[2], "^[0-9]{2}$")) { motivo = "o número do computador deve ter 2 dígitos (01 a 99)."; return false; }
            if (partes[2] == "00") { motivo = "o número do computador começa em 01."; return false; }
            return PadraoNome.IsMatch(nome);
        }

        /// <summary>IPv4 no formato a.b.c.d (sem nomes nem máscaras).</summary>
        public static bool IpValido(string ip)
        {
            var texto = (ip ?? string.Empty).Trim();
            var partes = texto.Split('.');
            if (partes.Length != 4 || partes.Any(p => p.Length == 0 || p.Length > 3 || !p.All(char.IsDigit))) return false;
            if (!IPAddress.TryParse(texto, out var endereco)) return false;
            var bytes = endereco.GetAddressBytes();
            return bytes[0] != 0 && bytes[0] < 224 && !(bytes.All(b => b == 255));
        }

        public static bool ChaveValida(string chave) =>
            Regex.IsMatch((chave ?? string.Empty).Trim().ToUpperInvariant(), "^[0-9A-Z]{5}(-[0-9A-Z]{5}){4}$");

        public static bool NomeImpressoraValido(string nome) =>
            !string.IsNullOrWhiteSpace(nome) && nome.Length <= 60 && nome.IndexOfAny(new[] { '\\', ',', '"', '\'' }) < 0;

        private static string Limpar(string texto)
        {
            var semAcento = new StringBuilder();
            foreach (var c in (texto ?? string.Empty).Trim().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && !char.IsWhiteSpace(c))
                    semAcento.Append(c);
            return semAcento.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
    }
}
