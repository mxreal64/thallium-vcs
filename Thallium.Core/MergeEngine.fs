
module Thallium.Core.MergeEngine

open System
open System.IO
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.IntentResolver


type private PathMerge =
    | OnlyInA    of FileChange             
    | OnlyInB    of FileChange             
    | BothSame   of FileChange             
    | Conflict   of FileChange * FileChange


let private lineTokens (data: byte[]) : string[] =
    Text.Encoding.UTF8.GetString(data).Split('\n')

let private tryTextMerge (baseBytes: byte[]) (oursBytes: byte[]) (theirsBytes: byte[]) : byte[] option =
    let baseLines   = lineTokens baseBytes
    let oursLines   = lineTokens oursBytes
    let theirsLines = lineTokens theirsBytes

   
   
    let changedByOurs   = Set.ofSeq (seq { for i in 0 .. min (baseLines.Length-1) (oursLines.Length-1) do
                                               if baseLines.[i] <> oursLines.[i] then yield i })
    let changedByTheirs = Set.ofSeq (seq { for i in 0 .. min (baseLines.Length-1) (theirsLines.Length-1) do
                                               if baseLines.[i] <> theirsLines.[i] then yield i })

    let overlap = Set.intersect changedByOurs changedByTheirs

    if Set.isEmpty overlap then
       
       
        let merged =
            theirsLines
            |> Array.mapi (fun i line ->
                if Set.contains i changedByOurs && i < oursLines.Length
                then oursLines.[i]
                else line)
        Some (Text.Encoding.UTF8.GetBytes(String.concat "\n" merged))
    else
        None 


let private isMoveCompatible (a: IntentTag) (b: IntentTag) =
    match a, b with
    | (Move | Rename), (Modify | Refactor | AIGenerated) -> true
    | (Modify | Refactor | AIGenerated), (Move | Rename) -> true
    | _ -> false


