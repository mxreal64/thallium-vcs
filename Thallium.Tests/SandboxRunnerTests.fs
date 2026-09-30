
module Thallium.Tests.SandboxRunnerTests

open System
open System.IO
open System.Text
open Xunit
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager
open Thallium.Core.SandboxRunner

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), "tl_runner_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``runTestInSandbox executes custom command and returns exit code and output`` () =
    withTempDir (fun dir ->
        let objDir = Path.Combine(dir, "objects")
        let ledger = Path.Combine(dir, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let genesis : Transaction =
            { TxId = TxId (Guid.NewGuid().ToString("N"))
              ParentId = None
              Timestamp = DateTimeOffset.UtcNow
              Author = "tester"
              Summary = "genesis"
              Changes = []
              FromSandbox = None; AgentData = None }
        append ledger genesis

        let sb = create genesis.TxId (TimeSpan.FromMinutes 5.0)
        let sbWithFile = writeFile objDir sb "test.sh" (Encoding.UTF8.GetBytes("echo 'Sandbox Test OK'"))

        let res = runTestInSandbox objDir ledger sbWithFile (Some "bash test.sh")

        Assert.True(res.Passed)
        Assert.Equal(0, res.ExitCode)
        Assert.Contains("Sandbox Test OK", res.StandardOut))

[<Fact>]
let ``runTestInSandbox captures failure exit code`` () =
    withTempDir (fun dir ->
        let objDir = Path.Combine(dir, "objects")
        let ledger = Path.Combine(dir, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let genesis : Transaction =
            { TxId = TxId (Guid.NewGuid().ToString("N"))
              ParentId = None
              Timestamp = DateTimeOffset.UtcNow
              Author = "tester"
              Summary = "genesis"
              Changes = []
              FromSandbox = None; AgentData = None }
        append ledger genesis

        let sb = create genesis.TxId (TimeSpan.FromMinutes 5.0)
        let res = runTestInSandbox objDir ledger sb (Some "exit 42")

        Assert.False(res.Passed)
        Assert.Equal(42, res.ExitCode))
