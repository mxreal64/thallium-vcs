using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class StatusCommand {
    public static Command Build() {
        var cmd = new Command("status", "Show working tree status");
        cmd.SetHandler(() => {
            try {
                var p      = Repository.requireRoot(Directory.GetCurrentDirectory());
                var items  = Repository.status(p);
                var head   = Repository.readHead(p);
                var headStr = FSharpOption<Domain.TxId>.get_IsSome(head)
                              ? ((Domain.TxId)head.Value).Item[..8]
                              : "(empty)";

                AnsiConsole.MarkupLine($"HEAD → [bold]{headStr}[/]");

                if (items.Length == 0) {
                    AnsiConsole.MarkupLine("[green]Nothing to commit, working tree clean.[/]");
                    return;
                }

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Status")
                    .AddColumn("Path");

                foreach (var (path, statusVal) in items) {
                    string label, color;
                    if (statusVal.IsStaged)         { label = "staged";    color = "green"; }
                    else if (statusVal.IsModified)  { label = "modified";  color = "yellow"; }
                    else if (statusVal.IsUntracked) { label = "untracked"; color = "dim"; }
                    else if (statusVal.IsDeleted)   { label = "deleted";   color = "red"; }
                    else                            { label = "?";         color = "white"; }
                    table.AddRow(new Markup($"[{color}]{label}[/]"), new Text(path));
                }

                AnsiConsole.Write(table);
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[Error] {ex}");
                Environment.Exit(1);
            }
        });

        return cmd;
    }
}
