using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class GcCommand {
    public static Command Build() {
        var cmd = new Command("gc", "Garbage collect orphaned objects and optimize repository storage");
        cmd.SetHandler(() => {
            try {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());

                AnsiConsole.Status().Start("Running garbage collector and compacting storage…", _ => { });

                var res = GarbageCollector.collect(p.ObjectsDir, p.LedgerPath, p.TlDir);

                AnsiConsole.MarkupLine("[green]✓[/] Garbage collection complete");
                AnsiConsole.MarkupLine($"  Active Transactions: [bold]{res.CompactedTxCount}[/]");
                AnsiConsole.MarkupLine($"  Blobs Before:        {res.TotalBlobsBefore}");
                AnsiConsole.MarkupLine($"  Blobs Retained:      [bold]{res.TotalBlobsAfter}[/]");
                AnsiConsole.MarkupLine($"  Blobs Pruned:        [green]{res.PrunedBlobsCount}[/]");
                AnsiConsole.MarkupLine($"  Disk Space Reclaimed:[bold]{res.BytesReclaimed:N0}[/] bytes");
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        });

        return cmd;
    }
}
