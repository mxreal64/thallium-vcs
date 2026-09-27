// SPDX-License-Identifier: MPL-2.0
using System.CommandLine;
using Spectre.Console;
using Thallium.Core;

namespace Thallium.Cli.Commands;

public static class DashboardCommand {
    public static Command Build() {
        var portOpt = new Option<int>("--port", () => 7374, "Port to host the dashboard on");
        var repoOpt = new Option<string>("--repo", () => Directory.GetCurrentDirectory(), "Target repository");

        var cmd = new Command("dashboard", "Launch the Continuous Timeline Web Dashboard") { portOpt, repoOpt };
        cmd.AddAlias("ui");

        cmd.SetHandler((port, repo) => {
            try {
                var fullRepo = Path.GetFullPath(repo);
                var p = Repository.requireRoot(fullRepo);

                AnsiConsole.MarkupLine($"[green]↗[/] Launching Continuous Timeline Dashboard on [link]http://localhost:{port}/[/]");
                AnsiConsole.MarkupLine($"  Repository: [bold]{fullRepo}[/]");
                AnsiConsole.MarkupLine("[dim]Press Ctrl+C to terminate dashboard.[/]");

                DashboardServer.startDashboard(fullRepo, port);

               
                Thread.Sleep(Timeout.Infinite);
            }
            catch (Exception ex) {
                AnsiConsole.MarkupLine($"[red]✗ {ex.Message}[/]");
                Environment.Exit(1);
            }
        }, portOpt, repoOpt);

        return cmd;
    }
}
