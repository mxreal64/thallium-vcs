using System.CommandLine;
using Spectre.Console;
using System.Diagnostics;

namespace Thallium.Cli.Commands;

public static class ServeCommand {
    public static Command Build() {
        var portOpt = new Option<int>("--port", () => 7373, "Port to listen on");
        var repoOpt = new Option<string>("--repo", () => Directory.GetCurrentDirectory(), "Repository path");

        var cmd = new Command("serve", "Start the JSON-RPC AI interface server") { portOpt, repoOpt };
        cmd.SetHandler((port, repo) => {
           
            var exeDir   = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var rpcExe   = Path.Combine(exeDir, "tl-rpc");
            var rpcExeWin = Path.Combine(exeDir, "tl-rpc.exe");

            string? rpcPath = File.Exists(rpcExe) ? rpcExe
                            : File.Exists(rpcExeWin) ? rpcExeWin
                            : null;

            if (rpcPath != null) {
                AnsiConsole.MarkupLine($"[green]↗[/]  Launching RPC server on port [bold]{port}[/]…");
                AnsiConsole.MarkupLine($"  Endpoint: [link]http://localhost:{port}/rpc[/]");
                AnsiConsole.MarkupLine($"  Repo:     {repo}");
                AnsiConsole.MarkupLine("[dim]Press Ctrl+C to stop.[/]");

                var psi = new ProcessStartInfo(rpcPath, $"--port {port} --repo \"{repo}\"") {
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            else {
               
                AnsiConsole.MarkupLine("[yellow]tl-rpc binary not found next to tl.[/]");
                AnsiConsole.MarkupLine($"Run manually: [bold]dotnet run --project Thallium.JsonRpc -- --port {port} --repo \"{repo}\"[/]");
            }
        }, portOpt, repoOpt);

        return cmd;
    }
}
