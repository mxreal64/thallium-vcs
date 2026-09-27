// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.GarbageCollector

open System
open System.IO
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager

type GcResult =
    { TotalBlobsBefore   : int
      TotalBlobsAfter    : int
      PrunedBlobsCount   : int
      BytesReclaimed     : int64
      CompactedTxCount   : int }

let collect
    (objectsDir : string)
    (ledgerPath : string)
    (tlDir      : string)
    : GcResult =

    let allDiskBlobs = ObjectStore.listAll objectsDir |> Seq.toList
    let allDiskBlobsSet = allDiskBlobs |> Set.ofList

   
    let allTransactions = Ledger.readAll ledgerPath
    let txReferencedBlobs =
        allTransactions
        |> List.collect (fun tx ->
            tx.Changes
            |> List.choose (fun fc -> fc.NewBlob))
        |> Set.ofList

   
    let treeBlobs =
        allTransactions
        |> List.collect (fun tx ->
            Ledger.resolveTree ledgerPath tx.TxId
            |> Map.values
            |> Seq.toList)
        |> Set.ofList

   
    let activeSandboxes = SandboxManager.listFromRepo tlDir
    let sandboxBlobs =
        activeSandboxes
        |> List.collect (fun sb ->
            sb.Overlay
            |> Map.values
            |> Seq.choose id
            |> Seq.toList)
        |> Set.ofList

    let liveBlobs = Set.unionMany [txReferencedBlobs; treeBlobs; sandboxBlobs]

    let orphanedBlobs = Set.difference allDiskBlobsSet liveBlobs

    let mutable bytesReclaimed = 0L
    let mutable prunedCount = 0

    for (BlobId hash) in orphanedBlobs do
        let prefix = hash.[0..1]
        let rest   = hash.[2..]
        let path   = Path.Combine(objectsDir, prefix, rest)
        if File.Exists path then
            try
                let fi = FileInfo(path)
                bytesReclaimed <- bytesReclaimed + fi.Length
                File.Delete(path)
                prunedCount <- prunedCount + 1
            with _ -> ()

    { TotalBlobsBefore = allDiskBlobs.Length
      TotalBlobsAfter  = allDiskBlobs.Length - prunedCount
      PrunedBlobsCount = prunedCount
      BytesReclaimed   = bytesReclaimed
      CompactedTxCount = allTransactions.Length }
