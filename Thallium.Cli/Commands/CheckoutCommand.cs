using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class CheckoutCommand {
    public static Command Build() {
        var txArg = new Argument<string>("tx_id", "Transaction ID to check out (short or full)");
        var cmd   = new Command("checkout", "Restore the working tree to a given transaction") { txArg };

        cmd.SetHandler(txId => {
            try {
                var p   = Repository.requireRoot(Directory.GetCurrentDirectory());
                var all = Ledger.readAll(p.LedgerPath);

               
                var tx = all.FirstOrDefault(t =>
                    ((Domain.TxId)t.TxId).Item.StartsWith(txId, StringComparison.OrdinalIgnoreCase));

                if (tx == null) {
                    AnsiConsole.MarkupLine($"[red]✗ Transaction '{txId}' not found.[/]");
                    Environment.Exit(1);
                    return;
                }

                var fullId = ((Domain.TxId)tx.TxId).Item;
                AnsiConsole.Status().Start($"Checking out {fullId[..8]}…", _ => {
                    Repository.checkout(p, tx.TxId);
                });

                AnsiConsole.MarkupLine($"[green]✓[/] Working tree restored to [bold]{fullId[..8]}[/]");
                AnsiConsole.MarkupLine($"  [italic]{tx.Summary}[/]");
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, txArg);

        return cmd;
    }
}
