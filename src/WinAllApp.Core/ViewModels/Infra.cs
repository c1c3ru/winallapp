using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace WinAllApp.Core.ViewModels
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T campo, T valor, [CallerMemberName] string propriedade = null)
        {
            if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
            campo = valor;
            OnPropertyChanged(propriedade);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propriedade = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriedade));
    }

    /// <summary>ICommand simples. ICommand vive em System.dll no .NET 4.8, então o Core não depende de WPF.</summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _executar;
        private readonly Func<bool> _podeExecutar;

        public RelayCommand(Action executar, Func<bool> podeExecutar = null)
        {
            _executar = executar ?? throw new ArgumentNullException(nameof(executar));
            _podeExecutar = podeExecutar;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => _podeExecutar == null || _podeExecutar();

        public void Execute(object parameter)
        {
            if (CanExecute(parameter)) _executar();
        }

        public void NotificarMudanca() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Comando assíncrono: a UI dispara e continua responsiva enquanto a tarefa roda.</summary>
    public sealed class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _executar;
        private readonly Func<bool> _podeExecutar;
        private bool _executando;

        public AsyncRelayCommand(Func<Task> executar, Func<bool> podeExecutar = null)
        {
            _executar = executar ?? throw new ArgumentNullException(nameof(executar));
            _podeExecutar = podeExecutar;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) => !_executando && (_podeExecutar == null || _podeExecutar());

        /// <summary>Tarefa da última execução (permite aguardar o término em testes).</summary>
        public Task Execucao { get; private set; } = Task.CompletedTask;

        public void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;
            Execucao = ExecutarAsync();
        }

        private async Task ExecutarAsync()
        {
            _executando = true;
            NotificarMudanca();
            try
            {
                await _executar();
            }
            finally
            {
                _executando = false;
                NotificarMudanca();
            }
        }

        public void NotificarMudanca() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
