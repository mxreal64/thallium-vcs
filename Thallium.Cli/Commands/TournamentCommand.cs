using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class TournamentCommand
{
    public static Command Build()
    {
        var sandboxesArg = new Argument<string[]>("sandboxes", "List of candidate sandbox IDs to evaluate");
        var cmdOpt       = new Option<string>(["-c", "--cmd"], () => "", "Test command to execute for evaluation");
        var autoCommitOpt= new Option<bool>(["-a", "--auto-commit"], () => false, "Automatically commit the winning sandbox to HEAD");
        var msgOpt       = new Option<string>(["-m", "--message"], () => "", "Commit message if auto-committing");
        var authorOpt    = new Option<string>("--author", () => Environment.UserName, "Commit author");

        var cmd = new Command("tournament", "Run speculative parallel tournament evaluation across candidate sandboxes")
        {
            sandboxesArg,
            cmdOpt,
            autoCommitOpt,
            msgOpt,
            authorOpt
        };

        cmd.SetHandler((sandboxes, customCmd, autoCommit, msg, author) =>
        {
            try
            {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());

                if (sandboxes == null || sandboxes.Length == 0)
                {
                    AnsiConsole.MarkupLine("[red]✗ Please provide at least one sandbox ID to evaluate.[/]");
                    return;
                }

                var loadedSandboxes = new List<Domain.SandboxState>();
                foreach (var id in sandboxes)
                {
                    var sbId = Domain.SandboxId.NewSandboxId(id);
                    var sb = SandboxManager.loadFromRepo(p.TlDir, sbId);
                    if (FSharpOption<Domain.SandboxState>.get_IsSome(sb))
                    {
                        loadedSandboxes.Add(sb.Value);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]⚠ Sandbox '{id}' not found or expired — skipping.[/]");
                    }
                }

                if (loadedSandboxes.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]✗ No valid sandboxes found to evaluate.[/]");
                    Environment.Exit(1);
                    return;
                }

                var fsharpList = ListModule.OfSeq(loadedSandboxes);
                var cmdOption = string.IsNullOrWhiteSpace(customCmd)
                    ? FSharpOption<string>.None
                    : FSharpOption<string>.Some(customCmd);

                AnsiConsole.Status().Start($"Executing parallel tournament across [bold]{loadedSandboxes.Count}[/] sandboxes…", _ => { });

                var evaluations = TournamentEngine.evaluateTournament(p.ObjectsDir, p.LedgerPath, fsharpList, cmdOption);

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .AddColumn("Rank")
                    .AddColumn("Sandbox ID")
                    .AddColumn("Status")
                    .AddColumn("Duration")
                    .AddColumn("Files")
                    .AddColumn("Score");

                int rank = 1;
                TournamentEngine.CandidateEvaluation? winner = null;

                foreach (var eval in evaluations)
                {
                    var sbid = ((Domain.SandboxId)eval.SandboxId).Item;
                    var shortId = sbid.Substring(0, Math.Min(8, sbid.Length));
                    var status = eval.Passed ? "[green]PASSED[/]" : $"[red]FAILED (exit {eval.ExitCode})[/]";
                    var duration = $"{eval.DurationMs}ms";
                    var files = eval.FileChanges.ToString();
                    var score = eval.Passed ? $"[bold green]{eval.Score:F1}[/]" : "[dim]0.0[/]";

                    if (rank == 1 && eval.Passed)
                    {
                        winner = eval;
                        table.AddRow($"[bold yellow]★ #{rank}[/]", $"[bold]{shortId}[/]", status, duration, files, score);
                    }
                    else
                    {
                        table.AddRow($"#{rank}", shortId, status, duration, files, score);
                    }
                    rank++;
                }

                AnsiConsole.Write(table);

                if (winner != null)
                {
                    var winId = ((Domain.SandboxId)winner.SandboxId).Item;
                    AnsiConsole.MarkupLine($"\n[green]🏆 Winner:[/] Sandbox [bold]{winId[..8]}[/] with score [bold]{winner.Score:F1}[/]");

                    if (autoCommit)
                    {
                        var winnerSb = loadedSandboxes.First(sb => sb.SandboxId.Equals(winner.SandboxId));
                        var commitMsg = string.IsNullOrWhiteSpace(msg)
                            ? $"Tournament Winner: Sandbox {winId[..8]} (Score {winner.Score:F1})"
                            : msg;

                        var tx = SandboxManager.commit(p.ObjectsDir, p.LedgerPath, winnerSb, author, commitMsg);
                        Repository.writeHead(p, tx.TxId);

                        // Cleanup sandboxes
                        foreach (var sb in loadedSandboxes)
                        {
                            SandboxManager.removeFromRepo(p.TlDir, sb.SandboxId);
                        }

                        var txId = ((Domain.TxId)tx.TxId).Item;
                        AnsiConsole.MarkupLine($"[green]✓[/] Auto-committed winner to HEAD → transaction [bold]{txId[..8]}[/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("\n[red]✗ All candidates failed test verification.[/]");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, sandboxesArg, cmdOpt, autoCommitOpt, msgOpt, authorOpt);

        return cmd;
    }
}
