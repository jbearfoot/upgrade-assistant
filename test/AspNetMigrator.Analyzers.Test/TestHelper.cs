﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AspNetMigrator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace TestProject
{
    public static class TestHelper
    {
        // Path relative from .\bin\debug\net5.0
        // TODO : Make this configurable so the test can pass from other working dirs
        internal const string TestProjectPath = @"..\..\..\..\TestProject\TestProject.csproj";

        public static async Task<IEnumerable<Diagnostic>> GetDiagnosticsAsync(string documentPath, params string[] diagnosticIds)
        {
            if (documentPath is null)
            {
                throw new ArgumentNullException(nameof(documentPath));
            }

            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(TestProjectPath).ConfigureAwait(false);
            return await GetDiagnosticsFromProjectAsync(project, documentPath, diagnosticIds).ConfigureAwait(false);
        }

        private static async Task<IEnumerable<Diagnostic>> GetDiagnosticsFromProjectAsync(Project project, string documentPath, params string[] diagnosticIds)
        {
            var analyzersToUse = AspNetCoreMigrationAnalyzers.AllAnalyzers.Where(a => a.SupportedDiagnostics.Any(d => diagnosticIds.Contains(d.Id, StringComparer.Ordinal)));
            var compilation = (await project.GetCompilationAsync().ConfigureAwait(false))
                            .WithAnalyzers(ImmutableArray.Create(analyzersToUse.ToArray()));

            return (await compilation.GetAnalyzerDiagnosticsAsync().ConfigureAwait(false))
                .Where(d => d.Location.IsInSource && documentPath.Equals(Path.GetFileName(d.Location.GetLineSpan().Path), StringComparison.Ordinal))
                .Where(d => diagnosticIds.Contains(d.Id, StringComparer.Ordinal));
        }

        public static async Task<Document?> GetSourceAsync(string documentPath)
        {
            if (documentPath is null)
            {
                throw new ArgumentNullException(nameof(documentPath));
            }

            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(TestProjectPath).ConfigureAwait(false);
            return project.Documents.FirstOrDefault(d => documentPath.Equals(Path.GetFileName(d.FilePath)));
        }

        public static async Task<Document> FixSourceAsync(string documentPath, string diagnosticId)
        {
            if (documentPath is null)
            {
                throw new ArgumentNullException(nameof(documentPath));
            }

            using var workspace = MSBuildWorkspace.Create();
            var project = await workspace.OpenProjectAsync(TestProjectPath).ConfigureAwait(false);
            var projectId = project.Id;

            var diagnosticFixed = false;
            var solution = workspace.CurrentSolution;
            do
            {
                diagnosticFixed = false;
                project = solution.GetProject(projectId)!;
                var diagnostics = await GetDiagnosticsFromProjectAsync(project, documentPath, diagnosticId).ConfigureAwait(false);

                foreach (var diagnostic in diagnostics)
                {
                    var doc = project.GetDocument(diagnostic.Location.SourceTree)!;
                    var fixedSolution = await TryFixDiagnosticAsync(diagnostic, doc).ConfigureAwait(false);
                    if (fixedSolution != null)
                    {
                        solution = fixedSolution;
                        diagnosticFixed = true;
                        break;
                    }
                }
            }
            while (diagnosticFixed);

            project = solution.GetProject(projectId)!;
            return project.Documents.First(d => documentPath.Equals(Path.GetFileName(d.FilePath)));
        }

        private static async Task<Solution?> TryFixDiagnosticAsync(Diagnostic diagnostic, Document document)
        {
            if (diagnostic is null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var provider = AspNetCoreMigrationCodeFixers.AllCodeFixProviders.FirstOrDefault(p => p.FixableDiagnosticIds.Contains(diagnostic.Id));

            if (provider is null)
            {
                return null;
            }

            CodeAction? fixAction = null;
            var context = new CodeFixContext(document, diagnostic, (action, _) => fixAction = action, CancellationToken.None);
            await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);

            if (fixAction is null)
            {
                return null;
            }

            var applyOperation = (await fixAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false)).OfType<ApplyChangesOperation>().FirstOrDefault();

            if (applyOperation is null)
            {
                return null;
            }

            return applyOperation.ChangedSolution;
        }
    }
}
