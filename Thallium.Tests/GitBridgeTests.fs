
module Thallium.Tests.GitBridgeTests

open System
open System.IO
open System.Diagnostics
open Xunit
open Thallium.Core.GitBridge
open Thallium.Core.Ledger
open Thallium.Core.Repository

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), "tl_git_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

let private runCmd (dir: string) (cmd: string) (args: string) : int =
    let psi = ProcessStartInfo(cmd, args)
    psi.WorkingDirectory <- dir
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    use proc = Process.Start(psi)
    proc.WaitForExit()
    proc.ExitCode

[<Fact>]
let ``importGitRepo creates Thallium transactions from Git commits`` () =
    withTempDir (fun root ->
        let gitDir = Path.Combine(root, "git_repo")
        let tlDir  = Path.Combine(root, "tl_repo")
        Directory.CreateDirectory gitDir |> ignore
        Directory.CreateDirectory tlDir  |> ignore

       
        let _ = runCmd gitDir "git" "init"
        let _ = runCmd gitDir "git" "config user.name \"Alice\""
        let _ = runCmd gitDir "git" "config user.email \"alice@example.com\""

        File.WriteAllText(Path.Combine(gitDir, "hello.txt"), "Hello World")
        let _ = runCmd gitDir "git" "add -A"
        let _ = runCmd gitDir "git" "commit -m \"First commit\""

        File.WriteAllText(Path.Combine(gitDir, "app.fs"), "let main = 1")
        let _ = runCmd gitDir "git" "add -A"
        let _ = runCmd gitDir "git" "commit -m \"Add app.fs\""

       
        match importGitRepo gitDir tlDir with
        | Error err -> Assert.Fail($"Import failed: {err}")
        | Ok count ->
            Assert.Equal(2, count)

            let p = paths tlDir
            let txs = readAll p.LedgerPath
            Assert.True(txs.Length >= 2)
            Assert.Contains(txs, (fun t -> t.Summary = "Add app.fs"))
            Assert.Contains(txs, (fun t -> t.Summary = "First commit")))

[<Fact>]
let ``exportToGit exports Thallium ledger into valid Git repository`` () =
    withTempDir (fun root ->
        let tlDir  = Path.Combine(root, "tl_repo")
        let gitDir = Path.Combine(root, "git_export")
        Directory.CreateDirectory tlDir |> ignore

        let _ = init tlDir "Exporter"
        File.WriteAllText(Path.Combine(tlDir, "calc.fs"), "let add x y = x + y")
        stageAll (paths tlDir)
        let _ = commit (paths tlDir) "bob" "Implement add"

        match exportToGit tlDir gitDir with
        | Error err -> Assert.Fail($"Export failed: {err}")
        | Ok count ->
            Assert.True(count >= 1)
            Assert.True(Directory.Exists(Path.Combine(gitDir, ".git")))
            Assert.True(File.Exists(Path.Combine(gitDir, "calc.fs"))))
