using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class ConstantsFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create("MED018");

    public override FixAllProvider? GetFixAllProvider()
        => null;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Update project settings (refresh the project by switching to the Unity window after applying this fix)",
                createChangedDocument: ct => ApplyFixAsync(context.Document, ct),
                equivalenceKey: "EnableConstantsCodegen"
            ),
            context.Diagnostics.First()
        );

        return Task.CompletedTask;
    }

    static Task<Document> ApplyFixAsync(Document contextDocument, CancellationToken ct)
    {
        const string additionalFileArg = "/additionalfile:\"ProjectSettings/TagManager.asset\"";
        string path = contextDocument.FilePath ?? throw new InvalidOperationException("Unknown file path.");

        // find the /Assets path
        var dir = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (dir != null && dir.Name != "Assets")
            dir = dir.Parent;

        string assetsFolder = dir?.FullName ?? throw new InvalidOperationException("Unable to locate Assets folder.");

        // create or update /Assets/csc.rsp
        string cscPath = Path.Combine(assetsFolder, "csc.rsp");
        if (!File.Exists(cscPath))
        {
            File.WriteAllText(cscPath, additionalFileArg + Environment.NewLine);
        }
        else
        {
            var cscText = File.ReadAllText(cscPath);
            if (!cscText.Contains(additionalFileArg))
            {
                cscText = $"{cscText}\n{additionalFileArg}";
                File.WriteAllText(cscPath, cscText);
            }
        }

        // find the ProjectSettings/ProjectSettings.asset path
        string projectRoot = Directory.GetParent(assetsFolder)!.FullName;
        string projectSettingsFolder = Path.Combine(projectRoot, "ProjectSettings");
        string projectSettingsAsset = Path.Combine(projectSettingsFolder, "ProjectSettings.asset");
        if (!File.Exists(projectSettingsAsset))
            return Task.FromResult(contextDocument);

        // update ProjectSettings.asset
        {
            var lines = File.ReadAllLines(projectSettingsAsset).ToList();
            bool inBlock = false;
            int blockIndent = 0;
            string? dashIndent = null;
            bool platformHasArg = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];

                if (!inBlock)
                {
                    if (line.Trim() is "additionalCompilerArguments:")
                    {
                        inBlock = true;
                        blockIndent = line.TakeWhile(char.IsWhiteSpace).Count();
                    }

                    continue;
                }

                // detect platform key
                if (Regex.IsMatch(line, @"^\s*[^\s].*:\s*$"))
                {
                    if (dashIndent != null && !platformHasArg)
                        lines.Insert(i++, $"{dashIndent}- {additionalFileArg}");

                    dashIndent = new(' ', blockIndent + 2);
                    platformHasArg = false;
                    continue;
                }

                // check list item
                if (line.TrimStart().StartsWith("-"))
                {
                    string value = line.TrimStart()[1..].Trim();
                    if (value == additionalFileArg)
                        platformHasArg = true;

                    continue;
                }

                // exit block
                if (line.Length - line.TrimStart().Length <= blockIndent)
                {
                    if (dashIndent != null && !platformHasArg)
                        lines.Insert(i, $"{dashIndent}- {additionalFileArg}");

                    inBlock = false;
                    break;
                }
            }

            if (inBlock && dashIndent != null && !platformHasArg)
                lines.Add($"{dashIndent}- {additionalFileArg}");

            File.WriteAllLines(projectSettingsAsset, lines);
        }

        return Task.FromResult(contextDocument);
    }
}