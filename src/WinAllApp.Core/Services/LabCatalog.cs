using System;
using System.Collections.Generic;
using System.Linq;
using WinAllApp.Core.Models;

namespace WinAllApp.Core.Services
{
    /// <summary>Consultas de navegação: Bloco → Laboratórios → Programas daquela sala.</summary>
    public sealed class LabCatalog
    {
        private readonly InstallerConfig _config;
        private readonly Dictionary<string, Programa> _programas;

        public LabCatalog(InstallerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _programas = new Dictionary<string, Programa>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in config.Programas.Where(p => !string.IsNullOrWhiteSpace(p.Id)))
                if (!_programas.ContainsKey(p.Id)) _programas.Add(p.Id, p);
        }

        public IReadOnlyList<Bloco> Blocos => _config.Blocos;

        public IReadOnlyList<Laboratorio> ObterLaboratorios(string blocoId)
        {
            var bloco = _config.Blocos.FirstOrDefault(b => string.Equals(b.Id, blocoId, StringComparison.OrdinalIgnoreCase));
            return bloco?.Laboratorios ?? new List<Laboratorio>();
        }

        /// <summary>Somente os programas mapeados para o laboratório, na ordem do config, sem repetição.</summary>
        public IReadOnlyList<Programa> ObterProgramas(Laboratorio laboratorio)
        {
            if (laboratorio == null) return new List<Programa>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lista = new List<Programa>();
            foreach (var id in laboratorio.Programas)
            {
                if (vistos.Add(id) && _programas.TryGetValue(id, out var p)) lista.Add(p);
            }
            return lista;
        }

        public IReadOnlyList<Programa> ObterProgramas(string blocoId, string laboratorioId)
        {
            var lab = ObterLaboratorios(blocoId)
                .FirstOrDefault(l => string.Equals(l.Id, laboratorioId, StringComparison.OrdinalIgnoreCase));
            return ObterProgramas(lab);
        }
    }
}
