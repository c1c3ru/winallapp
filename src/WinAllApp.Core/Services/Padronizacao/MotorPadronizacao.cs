using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinAllApp.Core.Services.Padronizacao
{
    public enum StatusEtapa
    {
        Pendente,
        Executando,
        Concluida,
        ConcluidaComAviso,
        Falhou,
        Ignorada
    }

    public sealed class ResultadoEtapa
    {
        public ResultadoEtapa(StatusEtapa status, string resumo, string detalhes = null)
        {
            Status = status;
            Resumo = resumo ?? string.Empty;
            Detalhes = detalhes ?? string.Empty;
        }

        public StatusEtapa Status { get; }
        public string Resumo { get; }
        public string Detalhes { get; }
    }

    /// <summary>Um passo do checklist: um script PowerShell, uma ação do próprio app, ou as duas coisas.</summary>
    public sealed class EtapaPadronizacao
    {
        internal EtapaPadronizacao(string id, string topico, string titulo, string script, bool essencial = false)
        {
            Id = id;
            Topico = topico;
            Titulo = titulo;
            Script = script;
            Essencial = essencial;
        }

        public string Id { get; }

        /// <summary>Tópico do manual ("3", "6.1", "9.6"...).</summary>
        public string Topico { get; }

        public string Titulo { get; }

        /// <summary>Script do Windows PowerShell; null quando a etapa é só do app (teste de rede).</summary>
        public string Script { get; }

        /// <summary>Variáveis de ambiente passadas ao script (cada etapa recebe só o que usa).</summary>
        public Dictionary<string, string> Variaveis { get; } = new Dictionary<string, string>();

        /// <summary>Se falhar, as etapas seguintes não rodam.</summary>
        public bool Essencial { get; }

        /// <summary>Ação feita antes do script (ex.: copiar o papel de parede para o disco local).</summary>
        internal Func<EtapaPadronizacao, CancellationToken, Task> Preparar { get; set; }

        /// <summary>Etapa sem script: a própria ação devolve o resultado.</summary>
        internal Func<CancellationToken, Task<ResultadoEtapa>> Acao { get; set; }
    }

    /// <summary>
    /// Automação dos tópicos 3 a 8, 9.6 (impressora genérica por IP) e 9.7 do checklist institucional, no Windows 10 e 11.
    /// Planejar monta as etapas conforme o Windows e as opções; ExecutarAsync roda uma de cada vez.
    /// </summary>
    public sealed class MotorPadronizacao
    {
        private readonly IExecutorPowerShell _powerShell;
        private readonly IDiagnosticoRede _rede;

        public MotorPadronizacao(IExecutorPowerShell powerShell, IDiagnosticoRede rede)
        {
            _powerShell = powerShell ?? throw new ArgumentNullException(nameof(powerShell));
            _rede = rede ?? throw new ArgumentNullException(nameof(rede));
        }

        public TimeSpan TimeoutScript { get; set; } = TimeSpan.FromMinutes(10);
        public TimeSpan TimeoutRede { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan TimeoutCopia { get; set; } = TimeSpan.FromSeconds(60);

        public IReadOnlyList<EtapaPadronizacao> Planejar(OpcoesPadronizacao opcoes, VersaoPadronizacao versao)
        {
            if (opcoes == null) throw new ArgumentNullException(nameof(opcoes));
            if (versao == VersaoPadronizacao.NaoSuportado)
                throw new NotSupportedException("A padronização é só para Windows 10 e 11.");
            var erros = opcoes.Validar();
            if (erros.Count > 0) throw new ArgumentException(string.Join(" ", erros), nameof(opcoes));

            var windows = SistemaPadronizacao.Nome(versao);
            var contaUsuario = opcoes.ContaDoUsuario;
            var usuarioAdmin = opcoes.Perfil == PerfilDoUsuario.Bolsista ? "1" : "0";
            var etapas = new List<EtapaPadronizacao>();

            var sistema = new EtapaPadronizacao("sistema", "1", "Conferir o Windows e o modo Administrador",
                ScriptsPadronizacao.VerificarSistema(), essencial: true);
            sistema.Variaveis[ScriptsPadronizacao.VarWindows] = windows;
            etapas.Add(sistema);

            var rede = new EtapaPadronizacao("rede", "8", "Testar a rede e a internet", null);
            rede.Acao = c => TestarRedeAsync(opcoes, c);
            etapas.Add(rede);

            var ativacao = new EtapaPadronizacao("ativacao", "3", "Ativar o Windows (chave da etiqueta)", ScriptsPadronizacao.Ativacao(versao));
            ativacao.Variaveis[ScriptsPadronizacao.VarChave] = (opcoes.ChaveProduto ?? string.Empty).Trim().ToUpperInvariant();
            etapas.Add(ativacao);

            var contas = new EtapaPadronizacao("contas", "4", $"Criar as contas Informatica e {contaUsuario}", ScriptsPadronizacao.Contas());
            contas.Variaveis[ScriptsPadronizacao.VarSenha] = opcoes.SenhaInformatica;
            contas.Variaveis[ScriptsPadronizacao.VarContaUsuario] = contaUsuario;
            contas.Variaveis[ScriptsPadronizacao.VarUsuarioAdmin] = usuarioAdmin;
            etapas.Add(contas);

            etapas.Add(new EtapaPadronizacao("uac", "5", "Controle de Conta: Nunca notificar", ScriptsPadronizacao.Uac()));

            var papel = new EtapaPadronizacao("papel", "6.1", "Aplicar o papel de parede", ScriptsPadronizacao.PapelDeParede());
            papel.Variaveis[ScriptsPadronizacao.VarContaUsuario] = contaUsuario;
            papel.Preparar = (etapa, c) => CopiarPapeisAsync(opcoes, etapa, c);
            etapas.Add(papel);

            var gpos = new EtapaPadronizacao("gpo", "6.2", "GPOs: impedir a troca de tema e de plano de fundo", ScriptsPadronizacao.GposPersonalizacao());
            gpos.Variaveis[ScriptsPadronizacao.VarContaUsuario] = contaUsuario;
            etapas.Add(gpos);

            etapas.Add(new EtapaPadronizacao("update", "6.3", "Desabilitar as atualizações automáticas", ScriptsPadronizacao.WindowsUpdate(versao)));

            var nome = new EtapaPadronizacao("nome", "7", "Renomear para " + opcoes.NomeComputador, ScriptsPadronizacao.RenomearComputador());
            nome.Variaveis[ScriptsPadronizacao.VarNome] = opcoes.NomeComputador;
            etapas.Add(nome);

            if (opcoes.AdicionarImpressora)
            {
                var impressora = new EtapaPadronizacao("impressora", "9.6", "Adicionar a impressora " + opcoes.IpImpressora.Trim(), ScriptsPadronizacao.Impressora());
                impressora.Variaveis[ScriptsPadronizacao.VarImpressoraIp] = opcoes.IpImpressora.Trim();
                impressora.Variaveis[ScriptsPadronizacao.VarImpressoraNome] = opcoes.NomeImpressoraFinal;
                etapas.Add(impressora);
            }

            if (opcoes.LimparAreaDeTrabalho)
            {
                var limpeza = new EtapaPadronizacao("limpeza", "9.7", "Limpar atalhos e instaladores da área de trabalho", ScriptsPadronizacao.LimparAreaDeTrabalho());
                limpeza.Variaveis[ScriptsPadronizacao.VarExecutavel] = opcoes.ExecutavelEmUso ?? string.Empty;
                etapas.Add(limpeza);
            }

            return etapas;
        }

        /// <summary>
        /// Roda as etapas em ordem. Uma falha não impede as seguintes, exceto na etapa essencial (Windows/Administrador).
        /// Cancelar marca o que falta como ignorado.
        /// </summary>
        public async Task<IReadOnlyList<ResultadoEtapa>> ExecutarAsync(IReadOnlyList<EtapaPadronizacao> etapas,
            IProgress<KeyValuePair<EtapaPadronizacao, ResultadoEtapa>> progresso, CancellationToken cancelamento)
        {
            var resultados = new List<ResultadoEtapa>();
            var interromper = (string)null;
            foreach (var etapa in etapas)
            {
                if (interromper == null && cancelamento.IsCancellationRequested) interromper = "Cancelado pelo técnico.";
                if (interromper != null)
                {
                    var ignorada = new ResultadoEtapa(StatusEtapa.Ignorada, interromper);
                    resultados.Add(ignorada);
                    progresso?.Report(new KeyValuePair<EtapaPadronizacao, ResultadoEtapa>(etapa, ignorada));
                    continue;
                }

                progresso?.Report(new KeyValuePair<EtapaPadronizacao, ResultadoEtapa>(etapa, new ResultadoEtapa(StatusEtapa.Executando, "Executando…")));
                ResultadoEtapa resultado;
                try
                {
                    resultado = await ExecutarEtapaAsync(etapa, cancelamento).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
                {
                    resultado = new ResultadoEtapa(StatusEtapa.Falhou, "Cancelado pelo técnico durante a etapa.");
                    interromper = "Cancelado pelo técnico.";
                }
                catch (Exception ex)
                {
                    resultado = new ResultadoEtapa(StatusEtapa.Falhou, ex.Message, ex.ToString());
                }

                resultados.Add(resultado);
                progresso?.Report(new KeyValuePair<EtapaPadronizacao, ResultadoEtapa>(etapa, resultado));
                if (etapa.Essencial && resultado.Status == StatusEtapa.Falhou)
                    interromper = "Não executado: a verificação do Windows falhou.";
            }
            return resultados;
        }

        private async Task<ResultadoEtapa> ExecutarEtapaAsync(EtapaPadronizacao etapa, CancellationToken cancelamento)
        {
            if (etapa.Acao != null) return await etapa.Acao(cancelamento).ConfigureAwait(false);
            if (etapa.Preparar != null) await etapa.Preparar(etapa, cancelamento).ConfigureAwait(false);
            var saida = await _powerShell.ExecutarAsync(etapa.Script, etapa.Variaveis, TimeoutScript, cancelamento).ConfigureAwait(false);
            return Interpretar(saida);
        }

        /// <summary>Código 0/2/1 do script → concluída/aviso/falhou; o resumo é a linha RESULTADO: (ou ERRO:) e os avisos.</summary>
        public static ResultadoEtapa Interpretar(ResultadoPowerShell saida)
        {
            var linhas = saida.Saida.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            string Depois(string prefixo) => linhas.LastOrDefault(l => l.StartsWith(prefixo, StringComparison.Ordinal))?.Substring(prefixo.Length).Trim();
            var avisos = linhas.Where(l => l.StartsWith("AVISO:", StringComparison.Ordinal)).Select(l => l.Substring(6).Trim()).ToList();

            switch (saida.Codigo)
            {
                case 0:
                    return new ResultadoEtapa(StatusEtapa.Concluida, Depois("RESULTADO:") ?? "Concluído.", saida.Saida);
                case 2:
                    var resumo = string.Join(" ", avisos.Concat(new[] { Depois("RESULTADO:") }).Where(t => !string.IsNullOrEmpty(t)).Distinct());
                    return new ResultadoEtapa(StatusEtapa.ConcluidaComAviso, resumo.Length > 0 ? resumo : "Concluído com aviso.", saida.Saida);
                default:
                    var erro = Depois("ERRO:") ?? linhas.LastOrDefault() ?? "O PowerShell terminou com o código " + saida.Codigo + ".";
                    return new ResultadoEtapa(StatusEtapa.Falhou, erro, saida.Saida);
            }
        }

        private async Task CopiarPapeisAsync(OpcoesPadronizacao opcoes, EtapaPadronizacao etapa, CancellationToken cancelamento)
        {
            var pastaLocal = string.IsNullOrWhiteSpace(opcoes.PastaLocalPapeis) ? PapeisDeParede.PastaLocalPadrao() : opcoes.PastaLocalPapeis;
            var papeis = await PapeisDeParede.CopiarAsync(opcoes.PastaPapeisDeParede.Trim(), pastaLocal,
                opcoes.Perfil == PerfilDoUsuario.Aluno, TimeoutCopia, cancelamento).ConfigureAwait(false);
            etapa.Variaveis[ScriptsPadronizacao.VarPapelAdm] = papeis.Adm;
            etapa.Variaveis[ScriptsPadronizacao.VarPapelLab] = papeis.Lab ?? string.Empty;
        }

        private async Task<ResultadoEtapa> TestarRedeAsync(OpcoesPadronizacao opcoes, CancellationToken cancelamento)
        {
            var servidor = string.IsNullOrWhiteSpace(opcoes.ServidorArquivos)
                ? DiagnosticoRede.ServidorDoCaminho(opcoes.PastaPapeisDeParede) ?? OpcoesPadronizacao.ServidorArquivosPadrao
                : opcoes.ServidorArquivos.Trim();

            var respondeTarefa = _rede.RespondeAsync(servidor, TimeoutRede, cancelamento);
            var pastaTarefa = _rede.PastaAcessivelAsync(opcoes.PastaPapeisDeParede, TimeoutRede, cancelamento);
            var internetTarefa = _rede.InternetAsync(TimeoutRede, cancelamento);
            await Task.WhenAll(respondeTarefa, pastaTarefa, internetTarefa).ConfigureAwait(false);

            var responde = respondeTarefa.Result;
            var pasta = pastaTarefa.Result;
            var internet = internetTarefa.Result;
            var partes = new List<string>
            {
                $"Servidor de arquivos {servidor}: {(responde ? "responde" : "não responde")}",
                $"pasta da padronização: {(pasta ? "acessível" : "inacessível")}",
                $"internet: {(internet ? "ok" : "sem acesso")}"
            };
            var texto = string.Join(" · ", partes) + ".";
            if (responde && pasta && internet) return new ResultadoEtapa(StatusEtapa.Concluida, texto);
            return new ResultadoEtapa(StatusEtapa.ConcluidaComAviso,
                texto + " Confira o cabo, o adaptador de rede e o driver da placa (tópico 8).");
        }
    }
}
