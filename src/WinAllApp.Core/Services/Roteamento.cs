using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinAllApp.Core.Models;

namespace WinAllApp.Core.Services
{
    /// <summary>Os 4 cenários de implantação do config.json (chave "categoria").</summary>
    public enum CategoriaInstalacao
    {
        /// <summary>"gerenciador": instalador da rede se existir; senão winget (Win 10/11) ou Chocolatey (Win 7/8.1).</summary>
        Gerenciador,
        /// <summary>"offline_licenciado": só o instalador silencioso da pasta de rede; a ativação é manual.</summary>
        OfflineLicenciado,
        /// <summary>"offline_gratuito": instalador baixado à mão e guardado na pasta de rede.</summary>
        OfflineGratuito,
        /// <summary>"copia_pasta": programa portátil ou material de aula copiado da rede para a máquina.</summary>
        CopiaPasta
    }

    public enum AcaoInstalacao
    {
        /// <summary>Executa um processo (instalador da rede, winget ou choco).</summary>
        Processo,
        /// <summary>Copia uma pasta da rede para a máquina.</summary>
        CopiarPasta,
        /// <summary>Não dá para instalar nesta máquina (pré-requisito ausente, arquivo ausente...).</summary>
        Bloqueado,
        /// <summary>A versão configurada não roda neste Windows.</summary>
        Incompativel
    }

    /// <summary>De onde o programa vai sair.</summary>
    public enum FonteInstalacao
    {
        Nenhuma,
        Rede,
        Winget,
        Chocolatey
    }

    /// <summary>Resultado do roteamento: o que o motor vai fazer com um programa nesta máquina.</summary>
    public sealed class PlanoInstalacao
    {
        private PlanoInstalacao(Programa programa, CategoriaInstalacao categoria, AcaoInstalacao acao, FonteInstalacao fonte)
        {
            Programa = programa;
            Categoria = categoria;
            Acao = acao;
            Fonte = fonte;
        }

        public Programa Programa { get; }
        public CategoriaInstalacao Categoria { get; }
        public AcaoInstalacao Acao { get; }
        public FonteInstalacao Fonte { get; }

        /// <summary>Preenchido quando <see cref="Acao"/> é Processo.</summary>
        public InstallCommand Comando { get; private set; }

        /// <summary>Códigos de saída aceitos como sucesso, além dos padrões (ex.: winget "já instalado").</summary>
        public IReadOnlyList<int> CodigosSucessoExtras { get; private set; } = Array.Empty<int>();

        /// <summary>Preenchidos quando <see cref="Acao"/> é CopiarPasta.</summary>
        public string Origem { get; private set; }
        public string Destino { get; private set; }

        /// <summary>Motivo do bloqueio/incompatibilidade, ou lembrete (ex.: ativação manual da licença).</summary>
        public string Motivo { get; private set; }

        public string Descricao
        {
            get
            {
                switch (Acao)
                {
                    case AcaoInstalacao.Processo: return $"[{Fonte}] {Comando}";
                    case AcaoInstalacao.CopiarPasta: return $"[Cópia] \"{Origem}\" -> \"{Destino}\"";
                    case AcaoInstalacao.Incompativel: return "[Incompatível] " + Motivo;
                    default: return "[Bloqueado] " + Motivo;
                }
            }
        }

        public override string ToString() => Descricao;

        public static PlanoInstalacao Processo(Programa p, CategoriaInstalacao c, FonteInstalacao fonte, InstallCommand comando,
            string lembrete = null, IReadOnlyList<int> codigosExtras = null) =>
            new PlanoInstalacao(p, c, AcaoInstalacao.Processo, fonte)
            {
                Comando = comando,
                Motivo = lembrete,
                CodigosSucessoExtras = codigosExtras ?? Array.Empty<int>()
            };

        public static PlanoInstalacao Copia(Programa p, string origem, string destino) =>
            new PlanoInstalacao(p, CategoriaInstalacao.CopiaPasta, AcaoInstalacao.CopiarPasta, FonteInstalacao.Rede) { Origem = origem, Destino = destino };

        public static PlanoInstalacao Bloqueado(Programa p, CategoriaInstalacao c, string motivo) =>
            new PlanoInstalacao(p, c, AcaoInstalacao.Bloqueado, FonteInstalacao.Nenhuma) { Motivo = motivo };

        public static PlanoInstalacao Incompativel(Programa p, CategoriaInstalacao c, string motivo) =>
            new PlanoInstalacao(p, c, AcaoInstalacao.Incompativel, FonteInstalacao.Nenhuma) { Motivo = motivo };
    }

