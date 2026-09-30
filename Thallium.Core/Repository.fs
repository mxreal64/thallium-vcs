
module Thallium.Core.Repository

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.IntentResolver
open Thallium.Core.SandboxManager


type RepoPaths =
    { Root       : string
      TlDir      : string
      ObjectsDir : string
      LedgerPath : string
      HeadFile   : string
      ConfigFile : string
      IndexFile  : string }

let paths (workDir: string) =
    let root = workDir
    let tl   = Path.Combine(root, ".tl")
    { Root       = root
      TlDir      = tl
      ObjectsDir = Path.Combine(tl, "objects")
      LedgerPath = Path.Combine(tl, "ledger.bin")
      HeadFile   = Path.Combine(tl, "HEAD")
      ConfigFile = Path.Combine(tl, "config.json")
      IndexFile  = Path.Combine(tl, "index.json") }

let findRoot (startDir: string) : string option =
    let rec go dir =
        if Directory.Exists(Path.Combine(dir, ".tl")) then Some dir
        else
            let parent = Directory.GetParent(dir)
            if parent = null then None
            else go parent.FullName
    go startDir

let requireRoot (startDir: string) =
    match findRoot startDir with
    | Some r -> paths r
    | None   -> failwith "Not a Thallium repository (no .tl directory found)."


let private jsonOpts =
    let o = JsonSerializerOptions(WriteIndented = true)
    o.Converters.Add(JsonFSharpConverter())
    o

let private writeConfig (p: RepoPaths) (cfg: RepoConfig) =
    File.WriteAllText(p.ConfigFile, JsonSerializer.Serialize(cfg, jsonOpts))

let private readConfig (p: RepoPaths) : RepoConfig =
    JsonSerializer.Deserialize<RepoConfig>(File.ReadAllText(p.ConfigFile), jsonOpts)


let readHead (p: RepoPaths) : TxId option =
    if not (File.Exists p.HeadFile) then None
    else
        let text = File.ReadAllText(p.HeadFile).Trim()
        if text = "" then None else Some (TxId text)

let writeHead (p: RepoPaths) (txId: TxId) =
    let (TxId raw) = txId
    File.WriteAllText(p.HeadFile, raw)


type Index = Map<string, BlobId option>

let private emptyIndex : Index = Map.empty

let readIndex (p: RepoPaths) : Index =
    if not (File.Exists p.IndexFile) then emptyIndex
    else
        let raw = File.ReadAllText p.IndexFile
        JsonSerializer.Deserialize<Map<string, string option>>(raw)
        |> Map.map (fun _ v -> v |> Option.map BlobId)

let writeIndex (p: RepoPaths) (idx: Index) =
    let plain = idx |> Map.map (fun _ v -> v |> Option.map (fun (BlobId h) -> h))
    File.WriteAllText(p.IndexFile, JsonSerializer.Serialize(plain, jsonOpts))


let init (workDir: string) (authorName: string) : Transaction =
    let p = paths workDir
    if Directory.Exists p.TlDir then
        failwith $"Already a Thallium repository: {p.TlDir}"

    Directory.CreateDirectory p.TlDir    |> ignore
    Directory.CreateDirectory p.ObjectsDir |> ignore

    let cfg =
        { RepoId        = Guid.NewGuid().ToString("N")
          Name          = Path.GetFileName workDir
          CreatedAt     = DateTimeOffset.UtcNow
          DefaultAuthor = authorName }
    writeConfig p cfg

    let genesis =
        { TxId        = TxId (Guid.NewGuid().ToString("N"))
          ParentId    = None
          Timestamp   = DateTimeOffset.UtcNow
          Author      = authorName
          Summary     = "Initial commit (genesis)"
          Changes     = []
          FromSandbox = None; AgentData = None }

    append p.LedgerPath genesis
    writeHead p genesis.TxId
    writeIndex p emptyIndex
    genesis


let stageFile (p: RepoPaths) (relPath: string) =
    let fullPath = Path.Combine(p.Root, relPath)
    let idx = readIndex p
    let updated =
        if File.Exists fullPath then
            let bid = put p.ObjectsDir (File.ReadAllBytes fullPath)
            Map.add relPath (Some bid) idx
        else
            Map.add relPath None idx  
    writeIndex p updated

let stageAll (p: RepoPaths) =
    let headTree =
        match readHead p with
        | None    -> Map.empty
        | Some id -> Ledger.resolveTree p.LedgerPath id

   
    let workFiles =
        Directory.EnumerateFiles(p.Root, "*", SearchOption.AllDirectories)
        |> Seq.filter (fun f -> not (f.Contains(Path.Combine(".tl" + string Path.DirectorySeparatorChar)) || f.Contains("/.tl/")))
        |> Seq.map (fun f -> Path.GetRelativePath(p.Root, f).Replace('\\', '/'))
        |> Set.ofSeq

    let trackedPaths = headTree |> Map.keys |> Set.ofSeq
    let allPaths     = Set.union workFiles trackedPaths

    let idx = readIndex p
    let mutable updated = idx

    for relPath in allPaths do
        let fullPath = Path.Combine(p.Root, relPath)
        if File.Exists fullPath then
            let bytes = File.ReadAllBytes fullPath
            let bid   = put p.ObjectsDir bytes
           
            let headBid = Map.tryFind relPath headTree
            match headBid with
            | Some hb when hb = bid -> ()  
            | _ ->
                updated <- Map.add relPath (Some bid) updated
        else
           
            updated <- Map.add relPath None updated

    writeIndex p updated


