using System;
using System.Collections.Generic;
using WinAllApp.Core.Services;

namespace WinAllApp.Core.ViewModels
{
    public sealed class PassoOnboarding
    {
        public PassoOnboarding(int numero, string titulo, string texto, string dica)
        {
            Numero = numero;
            Titulo = titulo;
            Texto = texto;
            Dica = dica;
        }

        public int Numero { get; }
        public string Titulo { get; }
        public string Texto { get; }
        public string Dica { get; }
    }

    /// <summary>Tutorial de boas-vindas em 3 passos, com Voltar / Avançar / Concluir.</summary>
    public sealed class OnboardingViewModel : ObservableObject
    {
        private readonly PreferenciasUsuario _preferencias;
        private int _indice;
        private bool _naoMostrarNovamente = true;

        public OnboardingViewModel(string pastaRede, PreferenciasUsuario preferencias)
        {
            _preferencias = preferencias;
            var pasta = string.IsNullOrWhiteSpace(pastaRede) ? "(definida em \"pastaInstaladores\" no config.json)" : pastaRede;

            Passos = new List<PassoOnboarding>
            {
                new PassoOnboarding(1, "Escolha o bloco e o laboratório",
                    "No menu à esquerda, escolha o Bloco (BL1 ou BL2) e depois o Laboratório. A lista mostra só os programas daquela sala.",
                    "Laboratórios que usam apenas os programas padrões aparecem com um aviso e sem itens para instalar."),
                new PassoOnboarding(2, "Confira a pasta de rede",
                    "Os instaladores vêm da pasta de rede do servidor:\n" + pasta +
                    "\nO menu à esquerda mostra se ela está acessível. Se aparecer INACESSÍVEL, conecte a máquina à rede do campus e clique em Verificar.",
                    "Para usar outra pasta, rode WinAllApp.exe --extrair-config e altere \"pastaInstaladores\" no config.json."),
                new PassoOnboarding(3, "Instale em lote",
                    "Marque os programas (ou use \"Selecionar Todos do Laboratório\") e clique em Instalar selecionados. Cada item mostra o resultado ao terminar.",
                    "Programas licenciados (AutoCAD, MATLAB, Proteus…) são instalados pela pasta de rede; a ativação da licença é feita depois, à mão.")
            };

            VoltarCommand = new RelayCommand(() => Indice--, () => PodeVoltar);
            AvancarCommand = new RelayCommand(Avancar);
            PularCommand = new RelayCommand(Concluir);
        }

        public IReadOnlyList<PassoOnboarding> Passos { get; }
        public RelayCommand VoltarCommand { get; }
        public RelayCommand AvancarCommand { get; }
        public RelayCommand PularCommand { get; }

        /// <summary>A janela fecha quando este evento dispara.</summary>
        public event EventHandler Concluido;

        public int Indice
        {
            get => _indice;
            private set
            {
                var novo = Math.Max(0, Math.Min(Passos.Count - 1, value));
                if (!Set(ref _indice, novo)) return;
                OnPropertyChanged(nameof(PassoAtual));
                OnPropertyChanged(nameof(PodeVoltar));
                OnPropertyChanged(nameof(UltimoPasso));
                OnPropertyChanged(nameof(TextoAvancar));
                OnPropertyChanged(nameof(Progresso));
                VoltarCommand.NotificarMudanca();
            }
        }

        public PassoOnboarding PassoAtual => Passos[Indice];
        public bool PodeVoltar => Indice > 0;
        public bool UltimoPasso => Indice == Passos.Count - 1;
        public string TextoAvancar => UltimoPasso ? "Concluir" : "Avançar";
        public string Progresso => $"Passo {Indice + 1} de {Passos.Count}";

        public bool NaoMostrarNovamente
        {
            get => _naoMostrarNovamente;
            set => Set(ref _naoMostrarNovamente, value);
        }

        public bool Finalizado { get; private set; }

        private void Avancar()
        {
            if (UltimoPasso) Concluir();
            else Indice++;
        }

        private void Concluir()
        {
            if (Finalizado) return;
            Finalizado = true;
            if (_preferencias != null) _preferencias.OnboardingConcluido = NaoMostrarNovamente;
            Concluido?.Invoke(this, EventArgs.Empty);
        }
    }
}
