using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class GitCommand {
    public static Command Build() {
        var cmd = new Command("git", "Bidirectional Git interoperability commands");
        cmd.AddCommand(ImportSubCmd());
        cmd.AddCommand(ExportSubCmd());
        return cmd;
    }

    static Command ImportSubCmd() {
        var srcArg = new Argument<string>("git_repo", () => ".", "Path to source Git repository");
        var destOpt = new Option<string>("--to", () => Directory.GetCurrentDirectory(), "Target Thallium repository directory");

        var sub = new Command("import", "Import full Git history and commit trees into Thallium ledger") { srcArg, destOpt };
        sub.SetHandler((src, dest) => {
            var fullSrc = Path.GetFullPath(src);
            var fullDest = Path.GetFullPath(dest);

            AnsiConsole.Status().Start($"Importing Git history from {fullSrc}…", _ => { });

            var result = GitBridge.importGitRepo(fullSrc, fullDest);
            if (result.IsOk) {
                var count = result.ResultValue;
                AnsiConsole.MarkupLine($"[green]✓[/] Successfully imported [bold]{count}[/] Git commits into Thallium ledger at [bold]{fullDest}[/]");
            }
            else {
                var err = result.ErrorValue;
                AnsiConsole.MarkupLine($"[red]✗ Import failed:[/] {err}");
                Environment.Exit(1);
            }
        }, srcArg, destOpt);

        return sub;
    }

    static Command ExportSubCmd() {
        var destArg = new Argument<string>("git_repo", "Target Git repository path to export to");
        var srcOpt = new Option<string>("--from", () => Directory.GetCurrentDirectory(), "Source Thallium repository");

        var sub = new Command("export", "Export Thallium transactions as a Git repository") { destArg, srcOpt };
        sub.SetHandler((dest, src) => {
            var fullDest = Path.GetFullPath(dest);
            var fullSrc = Path.GetFullPath(src);

            AnsiConsole.Status().Start($"Exporting Thallium transactions to {fullDest}…", _ => { });

            var result = GitBridge.exportToGit(fullSrc, fullDest);
            if (result.IsOk) {
                var count = result.ResultValue;
                AnsiConsole.MarkupLine($"[green]✓[/] Successfully exported [bold]{count}[/] transactions into Git repository at [bold]{fullDest}[/]");
            }
            else {
                var err = result.ErrorValue;
                AnsiConsole.MarkupLine($"[red]✗ Export failed:[/] {err}");
                Environment.Exit(1);
            }
        }, destArg, srcOpt);

        return sub;
    }
}
