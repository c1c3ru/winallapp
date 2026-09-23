using Xunit;

// Os testes compartilham a pasta %TEMP%\WinAllApp-simulacao (kit extraído do .exe) e abrem janelas:
// rodam um de cada vez para um não apagar o log do outro.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
