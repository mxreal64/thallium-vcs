// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.IntentResolver

open System
open System.IO
open Thallium.Core.Domain


let private levenshtein (a: byte[]) (b: byte[]) : int =
    let m, n = a.Length, b.Length
    if m = 0 then n
    elif n = 0 then m
    else
        let prev = Array.init (n + 1) id
        let curr = Array.zeroCreate (n + 1)
        for i in 1 .. m do
            curr.[0] <- i
            for j in 1 .. n do
                let cost = if a.[i-1] = b.[j-1] then 0 else 1
                curr.[j] <- min (min (curr.[j-1] + 1) (prev.[j] + 1)) (prev.[j-1] + cost)
            Array.blit curr 0 prev 0 (n + 1)
        prev.[n]

let private similarity (a: byte[]) (b: byte[]) : float =
    if a.Length = 0 && b.Length = 0 then 1.0
    else
        let maxLen = float (max a.Length b.Length)
        1.0 - (float (levenshtein a b) / maxLen)


let private ext (path: string) = Path.GetExtension(path).ToLowerInvariant()
let private filename (path: string) = Path.GetFileNameWithoutExtension(path).ToLowerInvariant()

let private looksLikeRename (oldPath: string) (newPath: string) =
    Path.GetDirectoryName(oldPath) = Path.GetDirectoryName(newPath) &&
    ext oldPath = ext newPath &&
    filename oldPath <> filename newPath

let private looksLikeMove (oldPath: string) (newPath: string) =
    Path.GetFileName(oldPath) = Path.GetFileName(newPath) &&
    Path.GetDirectoryName(oldPath) <> Path.GetDirectoryName(newPath)


let [<Literal>] private MoveThreshold   = 0.95
let [<Literal>] private RefactorThreshold = 0.40


let private isAiAuthor (author: string) =
    author.StartsWith("agent:", StringComparison.OrdinalIgnoreCase) ||
    author.Contains("gpt", StringComparison.OrdinalIgnoreCase)    ||
    author.Contains("claude", StringComparison.OrdinalIgnoreCase) ||
    author.Contains("gemini", StringComparison.OrdinalIgnoreCase)


let resolveIntent
    (author     : string)
    (lookupBlob : BlobId -> byte[])
    (fc         : FileChange)
    : IntentTag =

    let aiTag (inner: IntentTag) =
        if isAiAuthor author then AIGenerated else inner

    match fc.OldBlob, fc.NewBlob with
    | None,    None    -> Unknown
    | None,    Some _  -> aiTag Add
    | Some _,  None    -> aiTag Remove
    | Some oldId, Some newId ->
        if oldId = newId then
            if looksLikeMove fc.Path fc.Path then Move 
            else Unknown
        else
            let oldBytes = lookupBlob oldId
            let newBytes = lookupBlob newId
            let sim      = similarity oldBytes newBytes

           
            let extMatch = ext fc.Path <> ""
            if sim >= MoveThreshold then
                if looksLikeRename fc.Path fc.Path then Rename
                else aiTag Modify
            elif sim >= RefactorThreshold then
                aiTag Refactor
            else
                aiTag Modify

let detectCrossFileMoves
    (lookupBlob : BlobId -> byte[])
    (changes    : FileChange list)
    : FileChange list =

    let removals = changes |> List.filter (fun fc -> fc.NewBlob.IsNone && fc.OldBlob.IsSome)
    let additions = changes |> List.filter (fun fc -> fc.OldBlob.IsNone && fc.NewBlob.IsSome)

    let movePairs =
        [ for r in removals do
            for a in additions do
                let rb = lookupBlob r.OldBlob.Value
                let ab = lookupBlob a.NewBlob.Value
                let sim = similarity rb ab
               
                if sim >= MoveThreshold || (Path.GetFileName(r.Path) = Path.GetFileName(a.Path) && sim >= 0.70) then
                    yield (r.Path, a.Path, sim) ]

    let movedSources = movePairs |> List.map (fun (s, _, _) -> s) |> Set.ofList
    let movedTargets = movePairs |> List.map (fun (_, t, _) -> t) |> Set.ofList
    let moveMetaMap  = movePairs |> List.map (fun (s, t, sim) -> t, (s, sim)) |> Map.ofList

    changes |> List.map (fun fc ->
        if movedSources.Contains fc.Path && (fc.Intent = Remove || fc.Intent = Unknown) then
            { fc with Intent = Move; Annotation = Some "Moved to another directory" }
        elif movedTargets.Contains fc.Path && (fc.Intent = Add || fc.Intent = Unknown) then
            let annotation =
                match Map.tryFind fc.Path moveMetaMap with
                | Some (src, sim) -> Some $"Moved from '{src}' (similarity: {int (sim * 100.0)}%%)"
                | None -> Some "Moved from another directory"
            { fc with Intent = Move; Annotation = annotation }
        else fc)

let resolveAll
    (author     : string)
    (lookupBlob : BlobId -> byte[])
    (changes    : FileChange list)
    : FileChange list =

    changes
    |> List.map (fun fc -> { fc with Intent = resolveIntent author lookupBlob fc })
    |> detectCrossFileMoves lookupBlob


let summarise (author: string) (changes: FileChange list) : string =
    let grouped =
        changes
        |> List.groupBy (fun fc -> fc.Intent)
        |> List.map (fun (intent, fcs) ->
            let paths = fcs |> List.map (fun fc -> Path.GetFileName fc.Path)
            intent, paths)

    let parts =
        grouped |> List.map (fun (intent, paths) ->
            let fileList = String.concat ", " (List.truncate 3 paths) +
                           (if paths.Length > 3 then $" (+{paths.Length - 3} more)" else "")
            match intent with
            | Add         -> $"Added {fileList}"
            | Remove      -> $"Removed {fileList}"
            | Modify      -> $"Modified {fileList}"
            | Move        -> $"Moved {fileList}"
            | Rename      -> $"Renamed {fileList}"
            | Refactor    -> $"Refactored {fileList}"
            | AIGenerated -> $"AI-generated changes in {fileList}"
            | MergedByEngine -> $"Auto-merged {fileList}"
            | Revert      -> $"Reverted {fileList}"
            | Unknown     -> $"Updated {fileList}")

    let byLine = String.concat "; " parts
    if isAiAuthor author then $"[AI] {byLine}" else byLine
