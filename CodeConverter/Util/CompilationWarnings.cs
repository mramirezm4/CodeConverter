using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ICSharpCode.CodeConverter.Util
{
    public static class CompilationWarnings
    {
        public static string WarningsForCompilation(Compilation finalCompilation, string compilationDescription)
        {
            var targetErrors = GetDiagnostics(finalCompilation);
            return targetErrors.Any()
                ? $"{Environment.NewLine}{targetErrors.Count} {compilationDescription} compilation errors:{Environment.NewLine}{String.Join(Environment.NewLine, targetErrors)}"
                : null;
        }

        private static List<string> GetDiagnostics(Compilation compilation)
        {
            var diagnostics = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}: {d.GetMessage()}")
                .Distinct()
                .ToList();
            return diagnostics;
        }
    }
}
