// SPDX-License-Identifier: MPL-2.0
module Thallium.Core.SandboxManager

open System
open System.Collections.Concurrent
open System.IO
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.IntentResolver


let private registry = ConcurrentDictionary<SandboxId, SandboxState>()


let create (baseTxId: TxId) (lifetime: TimeSpan) : SandboxState =
    let id  = SandboxId (Guid.NewGuid().ToString("N"))
    let now = DateTimeOffset.UtcNow
    let sb  =
        { SandboxId  = id
          BaseTxId   = baseTxId
          Overlay    = Map.empty
          ExpiresAt  = now + lifetime
          CreatedAt  = now }
    registry.[id] <- sb
    sb

let tryGet (id: SandboxId) : SandboxState option =
    match registry.TryGetValue(id) with
    | true, sb when DateTimeOffset.UtcNow < sb.ExpiresAt -> Some sb
    | true, _ ->
        registry.TryRemove(id) |> ignore
        None
    | _ -> None

let listAll () : SandboxState list =
    let now = DateTimeOffset.UtcNow
    registry.Values
    |> Seq.filter (fun sb -> now < sb.ExpiresAt)
    |> Seq.toList

let drop (id: SandboxId) =
    registry.TryRemove(id) |> ignore


let private jsonOptions =
    let o = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
    o.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
    o

let private sandboxesDir (tlDir: string) =
    let dir = Path.Combine(tlDir, "sandboxes")
    if not (Directory.Exists dir) then Directory.CreateDirectory dir |> ignore
    dir

let private sandboxFile (tlDir: string) (SandboxId id) =
    Path.Combine(sandboxesDir tlDir, $"{id}.json")

let saveToRepo (tlDir: string) (sb: SandboxState) =
    let json = System.Text.Json.JsonSerializer.Serialize(sb, jsonOptions)
    File.WriteAllText(sandboxFile tlDir sb.SandboxId, json)
    registry.[sb.SandboxId] <- sb

let loadFromRepo (tlDir: string) (id: SandboxId) : SandboxState option =
    match tryGet id with
    | Some sb -> Some sb
    | None ->
        let file = sandboxFile tlDir id
        if File.Exists file then
            try
                let json = File.ReadAllText file
                let sb = System.Text.Json.JsonSerializer.Deserialize<SandboxState>(json, jsonOptions)
                if DateTimeOffset.UtcNow < sb.ExpiresAt then
                    registry.[sb.SandboxId] <- sb
                    Some sb
                else
                    File.Delete file
                    None
            with _ -> None
        else None

let listFromRepo (tlDir: string) : SandboxState list =
    let dir = sandboxesDir tlDir
    let now = DateTimeOffset.UtcNow
    let diskSandboxes =
        if Directory.Exists dir then
            Directory.EnumerateFiles(dir, "*.json")
            |> Seq.choose (fun file ->
                try
                    let json = File.ReadAllText file
                    let sb = System.Text.Json.JsonSerializer.Deserialize<SandboxState>(json, jsonOptions)
                    if now < sb.ExpiresAt then
                        registry.[sb.SandboxId] <- sb
                        Some sb
                    else
                        File.Delete file
                        None
                with _ -> None)
            |> Seq.toList
        else []
    let memSandboxes = listAll ()
    (diskSandboxes @ memSandboxes)
    |> List.distinctBy (fun sb -> sb.SandboxId)

let removeFromRepo (tlDir: string) (id: SandboxId) =
    drop id
    let file = sandboxFile tlDir id
    if File.Exists file then File.Delete file


let readFile
    (objectsDir : string)
    (ledgerPath : string)
    (sb         : SandboxState)
    (path       : string)
    : byte[] option =

    match Map.tryFind path sb.Overlay with
    | Some None       -> None         
    | Some (Some bid) -> get objectsDir bid
    | None ->
        let tree = resolveTree ledgerPath sb.BaseTxId
        match Map.tryFind path tree with
        | Some bid -> get objectsDir bid
        | None     -> None

let writeFile
    (objectsDir : string)
    (sb         : SandboxState)
    (path       : string)
    (data       : byte[])
    : SandboxState =

    let bid     = put objectsDir data
    let overlay = Map.add path (Some bid) sb.Overlay
    let updated = { sb with Overlay = overlay }
    registry.[sb.SandboxId] <- updated
    updated

let deleteFile (sb: SandboxState) (path: string) : SandboxState =
    let overlay = Map.add path None sb.Overlay
    let updated = { sb with Overlay = overlay }
    registry.[sb.SandboxId] <- updated
    updated


let commit
    (objectsDir : string)
    (ledgerPath : string)
    (sb         : SandboxState)
    (author     : string)
    (message    : string)
    : Transaction =

   
    let baseTree = resolveTree ledgerPath sb.BaseTxId

   
    let changes =
        sb.Overlay
        |> Map.toList
        |> List.map (fun (path, newBlobOpt) ->
            let oldBlobOpt = Map.tryFind path baseTree
            { Path       = path
              OldBlob    = oldBlobOpt
              NewBlob    = newBlobOpt
              Intent     = Unknown  
              Annotation = None })

   
    let lookupBlob bid =
        get objectsDir bid |> Option.defaultValue [||]

    let resolvedChanges = resolveAll author lookupBlob changes
    let summary = if message.Trim() <> "" then message else summarise author resolvedChanges

    let tx =
        { TxId        = TxId (Guid.NewGuid().ToString("N"))
          ParentId    = Some sb.BaseTxId
          Timestamp   = DateTimeOffset.UtcNow
          Author      = author
          Summary     = summary
          Changes     = resolvedChanges
          FromSandbox = Some sb.SandboxId }

    append ledgerPath tx
    tx


let materialise
    (objectsDir : string)
    (ledgerPath : string)
    (sb         : SandboxState)
    (targetDir  : string)
    =
    let baseTree = resolveTree ledgerPath sb.BaseTxId

   
    for KeyValue(path, bid) in baseTree do
        match get objectsDir bid with
        | Some bytes ->
            let fullPath = Path.Combine(targetDir, path.Replace('/', Path.DirectorySeparatorChar))
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)) |> ignore
            File.WriteAllBytes(fullPath, bytes)
        | None -> ()

   
    for KeyValue(path, overlayOpt) in sb.Overlay do
        let fullPath = Path.Combine(targetDir, path.Replace('/', Path.DirectorySeparatorChar))
        match overlayOpt with
        | None ->
            if File.Exists fullPath then File.Delete fullPath
        | Some bid ->
            match get objectsDir bid with
            | Some bytes ->
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)) |> ignore
                File.WriteAllBytes(fullPath, bytes)
            | None -> ()
