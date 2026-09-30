/// Thallium.Core.RemoteSync
/// Synchronization of transaction ledgers and content-addressed blobs between local and remote repositories.
module Thallium.Core.RemoteSync

open System
open System.IO
open System.Text.Json
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.Repository

type RemoteConfig =
    { Name : string
      Url  : string }

type RemotesFile =
    { Remotes : RemoteConfig list }

let private jsonOptions =
    let o = JsonSerializerOptions(WriteIndented = true)
    o.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
    o

let private remotesFilePath (tlDir: string) =
    Path.Combine(tlDir, "remotes.json")

/// Load configured remotes from .tl/remotes.json
let listRemotes (tlDir: string) : RemoteConfig list =
    let p = remotesFilePath tlDir
    if File.Exists p then
        try
            let json = File.ReadAllText p
            let rf = JsonSerializer.Deserialize<RemotesFile>(json, jsonOptions)
            rf.Remotes
        with _ -> []
    else []

/// Add or update a named remote configuration
let addRemote (tlDir: string) (name: string) (url: string) : unit =
    let current = listRemotes tlDir |> List.filter (fun r -> r.Name <> name)
    let updated = { Remotes = { Name = name; Url = url } :: current }
    let json = JsonSerializer.Serialize(updated, jsonOptions)
    File.WriteAllText(remotesFilePath tlDir, json)

/// Remove a named remote
let removeRemote (tlDir: string) (name: string) : unit =
    let updated = { Remotes = listRemotes tlDir |> List.filter (fun r -> r.Name <> name) }
    let json = JsonSerializer.Serialize(updated, jsonOptions)
    File.WriteAllText(remotesFilePath tlDir, json)

/// Push local transactions and required blobs to a remote repository directory.
/// Returns Ok (pushedTxCount, pushedBlobCount) or Error string.
let push (localPaths: RepoPaths) (remoteUrl: string) : Result<int * int, string> =
    if not (Directory.Exists remoteUrl) then
        Error $"Remote destination directory does not exist: {remoteUrl}"
    else
        let remotePaths =
            if Directory.Exists(Path.Combine(remoteUrl, ".tl")) then
                paths remoteUrl
            else
                init remoteUrl "Remote Repo" |> ignore
                paths remoteUrl

        let localTxs  = Ledger.readAll localPaths.LedgerPath
        let remoteTxs = Ledger.readAll remotePaths.LedgerPath
        let remoteTxIds = remoteTxs |> List.map (fun t -> t.TxId) |> Set.ofList

        let missingTxs = localTxs |> List.filter (fun t -> not (Set.contains t.TxId remoteTxIds))
        let mutable blobsPushed = 0

        // Push required blobs
        for tx in missingTxs do
            for fc in tx.Changes do
                match fc.NewBlob with
                | Some bid ->
                    if not (ObjectStore.exists remotePaths.ObjectsDir bid) then
                        match ObjectStore.get localPaths.ObjectsDir bid with
                        | Some bytes ->
                            let _ = ObjectStore.put remotePaths.ObjectsDir bytes
                            blobsPushed <- blobsPushed + 1
                        | None -> ()
                | None -> ()

            // Append transaction to remote ledger
            Ledger.append remotePaths.LedgerPath tx

        // Update remote HEAD to match local HEAD
        match Repository.readHead localPaths with
        | Some localHead -> Repository.writeHead remotePaths localHead
        | None -> ()

        Ok (missingTxs.Length, blobsPushed)

/// Pull remote transactions and required blobs into local repository, fast-forwarding HEAD.
/// Returns Ok (pulledTxCount, pulledBlobCount) or Error string.
let pull (localPaths: RepoPaths) (remoteUrl: string) : Result<int * int, string> =
    if not (Directory.Exists remoteUrl) then
        Error $"Remote source directory does not exist: {remoteUrl}"
    else
        let remoteTlDir = Path.Combine(remoteUrl, ".tl")
        if not (Directory.Exists remoteTlDir) then
            Error $"Remote directory is not a valid Thallium repository (.tl missing): {remoteUrl}"
        else
            let remotePaths = paths remoteUrl
            let localTxs    = Ledger.readAll localPaths.LedgerPath
            let remoteTxs   = Ledger.readAll remotePaths.LedgerPath
            let localTxIds  = localTxs |> List.map (fun t -> t.TxId) |> Set.ofList

            let missingTxs = remoteTxs |> List.filter (fun t -> not (Set.contains t.TxId localTxIds))
            let mutable blobsPulled = 0

            for tx in missingTxs do
                for fc in tx.Changes do
                    match fc.NewBlob with
                    | Some bid ->
                        if not (ObjectStore.exists localPaths.ObjectsDir bid) then
                            match ObjectStore.get remotePaths.ObjectsDir bid with
                            | Some bytes ->
                                let _ = ObjectStore.put localPaths.ObjectsDir bytes
                                blobsPulled <- blobsPulled + 1
                            | None -> ()
                    | None -> ()

                Ledger.append localPaths.LedgerPath tx

            // Fast-forward local HEAD and checkout if new commits arrived
            match Repository.readHead remotePaths with
            | Some remoteHead when missingTxs.Length > 0 ->
                Repository.writeHead localPaths remoteHead
                Repository.checkout localPaths remoteHead
            | _ -> ()

            Ok (missingTxs.Length, blobsPulled)
