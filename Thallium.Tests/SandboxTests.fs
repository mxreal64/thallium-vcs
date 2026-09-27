// SPDX-License-Identifier: MPL-2.0

module Thallium.Tests.SandboxTests

open System
open System.IO
open System.Text
open Xunit
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager


let private makeGenesis objectsDir ledgerPath =
    let tx =
        { TxId        = TxId (Guid.NewGuid().ToString("N"))
          ParentId    = None
          Timestamp   = DateTimeOffset.UtcNow
          Author      = "test"
          Summary     = "genesis"
          Changes     = []
          FromSandbox = None }
    append ledgerPath tx
    tx

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally Directory.Delete(dir, true)


[<Fact>]
let ``sandbox write does not affect the base transaction tree`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let genesis = makeGenesis objDir ledger
        let sb = create genesis.TxId (TimeSpan.FromMinutes 5.0)

       
        let content = Encoding.UTF8.GetBytes "sandbox-only content"
        let sb2 = writeFile objDir sb "src/new_file.fs" content

       
        let baseTree = resolveTree ledger genesis.TxId
        Assert.False(Map.containsKey "src/new_file.fs" baseTree)

       
        let read = readFile objDir ledger sb2 "src/new_file.fs"
        Assert.True(read.IsSome)
        Assert.Equal<byte[]>(content, read.Value))

[<Fact>]
let ``sandbox read falls through to base tree for unmodified files`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

       
        let content = Encoding.UTF8.GetBytes "base file content"
        let bid = put objDir content
        let fc : FileChange = { Path = "src/base.fs"; OldBlob = None; NewBlob = Some bid; Intent = Add; Annotation = None }
        let genesis =
            { TxId = TxId (Guid.NewGuid().ToString("N")); ParentId = None
              Timestamp = DateTimeOffset.UtcNow; Author = "test"; Summary = "genesis"
              Changes = [fc]; FromSandbox = None }
        append ledger genesis

        let sb = create genesis.TxId (TimeSpan.FromMinutes 5.0)

       
        let read = readFile objDir ledger sb "src/base.fs"
        Assert.True(read.IsSome)
        Assert.Equal<byte[]>(content, read.Value))

[<Fact>]
let ``sandbox delete marks file as absent in overlay`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let content = Encoding.UTF8.GetBytes "will be deleted"
        let bid = put objDir content
        let fc : FileChange = { Path = "src/del.fs"; OldBlob = None; NewBlob = Some bid; Intent = Add; Annotation = None }
        let genesis =
            { TxId = TxId (Guid.NewGuid().ToString("N")); ParentId = None
              Timestamp = DateTimeOffset.UtcNow; Author = "test"; Summary = "genesis"
              Changes = [fc]; FromSandbox = None }
        append ledger genesis

        let sb  = create genesis.TxId (TimeSpan.FromMinutes 5.0)
        let sb2 = deleteFile sb "src/del.fs"

        let read = readFile objDir ledger sb2 "src/del.fs"
        Assert.True(read.IsNone))

[<Fact>]
let ``sandbox commit appends a new transaction to the ledger`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let genesis = makeGenesis objDir ledger
        let sb  = create genesis.TxId (TimeSpan.FromMinutes 5.0)
        let content = Encoding.UTF8.GetBytes "new feature"
        let sb2 = writeFile objDir sb "feature.fs" content
        let tx  = commit objDir ledger sb2 "agent:test" "add feature"

        let all = readAll ledger
        Assert.Equal(2, all.Length)  
        Assert.Equal(tx.TxId, all.[1].TxId)
        Assert.Equal(Some genesis.TxId, tx.ParentId))

[<Fact>]
let ``expired sandbox returns None from tryGet`` () =
    let genesis = TxId (Guid.NewGuid().ToString("N"))
    let sb = create genesis (TimeSpan.FromMilliseconds(-1.0)) 
    let found = tryGet sb.SandboxId
    Assert.True(found.IsNone)
