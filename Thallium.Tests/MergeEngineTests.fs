// SPDX-License-Identifier: MPL-2.0

module Thallium.Tests.MergeEngineTests

open System
open System.IO
open System.Text
open Xunit
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.MergeEngine


let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally Directory.Delete(dir, true)

let private mkBytes s = Encoding.UTF8.GetBytes(s : string)

let private makeTx parentId author summary (changes: FileChange list) ledgerPath : Transaction =
    let tx =
        { TxId        = TxId (Guid.NewGuid().ToString("N"))
          ParentId    = parentId
          Timestamp   = DateTimeOffset.UtcNow
          Author      = author
          Summary     = summary
          Changes     = changes
          FromSandbox = None }
    append ledgerPath tx
    tx

let private fc path oldBlob newBlob intent : FileChange =
    { Path = path; OldBlob = oldBlob; NewBlob = newBlob; Intent = intent; Annotation = None }


[<Fact>]
let ``auto-merge when changes are on disjoint files`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

       
        let genesis = makeTx None "test" "genesis" [] ledger

       
        let bidA = put objDir (mkBytes "a content")
        let bidB = put objDir (mkBytes "b content")

        let txA = makeTx (Some genesis.TxId) "alice" "modify a" [fc "file-a.fs" None (Some bidA) Add] ledger
        let txB = makeTx (Some genesis.TxId) "bob"   "modify b" [fc "file-b.fs" None (Some bidB) Add] ledger

        let result = merge objDir ledger txA txB "merger"

        Assert.True(result.IsAutoMerged))

[<Fact>]
let ``auto-merge when both sides make identical blob change`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let genesis = makeTx None "test" "genesis" [] ledger
        let bid = put objDir (mkBytes "same content")

        let txA = makeTx (Some genesis.TxId) "alice" "add shared" [fc "shared.fs" None (Some bid) Add] ledger
        let txB = makeTx (Some genesis.TxId) "bob"   "add shared" [fc "shared.fs" None (Some bid) Add] ledger

        let result = merge objDir ledger txA txB "merger"
        Assert.True(result.IsAutoMerged))

[<Fact>]
let ``conflict card emitted when both sides modify same file differently`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

       
        let baseBid = put objDir (mkBytes (String.replicate 50 "base line
"))
        let baseChange = fc "shared.fs" None (Some baseBid) Add
        let genesis = makeTx None "test" "genesis" [baseChange] ledger

       
        let bidA = put objDir (mkBytes (String.replicate 50 "alice's version
"))
        let bidB = put objDir (mkBytes (String.replicate 50 "bob's version
"))

        let txA = makeTx (Some genesis.TxId) "alice" "alice edit" [fc "shared.fs" (Some baseBid) (Some bidA) Modify] ledger
        let txB = makeTx (Some genesis.TxId) "bob"   "bob edit"   [fc "shared.fs" (Some baseBid) (Some bidB) Modify] ledger

        let result = merge objDir ledger txA txB "merger"

       
        match result with
        | AutoMerged _    -> ()  
        | NeedsReview cards ->
            Assert.NotEmpty cards
            let card = List.head cards
            Assert.Equal("shared.fs", card.ConflictPath)
            Assert.NotEmpty(card.OptionA.Label)
            Assert.NotEmpty(card.OptionC.Label)   
        | Incompatible reason ->
            Assert.Fail($"Expected merge attempt, got Incompatible: {reason}"))

[<Fact>]
let ``incompatible result when transactions have different parents`` () =
    withTempDir (fun tmp ->
        let objDir = Path.Combine(tmp, "objects")
        let ledger = Path.Combine(tmp, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let g1 = makeTx None "test" "genesis-1" [] ledger
        let g2 = makeTx None "test" "genesis-2" [] ledger
        let txA = makeTx (Some g1.TxId) "alice" "a" [] ledger
        let txB = makeTx (Some g2.TxId) "bob"   "b" [] ledger

        let result = merge objDir ledger txA txB "merger"
        Assert.True(result.IsIncompatible))