let merge
    (objectsDir : string)
    (ledgerPath : string)
    (txA        : Transaction)
    (txB        : Transaction)
    (mergeAuthor: string)
    : MergeResult =

   
    let incompatibleReason =
        match txA.ParentId, txB.ParentId with
        | None, _ | _, None    -> Some "Cannot merge a genesis transaction."
        | Some pa, Some pb when pa <> pb ->
            Some "Transactions do not share a common parent; 3-way merge not supported yet."
        | _ -> None

    match incompatibleReason with
    | Some reason -> Incompatible reason
    | None ->

    let lookupBlob bid = get objectsDir bid |> Option.defaultValue [||]

    let mapA = txA.Changes |> List.map (fun fc -> fc.Path, fc) |> Map.ofList
    let mapB = txB.Changes |> List.map (fun fc -> fc.Path, fc) |> Map.ofList
    let allPaths = Set.union (mapA |> Map.keys |> Set.ofSeq)
                             (mapB |> Map.keys |> Set.ofSeq)

    let pathMerges =
        allPaths |> Set.toList |> List.map (fun path ->
            match Map.tryFind path mapA, Map.tryFind path mapB with
            | Some a, None   -> OnlyInA a
            | None,   Some b -> OnlyInB b
            | Some a, Some b ->
                if a.NewBlob = b.NewBlob then BothSame a
                else Conflict (a, b)
            | None, None -> OnlyInA { Path = path; OldBlob = None; NewBlob = None
                                      Intent = Unknown; Annotation = None })

    let conflicts   = pathMerges |> List.choose (function Conflict (a,b) -> Some (a,b) | _ -> None)
    let nonConflict = pathMerges |> List.choose (function
        | OnlyInA fc | OnlyInB fc | BothSame fc -> Some fc
        | _ -> None)

    if conflicts.IsEmpty then
       
        let merged =
            { TxId        = TxId (Guid.NewGuid().ToString("N"))
              ParentId    = txA.ParentId
              Timestamp   = DateTimeOffset.UtcNow
              Author      = mergeAuthor
              Summary     = $"Auto-merged: '{txA.Summary}' + '{txB.Summary}'"
              Changes     = nonConflict |> List.map (fun fc -> { fc with Intent = MergedByEngine })
              FromSandbox = None; AgentData = None }
        append ledgerPath merged
        AutoMerged merged
    else
       
        let unresolvedCards = System.Collections.Generic.List<ChoiceCard>()
        let autoResolved    = System.Collections.Generic.List<FileChange>()

        for (a, b) in conflicts do
           
            if isMoveCompatible a.Intent b.Intent then
               
                let moveChange   = if a.Intent = Move || a.Intent = Rename then a else b
                let logicChange  = if a.Intent = Move || a.Intent = Rename then b else a
                let resolved = { moveChange with
                                    NewBlob    = logicChange.NewBlob
                                    Intent     = MergedByEngine
                                    Annotation = Some "Intent-aware: move + modify resolved automatically" }
                autoResolved.Add(resolved)
            else
               
                let baseTxId = txA.ParentId.Value
                let baseTree = resolveTree ledgerPath baseTxId
                let baseBytes =
                    match Map.tryFind a.Path baseTree with
                    | Some bid -> lookupBlob bid
                    | None     -> [||]
                let aBytes = a.NewBlob |> Option.map lookupBlob |> Option.defaultValue [||]
                let bBytes = b.NewBlob |> Option.map lookupBlob |> Option.defaultValue [||]

                match tryTextMerge baseBytes aBytes bBytes with
                | Some merged ->
                    let mergedBid = put objectsDir merged
                    autoResolved.Add({ a with NewBlob = Some mergedBid; Intent = MergedByEngine
                                              Annotation = Some "3-way text merge succeeded" })
                | None ->
                   
                    let optA =
                        { Label       = "Option A – Yours"
                          Description = $"Keep the changes from '{txA.Author}': {txA.Summary}"
                          Changes     = [a] }
                    let optB =
                        { Label       = "Option B – Theirs"
                          Description = $"Keep the changes from '{txB.Author}': {txB.Summary}"
                          Changes     = [b] }
                   
                    let optCChanges =
                        let combined = { a with
                                           NewBlob    = b.NewBlob 
                                           Intent     = MergedByEngine
                                           Annotation = Some "Combined candidate" }
                        [combined]

                    let testSandbox = SandboxManager.create baseTxId (TimeSpan.FromMinutes 2.0)
                    let testOverlaySandbox =
                        match b.NewBlob with
                        | Some bid -> { testSandbox with Overlay = Map.ofList [a.Path, Some bid] }
                        | None     -> { testSandbox with Overlay = Map.ofList [a.Path, None] }

                    let testResult = SandboxRunner.runTestInSandbox objectsDir ledgerPath testOverlaySandbox None
                    let statusBadge = if testResult.Passed then "[VERIFIED PASSING]" else "[TESTS FAILED]"
                    let testSummary =
                        if testResult.Passed then $"Passed test suite in {testResult.DurationMs}ms ({testResult.CommandRun})"
                        else $"Tests failed with exit code {testResult.ExitCode} ({testResult.CommandRun})"

                    let optC =
                        { Label       = $"Option C – Pre-tested Combined {statusBadge}"
                          Description = $"{testSummary}. Candidate applies both mutations sequentially."
                          Changes     = optCChanges }
                    unresolvedCards.Add(
                        { ConflictPath = a.Path
                          OptionA      = optA
                          OptionB      = optB
                          OptionC      = optC
                          Rationale    = $"Lines overlap in '{a.Path}'; both '{txA.Author}' and '{txB.Author}' modified the same region." })

        if unresolvedCards.Count = 0 then
           
            let allChanges = (nonConflict @ List.ofSeq autoResolved) |> List.map (fun fc -> { fc with Intent = MergedByEngine })
            let merged =
                { TxId        = TxId (Guid.NewGuid().ToString("N"))
                  ParentId    = txA.ParentId
                  Timestamp   = DateTimeOffset.UtcNow
                  Author      = mergeAuthor
                  Summary     = $"Auto-merged (intent-aware): '{txA.Summary}' + '{txB.Summary}'"
                  Changes     = allChanges
                  FromSandbox = None; AgentData = None }
            append ledgerPath merged
            AutoMerged merged
        else
            NeedsReview (List.ofSeq unresolvedCards)
