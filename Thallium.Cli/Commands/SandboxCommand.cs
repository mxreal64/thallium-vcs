// SPDX-License-Identifier: MPL-2.0
using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class SandboxCommand {
    public static Command Build() {
        var cmd = new Command("sandbox", "Manage ephemeral AI micro-sandboxes");
        cmd.AddCommand(CreateSubCmd());
        cmd.AddCommand(ListSubCmd());
        cmd.AddCommand(WriteSubCmd());
        cmd.AddCommand(CommitSubCmd());
        cmd.AddCommand(DropSubCmd());
        return cmd;
    }

   

    static Command CreateSubCmd() {
        var baseOpt    = new Option<string>("--base", () => "HEAD", "Base transaction ID or HEAD");
        var minutesOpt = new Option<int>("--minutes", () => 10, "Sandbox lifetime in minutes");
        var sub        = new Command("create", "Spin up a new isolated sandbox") { baseOpt, minutesOpt };

        sub.SetHandler((baseId, minutes) => {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());

            Domain.TxId baseTxId;
            if (baseId == "HEAD") {
                var h = Repository.readHead(p);
                if (FSharpOption<Domain.TxId>.get_IsNone(h))
                { AnsiConsole.MarkupLine("[red]✗ No HEAD yet.[/]"); Environment.Exit(1); return; }
                baseTxId = h.Value;
            }
            else {
                var tx = Ledger.readAll(p.LedgerPath)
                               .FirstOrDefault(t => ((Domain.TxId)t.TxId).Item.StartsWith(baseId));
                if (tx == null)
                { AnsiConsole.MarkupLine($"[red]✗ Transaction '{baseId}' not found.[/]"); Environment.Exit(1); return; }
                baseTxId = tx.TxId;
            }

            var sb   = SandboxManager.create(baseTxId, TimeSpan.FromMinutes(minutes));
            SandboxManager.saveToRepo(p.TlDir, sb);
            var sbId = ((Domain.SandboxId)sb.SandboxId).Item;

            AnsiConsole.MarkupLine($"[green]✓[/] Sandbox created");
            AnsiConsole.MarkupLine($"  ID:      [bold]{sbId}[/]");
            AnsiConsole.MarkupLine($"  Base:    [dim]{((Domain.TxId)sb.BaseTxId).Item[..8]}[/]");
            AnsiConsole.MarkupLine($"  Expires: [dim]{sb.ExpiresAt.ToLocalTime():HH:mm:ss}[/]");
        }, baseOpt, minutesOpt);

        return sub;
    }

   

    static Command ListSubCmd() {
        var sub = new Command("list", "List all live sandboxes");
        sub.SetHandler(() => {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            var sandboxes = SandboxManager.listFromRepo(p.TlDir);
            if (sandboxes.Length == 0)
            { AnsiConsole.MarkupLine("[dim]No active sandboxes.[/]"); return; }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Sandbox ID")
                .AddColumn("Base Tx")
                .AddColumn("Overlay Files")
                .AddColumn("Expires");

            foreach (var sb in sandboxes) {
                var sbId = ((Domain.SandboxId)sb.SandboxId).Item;
                var base_ = ((Domain.TxId)sb.BaseTxId).Item[..8];
                var overlayCount = sb.Overlay.Count;
                var exp = sb.ExpiresAt.ToLocalTime().ToString("HH:mm:ss");
                table.AddRow(sbId[..8], base_, overlayCount.ToString(), exp);
            }
            AnsiConsole.Write(table);
        });
        return sub;
    }

   

    static Command WriteSubCmd() {
        var idOpt      = new Option<string>("--id", "Sandbox ID") { IsRequired = true };
        var pathOpt    = new Option<string>("--path", "File path within the sandbox") { IsRequired = true };
        var contentOpt = new Option<string>("--content", () => "", "Content to write (or omit to read from stdin)");

        var sub = new Command("write", "Write a file into a sandbox overlay") { idOpt, pathOpt, contentOpt };
        sub.SetHandler((id, path, content) => {
            var p  = Repository.requireRoot(Directory.GetCurrentDirectory());
            if (string.IsNullOrEmpty(content))
                content = Console.In.ReadToEnd();

            var sbId = Domain.SandboxId.NewSandboxId(id);
            var sb   = SandboxManager.loadFromRepo(p.TlDir, sbId);
            if (FSharpOption<Domain.SandboxState>.get_IsNone(sb))
            { AnsiConsole.MarkupLine("[red]✗ Sandbox not found or expired.[/]"); Environment.Exit(1); return; }

            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            var updated = SandboxManager.writeFile(p.ObjectsDir, sb.Value, path, bytes);
            SandboxManager.saveToRepo(p.TlDir, updated);
            AnsiConsole.MarkupLine($"[green]✓[/] Wrote {bytes.Length} bytes to [bold]{path}[/] in sandbox [dim]{id[..8]}[/]");
        }, idOpt, pathOpt, contentOpt);

        return sub;
    }

   

    static Command CommitSubCmd() {
        var idOpt     = new Option<string>("--id", "Sandbox ID") { IsRequired = true };
        var msgOpt    = new Option<string>(["-m", "--message"], () => "", "Commit message");
        var authorOpt = new Option<string>("--author", () => Environment.UserName);

        var sub = new Command("commit", "Commit sandbox changes as a real transaction") { idOpt, msgOpt, authorOpt };
        sub.SetHandler((id, msg, author) => {
            var p    = Repository.requireRoot(Directory.GetCurrentDirectory());
            var sbId = Domain.SandboxId.NewSandboxId(id);
            var sb   = SandboxManager.loadFromRepo(p.TlDir, sbId);
            if (FSharpOption<Domain.SandboxState>.get_IsNone(sb))
            { AnsiConsole.MarkupLine("[red]✗ Sandbox not found or expired.[/]"); Environment.Exit(1); return; }

            var tx   = SandboxManager.commit(p.ObjectsDir, p.LedgerPath, sb.Value, author, msg);
            Repository.writeHead(p, tx.TxId);
            SandboxManager.removeFromRepo(p.TlDir, sbId);
            var txId = ((Domain.TxId)tx.TxId).Item;
            AnsiConsole.MarkupLine($"[green]✓[/] Sandbox committed → transaction [bold]{txId[..8]}[/]");
            AnsiConsole.MarkupLine($"  [italic]{tx.Summary}[/]");
        }, idOpt, msgOpt, authorOpt);

        return sub;
    }

   

    static Command DropSubCmd() {
        var idOpt = new Option<string>("--id", "Sandbox ID") { IsRequired = true };
        var sub   = new Command("drop", "Discard a sandbox") { idOpt };
        sub.SetHandler(id => {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            SandboxManager.removeFromRepo(p.TlDir, Domain.SandboxId.NewSandboxId(id));
            AnsiConsole.MarkupLine($"[green]✓[/] Sandbox [dim]{id[..8]}[/] dropped.");
        }, idOpt);
        return sub;
    }
}
