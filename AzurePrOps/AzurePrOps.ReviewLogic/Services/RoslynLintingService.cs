using AzurePrOps.ReviewLogic.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace AzurePrOps.ReviewLogic.Services;

public class RoslynLintingService : ILintingService
{
    public IEnumerable<LintIssue> Analyze(string code)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CSharpCompilation comp = CSharpCompilation.Create("Analysis")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        IEnumerable<Diagnostic> diagnostics = comp.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning || d.Severity == DiagnosticSeverity.Error);

        foreach (Diagnostic d in diagnostics)
        {
            int pos = d.Location.GetLineSpan().StartLinePosition.Line + 1;
            yield return new LintIssue
            {
                LineNumber = pos,
                RuleId     = d.Id,
                Message    = d.GetMessage(),
                Severity   = d.Severity.ToString()
            };
        }
    }
}