    /// <summary>Tudo o que as estratégias consultam; os testes trocam o acesso a disco por funções falsas.</summary>
    public sealed class ContextoInstalacao
    {
        public ContextoInstalacao(string pastaInstaladores, string pastaDestinoCopias, AmbienteSistema ambiente)
        {
            PastaInstaladores = pastaInstaladores;
            PastaDestinoCopias = string.IsNullOrWhiteSpace(pastaDestinoCopias) ? PastaDestinoCopiasPadrao : pastaDestinoCopias;
            Ambiente = ambiente ?? throw new ArgumentNullException(nameof(ambiente));
        }

        public const string PastaDestinoCopiasPadrao = @"C:\Programas";

        /// <summary>Fonte primária: a pasta de rede (\\servidor\instaladores) com os instaladores e pastas.</summary>
        public string PastaInstaladores { get; }
        public string PastaDestinoCopias { get; }
        public AmbienteSistema Ambiente { get; }
        public Func<string, bool> ArquivoExiste { get; set; } = File.Exists;
        public Func<string, bool> PastaExiste { get; set; } = Directory.Exists;
    }

    /// <summary>Uma estratégia por categoria (Strategy Pattern).</summary>
    public interface IEstrategiaInstalacao
    {
        CategoriaInstalacao Categoria { get; }
        PlanoInstalacao Planejar(Programa programa, ContextoInstalacao contexto);
    }

    public static class CategoriaResolver
    {
        /// <summary>
        /// Usa a chave "categoria"; aceita também o formato curto em "tipo" (ex.: "tipo": "winget").
        /// Sem nada disso: com wingetId/chocoId é gerenciador; senão, instalador gratuito da rede.
        /// </summary>
        public static CategoriaInstalacao Resolver(Programa programa)
        {
            if (TentarConverter(programa.Categoria, out var categoria)) return categoria;
            if (TentarConverter(programa.Tipo, out categoria)) return categoria;
            if (!string.IsNullOrWhiteSpace(programa.WingetId) || !string.IsNullOrWhiteSpace(programa.ChocoId))
                return CategoriaInstalacao.Gerenciador;
            return CategoriaInstalacao.OfflineGratuito;
        }

