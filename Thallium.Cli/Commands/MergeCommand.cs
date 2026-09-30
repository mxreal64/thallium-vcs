using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class MergeCommand {
    public static Command Build() {
        var txArg     = new Argument<string>("tx_id", "Transaction to merge into HEAD");
        var authorOpt = new Option<string>("--author", () => Environment.UserName, "Author for the merge transaction");

        var cmd = new Command("merge", "Merge a transaction into HEAD using logical set-union") { txArg, authorOpt };

        cmd.SetHandler((txId, author) => {
            try {
                var p   = Repository.requireRoot(Directory.GetCurrentDirectory());
                var all = Ledger.readAll(p.LedgerPath);

                var theirTx = all.FirstOrDefault(t =>
                    ((Domain.TxId)t.TxId).Item.StartsWith(txId, StringComparison.OrdinalIgnoreCase));

                if (theirTx == null) {
                    AnsiConsole.MarkupLine($"[red]✗ Transaction '{txId}' not found.[/]");
                    Environment.Exit(1);
                    return;
                }

                var headId = Repository.readHead(p);
                if (FSharpOption<Domain.TxId>.get_IsNone(headId)) {
                    AnsiConsole.MarkupLine("[red]✗ No HEAD. Cannot merge into an empty repo.[/]");
                    Environment.Exit(1);
                    return;
                }

                var ourTx = Ledger.findById(p.LedgerPath, headId.Value);
                if (FSharpOption<Domain.Transaction>.get_IsNone(ourTx)) {
                    AnsiConsole.MarkupLine("[red]✗ HEAD transaction not found in ledger.[/]");
                    Environment.Exit(1);
                    return;
                }

                AnsiConsole.Status().Start("Running merge engine…", _ => { });

                var result = MergeEngine.merge(p.ObjectsDir, p.LedgerPath, ourTx.Value, theirTx, author);

                if (result.IsAutoMerged) {
                   
                    var merged = ((Domain.MergeResult.AutoMerged)result).Item;
                    AnsiConsole.MarkupLine($"[green]✓ Auto-merged[/] — {merged.Summary}");
                    Repository.checkout(p, merged.TxId);
                }
                else if (result.IsNeedsReview) {
                    var cards = ((Domain.MergeResult.NeedsReview)result).Item;
                    AnsiConsole.MarkupLine($"[yellow]⚠ Merge needs review — {cards.Length} conflict(s)[/]");

                    foreach (var card in cards) {
                        AnsiConsole.Write(new Rule($"[red]Conflict: {card.ConflictPath}[/]"));
                        AnsiConsole.MarkupLine($"[dim]{card.Rationale}[/]");

                        var choice = AnsiConsole.Prompt(
                            new SelectionPrompt<string>()
                                .Title("Choose resolution:")
                                .AddChoices(card.OptionA.Label, card.OptionB.Label, card.OptionC.Label));

                        AnsiConsole.MarkupLine($"  Chose: [bold]{choice}[/]");
                       
                    }
                }
                else {
                    var reason = ((Domain.MergeResult.Incompatible)result).Item;
                    AnsiConsole.MarkupLine($"[red]✗ Incompatible: {reason}[/]");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, txArg, authorOpt);

        return cmd;
    }
}
