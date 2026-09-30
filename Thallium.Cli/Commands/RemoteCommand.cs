using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class RemoteCommand
{
    public static Command BuildRemoteCmd()
    {
        var cmd = new Command("remote", "Manage configured remote repositories");
        cmd.AddCommand(AddSubCmd());
        cmd.AddCommand(ListSubCmd());
        cmd.AddCommand(RemoveSubCmd());
        return cmd;
    }

    static Command AddSubCmd()
    {
        var nameArg = new Argument<string>("name", "Remote name (e.g. origin)");
        var urlArg  = new Argument<string>("url", "Remote URL or directory path");
        var sub = new Command("add", "Add a new remote destination") { nameArg, urlArg };

        sub.SetHandler((name, url) =>
        {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            RemoteSync.addRemote(p.TlDir, name, url);
            AnsiConsole.MarkupLine($"[green]✓[/] Added remote [bold]{name}[/] -> [dim]{url}[/]");
        }, nameArg, urlArg);

        return sub;
    }

    static Command ListSubCmd()
    {
        var sub = new Command("list", "List configured remote repositories");
        sub.SetHandler(() =>
        {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            var remotes = RemoteSync.listRemotes(p.TlDir);
            if (remotes.Length == 0)
            {
                AnsiConsole.MarkupLine("[dim]No remotes configured.[/]");
                return;
            }

            var table = new Table().Border(TableBorder.Rounded).AddColumn("Remote Name").AddColumn("Destination URL");
            foreach (var r in remotes)
            {
                table.AddRow($"[bold]{r.Name}[/]", r.Url);
            }
            AnsiConsole.Write(table);
        });
        return sub;
    }

    static Command RemoveSubCmd()
    {
        var nameArg = new Argument<string>("name", "Remote name to remove");
        var sub = new Command("remove", "Remove a configured remote") { nameArg };
        sub.SetHandler(name =>
        {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            RemoteSync.removeRemote(p.TlDir, name);
            AnsiConsole.MarkupLine($"[green]✓[/] Removed remote [bold]{name}[/]");
        }, nameArg);
        return sub;
    }

    public static Command BuildPushCmd()
    {
        var remoteArg = new Argument<string>("remote", () => "origin", "Target remote name or destination directory");
        var cmd = new Command("push", "Push transactions and content-addressed blobs to remote repository") { remoteArg };

        cmd.SetHandler(remote =>
        {
            try
            {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());
                var remotes = RemoteSync.listRemotes(p.TlDir);
                var resolvedUrl = remotes.FirstOrDefault(r => r.Name == remote)?.Url ?? remote;

                AnsiConsole.Status().Start($"Pushing to [bold]{resolvedUrl}[/]…", _ => { });

                var res = RemoteSync.push(p, resolvedUrl);
                if (res.IsOk)
                {
                    var (txs, blobs) = res.ResultValue;
                    AnsiConsole.MarkupLine($"[green]✓ Push complete:[/] Transferred [bold]{txs}[/] transaction(s) and [bold]{blobs}[/] blob(s) to [dim]{resolvedUrl}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]✗ Push failed:[/] {res.ErrorValue}");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, remoteArg);

        return cmd;
    }

    public static Command BuildPullCmd()
    {
        var remoteArg = new Argument<string>("remote", () => "origin", "Source remote name or directory");
        var cmd = new Command("pull", "Fetch and fast-forward transactions from remote repository") { remoteArg };

        cmd.SetHandler(remote =>
        {
            try
            {
                var p = Repository.requireRoot(Directory.GetCurrentDirectory());
                var remotes = RemoteSync.listRemotes(p.TlDir);
                var resolvedUrl = remotes.FirstOrDefault(r => r.Name == remote)?.Url ?? remote;

                AnsiConsole.Status().Start($"Pulling from [bold]{resolvedUrl}[/]…", _ => { });

                var res = RemoteSync.pull(p, resolvedUrl);
                if (res.IsOk)
                {
                    var (txs, blobs) = res.ResultValue;
                    AnsiConsole.MarkupLine($"[green]✓ Pull complete:[/] Fetched [bold]{txs}[/] transaction(s) and [bold]{blobs}[/] blob(s) from [dim]{resolvedUrl}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]✗ Pull failed:[/] {res.ErrorValue}");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, remoteArg);

        return cmd;
    }
}
