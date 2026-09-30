
module Thallium.Tests.LedgerTests

open System
open System.IO
open Xunit
open Thallium.Core.Domain
open Thallium.Core.Ledger


let private makeTx (parentId: TxId option) author summary : Transaction =
    { TxId        = TxId (Guid.NewGuid().ToString("N"))
      ParentId    = parentId
      Timestamp   = DateTimeOffset.UtcNow
      Author      = author
      Summary     = summary
      Changes     = []
      FromSandbox = None; AgentData = None }

let private withTempFile f =
    let path = Path.GetTempFileName()
    try
        File.Delete path  
        f path
    finally
        if File.Exists path then File.Delete path


[<Fact>]
let ``append and readAll round-trips a single transaction`` () =
    withTempFile (fun path ->
        let tx = makeTx None "alice" "genesis"
        append path tx
        let result = readAll path
        Assert.Single result |> ignore
        Assert.Equal(tx.TxId, result.[0].TxId)
        Assert.Equal("genesis", result.[0].Summary))

[<Fact>]
let ``readAll preserves append order`` () =
    withTempFile (fun path ->
        let tx1 = makeTx None "alice" "first"
        let tx2 = makeTx (Some tx1.TxId) "bob" "second"
        let tx3 = makeTx (Some tx2.TxId) "carol" "third"
        append path tx1
        append path tx2
        append path tx3
        let results = readAll path
        Assert.Equal(3, results.Length)
        Assert.Equal("first",  results.[0].Summary)
        Assert.Equal("second", results.[1].Summary)
        Assert.Equal("third",  results.[2].Summary))

[<Fact>]
let ``tip returns the last appended transaction`` () =
    withTempFile (fun path ->
        let tx1 = makeTx None "alice" "first"
        let tx2 = makeTx (Some tx1.TxId) "bob" "second"
        append path tx1
        append path tx2
        let t = tip path
        Assert.True(t.IsSome)
        Assert.Equal("second", t.Value.Summary))

[<Fact>]
let ``tip on empty ledger returns None`` () =
    let path = Path.GetTempFileName()
    File.Delete path
    try
        let t = tip path
        Assert.True(t.IsNone)
    finally
        if File.Exists path then File.Delete path

[<Fact>]
let ``findById returns correct transaction`` () =
    withTempFile (fun path ->
        let tx1 = makeTx None "alice" "one"
        let tx2 = makeTx (Some tx1.TxId) "bob" "two"
        append path tx1
        append path tx2
        let found = findById path tx1.TxId
        Assert.True(found.IsSome)
        Assert.Equal("one", found.Value.Summary))

[<Fact>]
let ``walkBack traverses ancestor chain newest-first`` () =
    withTempFile (fun path ->
        let tx1 = makeTx None "alice" "A"
        let tx2 = makeTx (Some tx1.TxId) "bob" "B"
        let tx3 = makeTx (Some tx2.TxId) "carol" "C"
        append path tx1
        append path tx2
        append path tx3
        let chain = walkBack path tx3.TxId
       
        Assert.Equal(3, chain.Length)
        let summaries = chain |> List.map (fun t -> t.Summary)
        Assert.Contains("A", summaries)
        Assert.Contains("B", summaries)
        Assert.Contains("C", summaries))

[<Fact>]
let ``resolveTree reflects add and delete operations`` () =
    withTempFile (fun path ->
        let blobId = BlobId "abc123"
        let addChange : FileChange =
            { Path = "src/foo.fs"; OldBlob = None; NewBlob = Some blobId; Intent = Add; Annotation = None }
        let tx1 = { makeTx None "alice" "add file" with Changes = [addChange] }

        let delChange : FileChange =
            { Path = "src/foo.fs"; OldBlob = Some blobId; NewBlob = None; Intent = Remove; Annotation = None }
        let tx2 = { makeTx (Some tx1.TxId) "bob" "delete file" with Changes = [delChange] }

        append path tx1
        append path tx2

        let tree1 = resolveTree path tx1.TxId
        Assert.True(Map.containsKey "src/foo.fs" tree1)

        let tree2 = resolveTree path tx2.TxId
        Assert.False(Map.containsKey "src/foo.fs" tree2))
