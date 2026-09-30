
module Thallium.Core.GitBridge

open System
open System.IO
open System.Diagnostics
open System.Text
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.IntentResolver
open Thallium.Core.Repository


let private runProcess (workingDir: string) (exe: string) (args: string) =
    let psi = ProcessStartInfo(exe, args)
    psi.WorkingDirectory <- workingDir
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.CreateNoWindow <- true
    use proc = Process.Start(psi)
    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()
    proc.WaitForExit()
    if proc.ExitCode <> 0 then
        Error $"{exe} failed (code {proc.ExitCode}): {stderr}"
    else
        Ok stdout


type GitCommitInfo =
    { Hash       : string
      ParentHash : string option
      Author     : string
      Timestamp  : DateTimeOffset
      Subject    : string }

let private parseGitLog (gitRepoDir: string) : Result<GitCommitInfo list, string> =
   
    match runProcess gitRepoDir "git" "log --reverse --format=\"%H|%P|%an <%ae>|%aI|%s\"" with
    | Error err -> Error err
    | Ok output ->
        let lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        let commits =
            lines
            |> Seq.choose (fun line ->
                let parts = line.Trim().Trim('"').Split('|')
                if parts.Length >= 5 then
                    let hash = parts.[0]
                    let parents = parts.[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    let parent = parents |> Array.tryHead
                    let author = parts.[2]
                    let ts =
                        match DateTimeOffset.TryParse(parts.[3]) with
                        | true, dt -> dt
                        | _ -> DateTimeOffset.UtcNow
                    let subject = parts.[4..] |> String.concat "|"
                    Some { Hash = hash; ParentHash = parent; Author = author; Timestamp = ts; Subject = subject }
                else None)
            |> Seq.toList
        Ok commits

let importGitRepo (gitRepoDir: string) (targetTlDir: string) : Result<int, string> =
    let p =
        if Directory.Exists(Path.Combine(targetTlDir, ".tl")) then
            paths targetTlDir
        else
            let _ = init targetTlDir "Thallium Git Importer"
            paths targetTlDir

    match parseGitLog gitRepoDir with
    | Error err -> Error err
    | Ok gitCommits ->
        let mutable lastTxId : TxId option = None
        let gitToTxMap = System.Collections.Generic.Dictionary<string, TxId>()
        let lookupBlob bid = get p.ObjectsDir bid |> Option.defaultValue [||]

        for gitCommit in gitCommits do
           
            match runProcess gitRepoDir "git" $"ls-tree -r -z {gitCommit.Hash}" with
            | Error _ -> ()
            | Ok treeOutput ->
               
                let entries = treeOutput.Split([| char 0 |], StringSplitOptions.RemoveEmptyEntries)
                let currentFiles = System.Collections.Generic.Dictionary<string, BlobId>()

                for entry in entries do
                    let tabIdx = entry.IndexOf('\t')
                    if tabIdx > 0 then
                        let meta = entry.[..tabIdx - 1]
                        let filePath = entry.[tabIdx + 1..]
                        let metaParts = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        if metaParts.Length >= 3 && metaParts.[1] = "blob" then
                            let blobHash = metaParts.[2]
                           
                            match runProcess gitRepoDir "git" $"cat-file -p {blobHash}" with
                            | Ok blobContent ->
                                let bytes = Encoding.UTF8.GetBytes(blobContent)
                                let bid = put p.ObjectsDir bytes
                                currentFiles.[filePath] <- bid
                            | Error _ -> ()

               
                let parentTxId =
                    match gitCommit.ParentHash with
                    | Some ph when gitToTxMap.ContainsKey(ph) -> Some gitToTxMap.[ph]
                    | _ -> lastTxId

                let prevTree =
                    match parentTxId with
                    | Some pid -> Ledger.resolveTree p.LedgerPath pid
                    | None -> Map.empty

               
                let allPaths =
                    Set.union (currentFiles.Keys |> Set.ofSeq) (prevTree |> Map.keys |> Set.ofSeq)

                let changes =
                    allPaths
                    |> Set.toList
                    |> List.choose (fun path ->
                        let oldBid = Map.tryFind path prevTree
                        let newBid =
                            if currentFiles.ContainsKey(path) then Some currentFiles.[path]
                            else None
                        if oldBid = newBid then None
                        else
                            Some { Path = path; OldBlob = oldBid; NewBlob = newBid; Intent = Unknown; Annotation = None })

                let resolvedChanges = resolveAll gitCommit.Author lookupBlob changes
                let summary = if gitCommit.Subject.Trim() <> "" then gitCommit.Subject else summarise gitCommit.Author resolvedChanges

                let tx =
                    { TxId = TxId (Guid.NewGuid().ToString("N"))
                      ParentId = parentTxId
                      Timestamp = gitCommit.Timestamp
                      Author = gitCommit.Author
                      Summary = summary
                      Changes = resolvedChanges
                      FromSandbox = None; AgentData = None }

                append p.LedgerPath tx
                writeHead p tx.TxId
                gitToTxMap.[gitCommit.Hash] <- tx.TxId
                lastTxId <- Some tx.TxId

       
        match lastTxId with
        | Some tipId -> checkout p tipId
        | None -> ()

        Ok gitCommits.Length


let exportToGit (tlRepoDir: string) (targetGitDir: string) : Result<int, string> =
    let p = requireRoot tlRepoDir
    let transactions = readAll p.LedgerPath

    if not (Directory.Exists targetGitDir) then
        Directory.CreateDirectory targetGitDir |> ignore

    let isGit = Directory.Exists(Path.Combine(targetGitDir, ".git"))
    if not isGit then
        match runProcess targetGitDir "git" "init" with
        | Error err -> () |> ignore
        | Ok _ -> ()

    let mutable exportedCount = 0

    for tx in transactions do
        let tree = Ledger.resolveTree p.LedgerPath tx.TxId

       
        let existingFiles =
            Directory.EnumerateFiles(targetGitDir, "*", SearchOption.AllDirectories)
            |> Seq.filter (fun f -> not (f.Contains("/.git/") || f.Contains("\\.git\\")))
            |> Seq.toList

        for f in existingFiles do
            let rel = Path.GetRelativePath(targetGitDir, f).Replace('\\', '/')
            if not (Map.containsKey rel tree) then
                File.Delete(f)

       
        for KeyValue(relPath, bid) in tree do
            match get p.ObjectsDir bid with
            | Some bytes ->
                let fullPath = Path.Combine(targetGitDir, relPath.Replace('/', Path.DirectorySeparatorChar))
                let dir = Path.GetDirectoryName(fullPath)
                if not (String.IsNullOrEmpty(dir)) && not (Directory.Exists dir) then
                    Directory.CreateDirectory dir |> ignore
                File.WriteAllBytes(fullPath, bytes)
            | None -> ()

       
        let _ = runProcess targetGitDir "git" "add -A"
        let authorEnv = $"GIT_AUTHOR_NAME=\"{tx.Author}\" GIT_AUTHOR_DATE=\"{tx.Timestamp:O}\" GIT_COMMITTER_NAME=\"{tx.Author}\" GIT_COMMITTER_DATE=\"{tx.Timestamp:O}\""
        let safeMsg = tx.Summary.Replace("\"", "\\\"")
        let _ = runProcess targetGitDir "bash" $"-c \"{authorEnv} git commit --allow-empty -m \\\"{safeMsg}\\\"\""
        exportedCount <- exportedCount + 1

    Ok exportedCount
