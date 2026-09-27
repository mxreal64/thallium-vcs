// SPDX-License-Identifier: MPL-2.0
using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class RollbackCommand {
    public static Command Build() {
        var txArg     = new Argument<string>("tx_id", "Transaction to roll back to");
        var authorOpt = new Option<string>("--author", () => Environment.UserName, "Author for the rollback record");
        var yesOpt    = new Option<bool>(["-y", "--yes"], "Skip confirmation prompt");

        var cmd = new Command("rollback", "Roll back the working tree to a prior transaction (non-destructive)") { txArg, authorOpt, yesOpt };

        cmd.SetHandler((txId, author, yes) => {
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

                if (!yes) {
                    AnsiConsole.MarkupLine($"[yellow]⚠[/]  This will roll back to [bold]{fullId[..8]}[/]: [italic]{tx.Summary}[/]");
                    AnsiConsole.MarkupLine("  A new revert transaction will be appended (history is preserved).");
                    var confirm = AnsiConsole.Confirm("Proceed?", false);
                    if (!confirm) { AnsiConsole.MarkupLine("[dim]Aborted.[/]"); return; }
                }

                AnsiConsole.Status().Start("Rolling back…", _ => {
                    Repository.rollback(p, tx.TxId, author);
                });

                AnsiConsole.MarkupLine($"[green]✓[/] Rolled back to [bold]{fullId[..8]}[/]");
                AnsiConsole.MarkupLine("  A revert transaction has been appended to the ledger.");
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, txArg, authorOpt, yesOpt);

        return cmd;
    }
}
