using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class CommitCommand {
    public static Command Build() {
        var msgOpt    = new Option<string>(["-m", "--message"], () => "", "Commit summary");
        var authorOpt = new Option<string>("--author", () => Environment.UserName, "Commit author");
        var allOpt    = new Option<bool>(["-a", "--all"], "Stage all changes before committing (like git commit -am)");

        var cmd = new Command("commit", "Record staged changes as a new transaction") { msgOpt, authorOpt, allOpt };
        cmd.SetHandler((msg, author, all) => {
            try {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());

                if (all) {
                    AnsiConsole.MarkupLine("[dim]Staging all changes…[/]");
                    Repository.stageAll(p);
                }

                var result = Repository.commit(p, author, msg);
                if (Microsoft.FSharp.Core.FSharpOption<Domain.Transaction>.get_IsNone(result)) {
                    AnsiConsole.MarkupLine("[yellow]Nothing to commit (staging area empty).[/]");
                    return;
                }

                var tx = result.Value;
                var txId = ((Domain.TxId)tx.TxId).Item;

                AnsiConsole.MarkupLine($"[green]✓[/] Transaction [bold]{txId[..8]}[/]");
                AnsiConsole.MarkupLine($"  Intent summary: [italic]{tx.Summary}[/]");
                AnsiConsole.MarkupLine($"  Files changed:  {tx.Changes.Length}");

               
                var table = new Table().Border(TableBorder.Rounded).AddColumn("File").AddColumn("Intent");
                foreach (var fc in tx.Changes) {
                    var intent = fc.Intent.ToString();
                    var color  = intent switch {
                        "Add"         => "green",
                        "Remove"      => "red",
                        "Move"        => "blue",
                        "Rename"      => "blue",
                        "Refactor"    => "yellow",
                        "AIGenerated" => "magenta",
                        _             => "white"
                    };
                    table.AddRow(new Text(fc.Path), new Markup($"[{color}]{intent}[/]"));
                }
                AnsiConsole.Write(table);
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, msgOpt, authorOpt, allOpt);

        return cmd;
    }
}
