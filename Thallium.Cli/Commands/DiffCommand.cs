// SPDX-License-Identifier: MPL-2.0
using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class DiffCommand
{
    public static Command Build()
    {
        var targetAOpt = new Argument<string>("tx_a", () => "HEAD~1", "Base transaction ID or HEAD~1");
        var targetBOpt = new Argument<string>("tx_b", () => "HEAD", "Target transaction ID or HEAD");

        var cmd = new Command("diff", "Show semantic intent-aware unified diff between transactions") { targetAOpt, targetBOpt };

        cmd.SetHandler((targetA, targetB) =>
        {
            try
            {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());
                var allTxs = Ledger.readAll(p.LedgerPath);

                if (allTxs.Length < 1)
                {
                    AnsiConsole.MarkupLine("[dim]No transactions in repository.[/]");
                    return;
                }

                Domain.TxId ResolveTarget(string name)
                {
                    if (name.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                    {
                        var h = Repository.readHead(p);
                        return h.Value;
                    }
                    if (name.Equals("HEAD~1", StringComparison.OrdinalIgnoreCase) || name.Equals("HEAD~", StringComparison.OrdinalIgnoreCase))
                    {
                        if (allTxs.Length >= 2) return allTxs[allTxs.Length - 2].TxId;
                        return allTxs[0].TxId;
                    }
                    var match = allTxs.FirstOrDefault(t => ((Domain.TxId)t.TxId).Item.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match.TxId;
                    return Domain.TxId.NewTxId(name);
                }

                var txA = ResolveTarget(targetA);
                var txB = ResolveTarget(targetB);

                var aStr = ((Domain.TxId)txA).Item[..8];
                var bStr = ((Domain.TxId)txB).Item[..8];

                AnsiConsole.MarkupLine($"[dim]Comparing[/] [bold]{aStr}[/] [dim]→[/] [bold]{bStr}[/]
");

                var diffs = DiffEngine.diffTransactions(p.ObjectsDir, p.LedgerPath, txA, txB);

                if (diffs.Length == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No differences found.[/]");
                    return;
                }

                foreach (var d in diffs)
                {
                    var intentColor = d.Intent.ToString() switch
                    {
                        "Add" => "green",
                        "Remove" => "red",
                        "Move" => "blue",
                        "Rename" => "blue",
                        "Refactor" => "yellow",
                        _ => "cyan"
                    };

                    var rule = new Rule($"[{intentColor}]{d.Path} ({d.Intent}) +{d.Additions} -{d.Deletions}[/]");
                    rule.LeftJustified();
                    AnsiConsole.Write(rule);

                    foreach (var line in d.Lines)
                    {
                        if (line.Kind.IsAddition)
                        {
                            AnsiConsole.MarkupLine($"[green]+ {Markup.Escape(line.Text)}[/]");
                        }
                        else if (line.Kind.IsDeletion)
                        {
                            AnsiConsole.MarkupLine($"[red]- {Markup.Escape(line.Text)}[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[dim]  {Markup.Escape(line.Text)}[/]");
                        }
                    }
                    AnsiConsole.WriteLine();
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, targetAOpt, targetBOpt);

        return cmd;
    }
}
