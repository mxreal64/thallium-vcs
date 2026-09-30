
module Thallium.Tests.GarbageCollectorTests

open System
open System.IO
open System.Text
open Xunit
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.Repository
open Thallium.Core.GarbageCollector

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), "tl_gc_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``collect prunes orphaned blobs not in any transaction or sandbox`` () =
    withTempDir (fun dir ->
        let p = init dir "tester"
        let repoPaths = paths dir

       
        File.WriteAllText(Path.Combine(dir, "live.txt"), "I am live")
        stageAll repoPaths
        let _ = commit repoPaths "tester" "Add live"

       
        let orphanBytes = Encoding.UTF8.GetBytes("I am an unreferenced orphan blob")
        let orphanBid = put repoPaths.ObjectsDir orphanBytes
        Assert.True(exists repoPaths.ObjectsDir orphanBid)

       
        let result = collect repoPaths.ObjectsDir repoPaths.LedgerPath repoPaths.TlDir

        Assert.Equal(1, result.PrunedBlobsCount)
        Assert.False(exists repoPaths.ObjectsDir orphanBid)
        Assert.True(result.BytesReclaimed > 0L))
