// SPDX-License-Identifier: MPL-2.0
using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class TestCommand {
    public static Command Build() {
        var targetOpt = new Option<string>(["-t", "--target"], () => "HEAD", "Target transaction ID or sandbox ID to test");
        var cmdOpt = new Option<string>(["-c", "--cmd"], () => "", "Custom test command to execute");

        var cmd = new Command("test", "Run test suite in an isolated sandbox overlay without touching working tree") { targetOpt, cmdOpt };

        cmd.SetHandler((target, customCmd) => {
            try {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());

                Domain.SandboxState sb;

               
                var maybeSandbox = SandboxManager.loadFromRepo(p.TlDir, Domain.SandboxId.NewSandboxId(target));
                if (FSharpOption<Domain.SandboxState>.get_IsSome(maybeSandbox)) {
                    sb = maybeSandbox.Value;
                    AnsiConsole.MarkupLine($"[dim]Testing active sandbox {target[..8]}…[/]");
                }
                else {
                   
                    Domain.TxId txId;
                    if (target == "HEAD") {
                        var h = Repository.readHead(p);
                        if (FSharpOption<Domain.TxId>.get_IsNone(h)) {
                            AnsiConsole.MarkupLine("[red]✗ No HEAD transaction found.[/]");
                            Environment.Exit(1);
                            return;
                        }
                        txId = h.Value;
                    }
                    else {
                        var all = Ledger.readAll(p.LedgerPath);
                        var tx = all.FirstOrDefault(t => ((Domain.TxId)t.TxId).Item.StartsWith(target, StringComparison.OrdinalIgnoreCase));
                        if (tx == null) {
                            AnsiConsole.MarkupLine($"[red]✗ Transaction or sandbox '{target}' not found.[/]");
                            Environment.Exit(1);
                            return;
                        }
                        txId = tx.TxId;
                    }

                    sb = SandboxManager.create(txId, TimeSpan.FromMinutes(2.0));
                }

                var cmdOption = string.IsNullOrWhiteSpace(customCmd)
                    ? FSharpOption<string>.None
                    : FSharpOption<string>.Some(customCmd);

                AnsiConsole.Status().Start("Executing test suite in isolated COW sandbox…", _ => { });

                var res = SandboxRunner.runTestInSandbox(p.ObjectsDir, p.LedgerPath, sb, cmdOption);

                if (res.Passed) {
                    AnsiConsole.MarkupLine($"[green]✓ Test suite passed[/] in [bold]{res.DurationMs}ms[/]");
                    AnsiConsole.MarkupLine($"  Command: [dim]{res.CommandRun}[/]");
                    if (!string.IsNullOrWhiteSpace(res.StandardOut)) {
                        AnsiConsole.WriteLine(res.StandardOut.Trim());
                    }
                }
                else {
                    AnsiConsole.MarkupLine($"[red]✗ Test suite failed[/] with exit code [bold]{res.ExitCode}[/]");
                    AnsiConsole.MarkupLine($"  Command: [dim]{res.CommandRun}[/]");
                    if (!string.IsNullOrWhiteSpace(res.StandardErr)) {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(res.StandardErr.Trim())}[/]");
                    }
                    else if (!string.IsNullOrWhiteSpace(res.StandardOut)) {
                        AnsiConsole.WriteLine(res.StandardOut.Trim());
                    }
                    Environment.Exit(res.ExitCode);
                }
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, targetOpt, cmdOpt);

        return cmd;
    }
}
