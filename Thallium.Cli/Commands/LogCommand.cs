using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class LogCommand {
    public static Command Build() {
        var limitOpt      = new Option<int>(["-n", "--limit"], () => 20, "Maximum transactions to display");
        var onlineOpt     = new Option<bool>("--oneline", "Compact one-line format");
        var provenanceOpt = new Option<bool>(["-p", "--provenance"], "Show full AI agent prompt, model, and decision lineage");

        var cmd = new Command("log", "Display the transaction timeline") { limitOpt, onlineOpt, provenanceOpt };
        cmd.SetHandler((limit, oneline, provenance) => {
            try {
                var p   = Repository.requireRoot(Directory.GetCurrentDirectory());
                var all = Ledger.readAll(p.LedgerPath);
                var txs = all.Skip(Math.Max(0, all.Length - limit)).Reverse().ToList();
                var head = Repository.readHead(p);
                var headId = FSharpOption<Domain.TxId>.get_IsSome(head)
                             ? ((Domain.TxId)head.Value).Item : "";

                if (txs.Count == 0) {
                    AnsiConsole.MarkupLine("[dim]No transactions yet.[/]");
                    return;
                }

                if (oneline) {
                    foreach (var tx in txs) {
                        var id    = ((Domain.TxId)tx.TxId).Item;
                        var mark  = id == headId ? " [yellow]← HEAD[/]" : "";
                        var ai    = tx.Author.StartsWith("agent:") ? " [magenta][[AI]][/]" : "";
                        var prov  = (provenance && FSharpOption<Domain.AgentMetadata>.get_IsSome(tx.AgentData))
                                    ? $" [cyan]({tx.AgentData.Value.Model})[/]" : "";
                        AnsiConsole.MarkupLine($"[bold]{id[..8]}[/] {Markup.Escape(tx.Summary)}{ai}{prov}{mark}");
                    }
                }
                else {
                    foreach (var tx in txs) {
                        var id      = ((Domain.TxId)tx.TxId).Item;
                        var isHead  = id == headId;
                        var tsLocal = tx.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                        var isAI    = tx.Author.StartsWith("agent:");

                        var provDetails = "";
                        if (provenance && FSharpOption<Domain.AgentMetadata>.get_IsSome(tx.AgentData)) {
                            var ad = tx.AgentData.Value;
                            provDetails = $"\n[cyan]Agent Model:[/]   {Markup.Escape(ad.Model)}\n" +
                                          $"[cyan]Agent Prompt:[/]  [dim]{Markup.Escape(ad.Prompt)}[/]\n";
                            if (FSharpOption<string>.get_IsSome(ad.ReasoningSummary)) {
                                provDetails += $"[cyan]Reasoning:[/]     [italic]{Markup.Escape(ad.ReasoningSummary.Value)}[/]\n";
                            }
                            if (FSharpOption<int>.get_IsSome(ad.TokensUsed)) {
                                provDetails += $"[cyan]Tokens Used:[/]   {ad.TokensUsed.Value}\n";
                            }
                        }

                        var panel = new Panel(
                            new Markup(
                                $"[bold]{Markup.Escape(tx.Summary)}[/]\n" +
                                $"[dim]Author:[/]        {(isAI ? "[magenta]" : "")}{Markup.Escape(tx.Author)}{(isAI ? "[/]" : "")}\n" +
                                $"[dim]Time:[/]          {tsLocal}\n" +
                                $"[dim]Changes:[/]       {tx.Changes.Length} file(s)\n" +
                                (FSharpOption<Domain.SandboxId>.get_IsSome(tx.FromSandbox)
                                    ? $"[dim]Sandbox:[/]       {((Domain.SandboxId)tx.FromSandbox.Value).Item[..8]}\n"
                                    : "") +
                                provDetails)) {
                            Header = new PanelHeader(
                                $"[bold]{id[..8]}[/]" + (isHead ? " [yellow]← HEAD[/]" : ""),
                                Justify.Left),
                            Border = isHead ? BoxBorder.Double : BoxBorder.Rounded
                        };

                        AnsiConsole.Write(panel);
                    }
                }
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, limitOpt, onlineOpt, provenanceOpt);

        return cmd;
    }
}
