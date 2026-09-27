// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using Thallium.Cli.Commands;

var rootCmd = new RootCommand("tl — Thallium AI-native version control");

rootCmd.AddCommand(InitCommand.Build());
rootCmd.AddCommand(CommitCommand.Build());
rootCmd.AddCommand(StatusCommand.Build());
rootCmd.AddCommand(LogCommand.Build());
rootCmd.AddCommand(DiffCommand.Build());
rootCmd.AddCommand(CheckoutCommand.Build());
rootCmd.AddCommand(RollbackCommand.Build());
rootCmd.AddCommand(MergeCommand.Build());
rootCmd.AddCommand(SandboxCommand.Build());
rootCmd.AddCommand(GitCommand.Build());
rootCmd.AddCommand(GcCommand.Build());
rootCmd.AddCommand(DashboardCommand.Build());
rootCmd.AddCommand(TestCommand.Build());
rootCmd.AddCommand(ServeCommand.Build());

return await rootCmd.InvokeAsync(args);
