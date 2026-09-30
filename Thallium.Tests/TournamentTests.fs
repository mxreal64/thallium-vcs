/// Tests for TournamentEngine.
module Thallium.Tests.TournamentTests

open System
open System.IO
open System.Text
open Xunit
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager
open Thallium.Core.TournamentEngine

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), "tl_tournament_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``evaluateTournament ranks passing candidate higher than failing candidate`` () =
    withTempDir (fun dir ->
        let objDir = Path.Combine(dir, "objects")
        let ledger = Path.Combine(dir, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let genesis : Transaction =
            { TxId        = TxId (Guid.NewGuid().ToString("N"))
              ParentId    = None
              Timestamp   = DateTimeOffset.UtcNow
              Author      = "tester"
              Summary     = "genesis"
              Changes     = []
              FromSandbox = None
              AgentData   = None }
        append ledger genesis

        // Candidate 1: Passing code
        let sb1 = create genesis.TxId (TimeSpan.FromMinutes 5.0)
        let sb1Ready = writeFile objDir sb1 "test.sh" (Encoding.UTF8.GetBytes("echo 'Candidate 1 OK'; exit 0"))

        // Candidate 2: Failing code
        let sb2 = create genesis.TxId (TimeSpan.FromMinutes 5.0)
        let sb2Ready = writeFile objDir sb2 "test.sh" (Encoding.UTF8.GetBytes("echo 'Candidate 2 Broken'; exit 1"))

        let ranked = evaluateTournament objDir ledger [sb1Ready; sb2Ready] (Some "bash test.sh")

        Assert.Equal(2, ranked.Length)
        let top = ranked.[0]
        let bottom = ranked.[1]

        Assert.True(top.Passed)
        Assert.Equal(sb1.SandboxId, top.SandboxId)
        Assert.True(top.Score > 0.0)

        Assert.False(bottom.Passed)
        Assert.Equal(sb2.SandboxId, bottom.SandboxId)
        Assert.Equal(0.0, bottom.Score))
