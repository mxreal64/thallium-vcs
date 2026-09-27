// SPDX-License-Identifier: MPL-2.0

module Thallium.Tests.DiffEngineTests

open System
open System.IO
open System.Text
open Xunit
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.DiffEngine

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), "tl_diff_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``computeLineDiff detects additions and deletions accurately`` () =
    let oldText = "line1
line2
line3"
    let newText = "line1
modified_line2
line3
line4"

    let lines = computeLineDiff oldText newText

    Assert.Contains(lines, (fun l -> l.Kind = Addition && l.Text = "modified_line2"))
    Assert.Contains(lines, (fun l -> l.Kind = Deletion && l.Text = "line2"))
    Assert.Contains(lines, (fun l -> l.Kind = Addition && l.Text = "line4"))
    Assert.Contains(lines, (fun l -> l.Kind = Context && l.Text = "line1"))

[<Fact>]
let ``diffTransactions reports modified files and diff lines`` () =
    withTempDir (fun dir ->
        let objDir = Path.Combine(dir, "objects")
        let ledger = Path.Combine(dir, "ledger.bin")
        Directory.CreateDirectory objDir |> ignore

        let blobA = put objDir (Encoding.UTF8.GetBytes("let x = 1
let y = 2"))
        let blobB = put objDir (Encoding.UTF8.GetBytes("let x = 1
let y = 3
let z = 4"))

        let tx1 =
            { TxId = TxId (Guid.NewGuid().ToString("N"))
              ParentId = None
              Timestamp = DateTimeOffset.UtcNow
              Author = "alice"
              Summary = "Init"
              Changes = [ { Path = "test.fs"; OldBlob = None; NewBlob = Some blobA; Intent = Add; Annotation = None } ]
              FromSandbox = None }
        append ledger tx1

        let tx2 =
            { TxId = TxId (Guid.NewGuid().ToString("N"))
              ParentId = Some tx1.TxId
              Timestamp = DateTimeOffset.UtcNow
              Author = "bob"
              Summary = "Update"
              Changes = [ { Path = "test.fs"; OldBlob = Some blobA; NewBlob = Some blobB; Intent = Modify; Annotation = None } ]
              FromSandbox = None }
        append ledger tx2

        let diffs = diffTransactions objDir ledger tx1.TxId tx2.TxId

        Assert.Equal(1, diffs.Length)
        let d = List.head diffs
        Assert.Equal("test.fs", d.Path)
        Assert.True(d.Additions >= 2)
        Assert.True(d.Deletions >= 1))
