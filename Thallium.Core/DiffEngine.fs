// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.DiffEngine

open System
open System.IO
open System.Text
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger

type DiffLineKind =
    | Context
    | Addition
    | Deletion

type DiffLine =
    { Kind    : DiffLineKind
      OldLine : int option
      NewLine : int option
      Text    : string }

type FileDiff =
    { Path       : string
      OldBlob    : BlobId option
      NewBlob    : BlobId option
      Intent     : IntentTag
      Lines      : DiffLine list
      Additions  : int
      Deletions  : int }

let computeLineDiff (oldText: string) (newText: string) : DiffLine list =
    let oldLines = if String.IsNullOrEmpty oldText then [||] else oldText.Split('\n')
    let newLines = if String.IsNullOrEmpty newText then [||] else newText.Split('\n')

    let n = oldLines.Length
    let m = newLines.Length

    let dp = Array2D.zeroCreate (n + 1) (m + 1)
    for i in 1 .. n do
        for j in 1 .. m do
            if oldLines.[i - 1] = newLines.[j - 1] then
                dp.[i, j] <- dp.[i - 1, j - 1] + 1
            else
                dp.[i, j] <- max dp.[i - 1, j] dp.[i, j - 1]

    let rec backtrack i j acc =
        if i > 0 && j > 0 && oldLines.[i - 1] = newLines.[j - 1] then
            backtrack (i - 1) (j - 1) ({ Kind = Context; OldLine = Some i; NewLine = Some j; Text = oldLines.[i - 1] } :: acc)
        elif j > 0 && (i = 0 || dp.[i, j - 1] >= dp.[i - 1, j]) then
            backtrack i (j - 1) ({ Kind = Addition; OldLine = None; NewLine = Some j; Text = newLines.[j - 1] } :: acc)
        elif i > 0 && (j = 0 || dp.[i, j - 1] < dp.[i - 1, j]) then
            backtrack (i - 1) j ({ Kind = Deletion; OldLine = Some i; NewLine = None; Text = oldLines.[i - 1] } :: acc)
        else
            acc

    backtrack n m []

let diffTransactions
    (objectsDir : string)
    (ledgerPath : string)
    (txAId      : TxId)
    (txBId      : TxId)
    : FileDiff list =

    let treeA = Ledger.resolveTree ledgerPath txAId
    let treeB = Ledger.resolveTree ledgerPath txBId

    let allPaths =
        Set.union (treeA |> Map.keys |> Set.ofSeq)
                  (treeB |> Map.keys |> Set.ofSeq)

    allPaths
    |> Set.toList
    |> List.choose (fun path ->
        let blobA = Map.tryFind path treeA
        let blobB = Map.tryFind path treeB

        if blobA = blobB then None
        else
            let textA = match blobA with Some b -> ObjectStore.getText objectsDir b |> Option.defaultValue "" | None -> ""
            let textB = match blobB with Some b -> ObjectStore.getText objectsDir b |> Option.defaultValue "" | None -> ""
            let lines = computeLineDiff textA textB
            let adds  = lines |> List.filter (fun l -> l.Kind = Addition) |> List.length
            let dels  = lines |> List.filter (fun l -> l.Kind = Deletion) |> List.length

            let intent =
                if blobA.IsNone then Add
                elif blobB.IsNone then Remove
                elif adds > 0 && dels > 0 && float (min adds dels) / float (max adds dels) > 0.6 then Refactor
                else Modify

            Some { Path      = path
                   OldBlob   = blobA
                   NewBlob   = blobB
                   Intent    = intent
                   Lines     = lines
                   Additions = adds
                   Deletions = dels })
