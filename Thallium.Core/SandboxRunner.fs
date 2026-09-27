// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.SandboxRunner

open System
open System.IO
open System.Diagnostics
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager

type TestRunResult =
    { Passed       : bool
      ExitCode     : int
      StandardOut  : string
      StandardErr  : string
      DurationMs   : int64
      CommandRun   : string }

let detectTestCommand (workingDir: string) : string =
    if Directory.EnumerateFiles(workingDir, "*.fsproj").GetEnumerator().MoveNext() ||
       Directory.EnumerateFiles(workingDir, "*.csproj").GetEnumerator().MoveNext() ||
       Directory.EnumerateFiles(workingDir, "*.sln").GetEnumerator().MoveNext() ||
       Directory.EnumerateFiles(workingDir, "*.slnx").GetEnumerator().MoveNext() then
        "dotnet test"
    elif File.Exists(Path.Combine(workingDir, "package.json")) then
        "npm test"
    elif File.Exists(Path.Combine(workingDir, "Cargo.toml")) then
        "cargo test"
    elif File.Exists(Path.Combine(workingDir, "pytest.ini")) ||
         File.Exists(Path.Combine(workingDir, "setup.py")) ||
         File.Exists(Path.Combine(workingDir, "pyproject.toml")) then
        "pytest"
    else
        "echo 'No test suite detected - syntax check only'"

let runTestInSandbox
    (objectsDir : string)
    (ledgerPath : string)
    (sb         : SandboxState)
    (testCmdOpt : string option)
    : TestRunResult =

    let tempDir = Path.Combine(Path.GetTempPath(), "tl_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tempDir) |> ignore

    try
       
        materialise objectsDir ledgerPath sb tempDir

        let testCmd =
            match testCmdOpt with
            | Some cmd when cmd.Trim() <> "" -> cmd
            | _ -> detectTestCommand tempDir

        let sw = Stopwatch.StartNew()
        let psi = ProcessStartInfo()
        psi.FileName <- "bash"
        psi.Arguments <- $"-c \"{testCmd}\""
        psi.WorkingDirectory <- tempDir
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true

        use proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit(60000) |> ignore

        sw.Stop()
        { Passed      = proc.ExitCode = 0
          ExitCode    = proc.ExitCode
          StandardOut = stdout
          StandardErr = stderr
          DurationMs  = sw.ElapsedMilliseconds
          CommandRun  = testCmd }
    finally
        try
            if Directory.Exists tempDir then
                Directory.Delete(tempDir, true)
        with _ -> ()