        public static bool TentarConverter(string texto, out CategoriaInstalacao categoria)
        {
            switch ((texto ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_'))
            {
                case "gerenciador":
                case "winget":
                case "choco":
                case "chocolatey":
                    categoria = CategoriaInstalacao.Gerenciador;
                    return true;
                case "offline_licenciado":
                case "licenciado":
                    categoria = CategoriaInstalacao.OfflineLicenciado;
                    return true;
                case "offline_gratuito":
                case "gratuito":
                case "offline":
                    categoria = CategoriaInstalacao.OfflineGratuito;
                    return true;
                case "copia_pasta":
                case "copia":
                case "pasta":
                    categoria = CategoriaInstalacao.CopiaPasta;
                    return true;
                default:
                    categoria = CategoriaInstalacao.OfflineGratuito;
                    return false;
            }
        }

        public static string Rotulo(CategoriaInstalacao categoria)
        {
            switch (categoria)
            {
                case CategoriaInstalacao.Gerenciador: return "Rede ou winget/Chocolatey";
                case CategoriaInstalacao.OfflineLicenciado: return "Licenciado (rede)";
                case CategoriaInstalacao.CopiaPasta: return "Cópia de pasta";
                default: return "Gratuito (rede)";
            }
        }
    }

    /// <summary>Base comum: checagem de compatibilidade com o Windows e instalador da pasta de rede.</summary>
    public abstract class EstrategiaBase : IEstrategiaInstalacao
    {
        public abstract CategoriaInstalacao Categoria { get; }
        public abstract PlanoInstalacao Planejar(Programa programa, ContextoInstalacao contexto);

        public static string MotivoIncompatibilidade(Programa programa, AmbienteSistema ambiente)
        {
            if (ambiente.Atende(programa.WindowsMinimo)) return null;
            var versao = string.IsNullOrWhiteSpace(programa.Versao) ? string.Empty : " " + programa.Versao;
            return $"Versão incompatível com o SO: {programa.Nome}{versao} exige Windows {programa.WindowsMinimo} ou mais novo; esta máquina é {ambiente.NomeWindows}.";
        }

        /// <summary>
        /// Aviso de compatibilidade mostrado na lista antes de instalar (sem acessar a rede).
        /// null quando a versão configurada roda neste Windows.
        /// </summary>
        public static string AvisoCompatibilidade(Programa programa, AmbienteSistema ambiente)
        {
            var motivo = MotivoIncompatibilidade(programa, ambiente);
            if (motivo == null) return null;
            if (!ambiente.UsaWinget && !string.IsNullOrWhiteSpace(programa.ChocoVersao) && !string.IsNullOrWhiteSpace(programa.ChocoId)
                && CategoriaResolver.Resolver(programa) == CategoriaInstalacao.Gerenciador)
                return $"A versão mais nova não roda no {ambiente.NomeWindows}; será instalada a {programa.ChocoVersao} pelo Chocolatey.";
            return motivo;
        }

        protected static bool TemInstaladorNaRede(Programa programa, ContextoInstalacao contexto, out InstallCommand comando)
        {
            comando = null;
            if (string.IsNullOrWhiteSpace(programa.Instalador)) return false;
            comando = InstallCommandBuilder.Construir(programa, contexto.PastaInstaladores);
            return contexto.ArquivoExiste(comando.CaminhoInstalador);
        }
    }

    /// <summary>
    /// Gerenciador de pacotes. A pasta de rede continua sendo a fonte primária: se o instalador estiver lá, ele é usado.
    /// Senão: Windows 10/11 → winget; Windows 7/8.1 → Chocolatey, só com .NET 4.8 e TLS 1.2 ativos.
    /// </summary>
    public sealed class EstrategiaGerenciador : EstrategiaBase
    {
        /// <summary>winget: APPINSTALLER_CLI_ERROR_PACKAGE_ALREADY_INSTALLED (0x8A15002B).</summary>
        public const int WingetJaInstalado = unchecked((int)0x8A15002B);

        public override CategoriaInstalacao Categoria => CategoriaInstalacao.Gerenciador;

        public override PlanoInstalacao Planejar(Programa programa, ContextoInstalacao contexto)
        {
            var ambiente = contexto.Ambiente;
            var incompativel = MotivoIncompatibilidade(programa, ambiente);

            // Windows antigo com versão fixa compatível no Chocolatey: ela tem prioridade (o instalador da rede é a versão nova).
            var usarChocoFixo = incompativel != null && !ambiente.UsaWinget && !string.IsNullOrWhiteSpace(programa.ChocoVersao);

            if (incompativel == null && TemInstaladorNaRede(programa, contexto, out var daRede))
                return PlanoInstalacao.Processo(programa, Categoria, FonteInstalacao.Rede, daRede);

            if (incompativel != null && !usarChocoFixo)
                return PlanoInstalacao.Incompativel(programa, Categoria, incompativel);

            return ambiente.UsaWinget ? PlanejarWinget(programa, ambiente) : PlanejarChoco(programa, ambiente);
        }

        private PlanoInstalacao PlanejarWinget(Programa programa, AmbienteSistema ambiente)
        {
            if (string.IsNullOrWhiteSpace(programa.WingetId))
                return PlanoInstalacao.Bloqueado(programa, Categoria, "Instalador ausente na pasta de rede e sem \"wingetId\" no config.");
            if (!ambiente.WingetDisponivel)
                return PlanoInstalacao.Bloqueado(programa, Categoria,
                    $"Instalador ausente na pasta de rede e winget indisponível neste {ambiente.NomeWindows} (instale o \"Instalador de Aplicativo\").");

            var args = $"install --id {programa.WingetId.Trim()} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
            if (!string.IsNullOrWhiteSpace(programa.WingetVersao)) args += $" --version {programa.WingetVersao.Trim()}";
            return PlanoInstalacao.Processo(programa, Categoria, FonteInstalacao.Winget,
                InstallCommandBuilder.ConstruirFerramenta(ambiente.CaminhoWinget, args),
                codigosExtras: new[] { WingetJaInstalado });
        }

        private PlanoInstalacao PlanejarChoco(Programa programa, AmbienteSistema ambiente)
        {
            if (string.IsNullOrWhiteSpace(programa.ChocoId))
                return PlanoInstalacao.Bloqueado(programa, Categoria, "Instalador ausente na pasta de rede e sem \"chocoId\" no config.");
            if (!ambiente.DotNet48Instalado)
                return PlanoInstalacao.Bloqueado(programa, Categoria, $"Chocolatey exige o .NET Framework 4.8, ausente neste {ambiente.NomeWindows}.");
            if (!ambiente.Tls12Ativo)
                return PlanoInstalacao.Bloqueado(programa, Categoria,
                    $"TLS 1.2 desativado neste {ambiente.NomeWindows}: o Chocolatey não consegue baixar. Instale a KB3140245 e ative o TLS 1.2 no registro.");
            if (!ambiente.ChocoDisponivel)
                return PlanoInstalacao.Bloqueado(programa, Categoria, "Instalador ausente na pasta de rede e Chocolatey não instalado nesta máquina.");

            var args = $"install {programa.ChocoId.Trim()} -y --no-progress";
            if (!string.IsNullOrWhiteSpace(programa.ChocoVersao)) args += $" --version {programa.ChocoVersao.Trim()}";
            return PlanoInstalacao.Processo(programa, Categoria, FonteInstalacao.Chocolatey,
                InstallCommandBuilder.ConstruirFerramenta(ambiente.CaminhoChoco, args));
        }
    }

    /// <summary>Instalador da pasta de rede (licenciado ou gratuito). Nada é baixado da internet.</summary>
    public sealed class EstrategiaOffline : EstrategiaBase
    {
        public EstrategiaOffline(bool licenciado)
        {
            Categoria = licenciado ? CategoriaInstalacao.OfflineLicenciado : CategoriaInstalacao.OfflineGratuito;
        }

        public override CategoriaInstalacao Categoria { get; }

        public override PlanoInstalacao Planejar(Programa programa, ContextoInstalacao contexto)
        {
            var incompativel = MotivoIncompatibilidade(programa, contexto.Ambiente);
            if (incompativel != null) return PlanoInstalacao.Incompativel(programa, Categoria, incompativel);

            if (TemInstaladorNaRede(programa, contexto, out var comando))
            {
                var lembrete = Categoria == CategoriaInstalacao.OfflineLicenciado
                    ? "Programa licenciado: faça a ativação da licença manualmente após a instalação."
                    : null;
                return PlanoInstalacao.Processo(programa, Categoria, FonteInstalacao.Rede, comando, lembrete);
            }

            var caminho = comando?.CaminhoInstalador ?? "(sem \"instalador\" no config)";
            return PlanoInstalacao.Bloqueado(programa, Categoria, Categoria == CategoriaInstalacao.OfflineLicenciado
                ? $"Instalador licenciado não encontrado na pasta de rede: {caminho}. Copie para lá o pacote de implantação do fabricante."
                : $"Instalador não encontrado na pasta de rede: {caminho}.");
        }
    }

    /// <summary>Copia uma pasta da rede para a máquina (programas portáteis e materiais de aula).</summary>
    public sealed class EstrategiaCopiaPasta : EstrategiaBase
    {
        public override CategoriaInstalacao Categoria => CategoriaInstalacao.CopiaPasta;

        public override PlanoInstalacao Planejar(Programa programa, ContextoInstalacao contexto)
        {
            var incompativel = MotivoIncompatibilidade(programa, contexto.Ambiente);
            if (incompativel != null) return PlanoInstalacao.Incompativel(programa, Categoria, incompativel);
            if (string.IsNullOrWhiteSpace(programa.Instalador))
                return PlanoInstalacao.Bloqueado(programa, Categoria, "Sem pasta de origem (\"instalador\") no config.");

            var origem = InstallCommandBuilder.ResolverCaminho(programa, contexto.PastaInstaladores);
            if (!contexto.PastaExiste(origem))
                return PlanoInstalacao.Bloqueado(programa, Categoria, $"Pasta não encontrada na rede: {origem}");

            var destino = string.IsNullOrWhiteSpace(programa.Destino)
                ? Path.Combine(Expandir(contexto.PastaDestinoCopias), programa.Id)
                : Expandir(programa.Destino);
            return PlanoInstalacao.Copia(programa, origem, Path.GetFullPath(destino));
        }

        private static string Expandir(string caminho) => Environment.ExpandEnvironmentVariables(caminho.Trim());
    }

    /// <summary>Escolhe a estratégia pela categoria do programa e devolve o plano para esta máquina.</summary>
    public sealed class RoteadorInstalacao
    {
        private readonly Dictionary<CategoriaInstalacao, IEstrategiaInstalacao> _estrategias;

        public RoteadorInstalacao(ContextoInstalacao contexto, IEnumerable<IEstrategiaInstalacao> estrategias = null)
        {
            Contexto = contexto ?? throw new ArgumentNullException(nameof(contexto));
            _estrategias = (estrategias ?? Padrao()).ToDictionary(e => e.Categoria);
        }

        public ContextoInstalacao Contexto { get; }

        public static IEnumerable<IEstrategiaInstalacao> Padrao() => new IEstrategiaInstalacao[]
        {
            new EstrategiaGerenciador(),
            new EstrategiaOffline(licenciado: true),
            new EstrategiaOffline(licenciado: false),
            new EstrategiaCopiaPasta()
        };

        public PlanoInstalacao Planejar(Programa programa)
        {
            if (programa == null) throw new ArgumentNullException(nameof(programa));
            var categoria = CategoriaResolver.Resolver(programa);
            if (!_estrategias.TryGetValue(categoria, out var estrategia))
                return PlanoInstalacao.Bloqueado(programa, categoria, $"Nenhuma estratégia para a categoria {categoria}.");
            return estrategia.Planejar(programa, Contexto);
        }
    }
}
