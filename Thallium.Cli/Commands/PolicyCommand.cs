using System.CommandLine;
using Spectre.Console;
using Thallium.Core;
using Microsoft.FSharp.Core;

namespace Thallium.Cli.Commands;

public static class PolicyCommand
{
    public static Command Build()
    {
        var cmd = new Command("policy", "Manage security policies and agent access controls");
        cmd.AddCommand(InitSubCmd());
        cmd.AddCommand(ShowSubCmd());
        cmd.AddCommand(CheckSubCmd());
        return cmd;
    }

    static Command InitSubCmd()
    {
        var sub = new Command("init", "Initialize a default .tl/policy.json file");
        sub.SetHandler(() =>
        {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            PolicyEngine.savePolicy(p.TlDir, PolicyEngine.defaultPolicy);
            AnsiConsole.MarkupLine("[green]✓[/] Created default security policy at [bold].tl/policy.json[/]");
        });
        return sub;
    }

    static Command ShowSubCmd()
    {
        var sub = new Command("show", "Display active repository policy rules");
        sub.SetHandler(() =>
        {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            var pol = PolicyEngine.loadPolicy(p.TlDir);
            if (FSharpOption<PolicyEngine.RepoPolicy>.get_IsNone(pol))
            {
                AnsiConsole.MarkupLine("[dim]No active policy found. Run [bold]tl policy init[/] to create one.[/]");
                return;
            }

            var policy = pol.Value;
            var table = new Table().Border(TableBorder.Rounded).AddColumn("Policy Rule").AddColumn("Configuration");

            table.AddRow("Protected Paths (Immutable)", string.Join(", ", policy.ProtectedPaths));
            table.AddRow("Human Required Paths", string.Join(", ", policy.RequireHumanForPaths));
            table.AddRow("Disallow Agent Deletions", policy.DisallowAgentDeletes ? "[green]True[/]" : "[yellow]False[/]");
            table.AddRow("Max Files Per Commit", FSharpOption<int>.get_IsSome(policy.MaxFilesPerCommit) ? policy.MaxFilesPerCommit.Value.ToString() : "Unlimited");

            AnsiConsole.Write(table);
        });
        return sub;
    }

    static Command CheckSubCmd()
    {
        var authorOpt = new Option<string>("--author", () => Environment.UserName, "Author identity to validate against");
        var sub = new Command("check", "Validate current working tree against active policy") { authorOpt };

        sub.SetHandler(author =>
        {
            var p = Repository.requireRoot(Directory.GetCurrentDirectory());
            var pol = PolicyEngine.loadPolicy(p.TlDir);
            if (FSharpOption<PolicyEngine.RepoPolicy>.get_IsNone(pol))
            {
                AnsiConsole.MarkupLine("[green]✓[/] No active policy rules to violate.");
                return;
            }

            var statusItems = Repository.status(p);
            var violations = new List<string>();

            foreach (var (path, statusVal) in statusItems)
            {
                if (PolicyEngine.isPathMatching(pol.Value.ProtectedPaths, path))
                {
                    violations.Add($"Protected path modified: '{path}'");
                }
                if (author.StartsWith("agent:", StringComparison.OrdinalIgnoreCase) &&
                    PolicyEngine.isPathMatching(pol.Value.RequireHumanForPaths, path))
                {
                    violations.Add($"Agent '{author}' cannot modify human-required path: '{path}'");
                }
            }

            if (violations.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]✓ All working tree changes comply with repository policy.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]✗ Policy check failed with {violations.Count} violation(s):[/]");
                foreach (var v in violations)
                {
                    AnsiConsole.MarkupLine($"  • [red]{Markup.Escape(v)}[/]");
                }
                Environment.Exit(1);
            }
        }, authorOpt);

        return sub;
    }
}
