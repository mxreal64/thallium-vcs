using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class InitCommand {
    public static Command Build() {
        var dirArg    = new Argument<string>("directory", () => ".", "Directory to initialise (default: current)");
        var authorOpt = new Option<string>("--author", () => Environment.UserName, "Default commit author");

        var cmd = new Command("init", "Initialise a new Thallium repository") { dirArg, authorOpt };
        cmd.SetHandler((dir, author) => {
            var fullDir = Path.GetFullPath(dir);
            Directory.CreateDirectory(fullDir);

            try {
                var genesis = Repository.init(fullDir, author);
                var txId = ((Domain.TxId)genesis.TxId).Item;

                AnsiConsole.MarkupLine($"[green]✓[/] Initialised Thallium repository in [bold]{fullDir}[/]");
                AnsiConsole.MarkupLine($"  Genesis transaction: [dim]{txId[..8]}[/]");
                AnsiConsole.MarkupLine($"  Author:             [dim]{author}[/]");
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, dirArg, authorOpt);

        return cmd;
    }
}