let commit (p: RepoPaths) (author: string) (message: string) : Transaction option =
    let idx = readIndex p
    if Map.isEmpty idx then None
    else
        let parentId = readHead p
        let headTree =
            match parentId with
            | None    -> Map.empty
            | Some id -> Ledger.resolveTree p.LedgerPath id

        let lookupBlob bid = get p.ObjectsDir bid |> Option.defaultValue [||]

        let rawChanges =
            idx |> Map.toList |> List.map (fun (relPath, newBlobOpt) ->
                let oldBlobOpt = Map.tryFind relPath headTree
                { Path       = relPath
                  OldBlob    = oldBlobOpt
                  NewBlob    = newBlobOpt
                  Intent     = Unknown
                  Annotation = None })

        let changes  = resolveAll author lookupBlob rawChanges
        let summary  = if message.Trim() <> "" then message else summarise author changes

        let tx =
            { TxId        = TxId (Guid.NewGuid().ToString("N"))
              ParentId    = parentId
              Timestamp   = DateTimeOffset.UtcNow
              Author      = author
              Summary     = summary
              Changes     = changes
              FromSandbox = None; AgentData = None }

        append p.LedgerPath tx
        writeHead p tx.TxId
        writeIndex p emptyIndex 
        Some tx


let checkout (p: RepoPaths) (txId: TxId) =
    let tree = Ledger.resolveTree p.LedgerPath txId

   
    let existing =
        Directory.EnumerateFiles(p.Root, "*", SearchOption.AllDirectories)
        |> Seq.filter (fun f -> not (f.Contains("/.tl/") || f.Contains("\\.tl\\")))
        |> Seq.map (fun f -> Path.GetRelativePath(p.Root, f).Replace('\\', '/'))

    for relPath in existing do
        if not (Map.containsKey relPath tree) then
            File.Delete(Path.Combine(p.Root, relPath))

   
    for KeyValue(relPath, bid) in tree do
        match get p.ObjectsDir bid with
        | Some bytes ->
            let fullPath = Path.Combine(p.Root, relPath)
            Directory.CreateDirectory(Path.GetDirectoryName fullPath) |> ignore
            File.WriteAllBytes(fullPath, bytes)
        | None -> ()

    writeHead p txId
    writeIndex p emptyIndex


let private shortId (TxId s) = s.Substring(0, min 8 s.Length)

let rollback (p: RepoPaths) (targetTxId: TxId) (author: string) : Transaction =
    let headTxId   = readHead p |> Option.defaultWith (fun () -> failwith "No HEAD")
    let headTree   = Ledger.resolveTree p.LedgerPath headTxId
    let targetTree = Ledger.resolveTree p.LedgerPath targetTxId

    let changes =
        targetTree |> Map.toList |> List.map (fun (path, bid) ->
            let oldBid = Map.tryFind path headTree
            { Path = path; OldBlob = oldBid; NewBlob = Some bid
              Intent = Revert; Annotation = Some $"Rolled back to {shortId targetTxId}" })

    let deletions =
        headTree |> Map.toList
        |> List.filter (fun (path, _) -> not (Map.containsKey path targetTree))
        |> List.map (fun (path, bid) ->
            { Path = path; OldBlob = Some bid; NewBlob = None
              Intent = Revert; Annotation = Some $"Deleted by rollback to {shortId targetTxId}" })

    let tx =
        { TxId        = TxId (Guid.NewGuid().ToString("N"))
          ParentId    = Some headTxId
          Timestamp   = DateTimeOffset.UtcNow
          Author      = author
          Summary     = $"Rollback to {shortId targetTxId}"
          Changes     = changes @ deletions
          FromSandbox = None; AgentData = None }

    append p.LedgerPath tx
    writeHead p tx.TxId
    checkout p targetTxId
    tx


type FileStatus = | Staged | Modified | Untracked | Deleted

let status (p: RepoPaths) : (string * FileStatus) list =
    let headTree =
        match readHead p with
        | None    -> Map.empty
        | Some id -> Ledger.resolveTree p.LedgerPath id

    let idx = readIndex p

    let workFiles =
        Directory.EnumerateFiles(p.Root, "*", SearchOption.AllDirectories)
        |> Seq.filter (fun f -> not (f.Contains("/.tl/") || f.Contains("\\.tl\\")))
        |> Seq.map (fun f -> Path.GetRelativePath(p.Root, f).Replace('\\', '/'))
        |> Set.ofSeq

    let allPaths = workFiles |> Set.union (headTree |> Map.keys |> Set.ofSeq)
                             |> Set.union (idx |> Map.keys |> Set.ofSeq)

    [ for path in allPaths do
        let inHead    = Map.tryFind path headTree
        let inIndex   = Map.tryFind path idx
        let fullPath  = Path.Combine(p.Root, path)
        let onDisk    = File.Exists fullPath

        match inIndex with
        | Some _ ->
            yield path, Staged
        | None ->
            match inHead, onDisk with
            | None,    true  -> yield path, Untracked
            | Some _,  false -> yield path, Deleted
            | Some hb, true  ->
                let curBytes = File.ReadAllBytes fullPath
                let curBid   = put p.ObjectsDir curBytes
                if curBid <> hb then yield path, Modified
            | None, false -> () ] 
