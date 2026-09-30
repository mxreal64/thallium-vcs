/// Tests for RemoteSync (push and pull between local and remote repositories).
module Thallium.Tests.RemoteSyncTests

open System
open System.IO
open Xunit
open Thallium.Core.Domain
open Thallium.Core.Ledger
open Thallium.Core.Repository
open Thallium.Core.RemoteSync

let private withTempDir f =
    let dir = Path.Combine(Path.GetTempPath(), "tl_remote_test_" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory dir |> ignore
    try f dir
    finally
        try Directory.Delete(dir, true) with _ -> ()

[<Fact>]
let ``push transfers transactions and missing blobs to remote destination`` () =
    withTempDir (fun root ->
        let localDir  = Path.Combine(root, "local_repo")
        let remoteDir = Path.Combine(root, "remote_repo")
        Directory.CreateDirectory localDir  |> ignore
        Directory.CreateDirectory remoteDir |> ignore

        let _ = init localDir "Alice Local"
        let _ = init remoteDir "Remote Central"

        let localPaths = paths localDir
        File.WriteAllText(Path.Combine(localDir, "shared.fs"), "let shared_value = 100")
        stageAll localPaths
        let _ = commit localPaths "alice" "Add shared.fs"

        let pushResult = push localPaths remoteDir
        match pushResult with
        | Error err -> Assert.Fail($"Push failed: {err}")
        | Ok (txCount, blobCount) ->
            Assert.True(txCount >= 1)
            Assert.True(blobCount >= 1)

            let remotePaths = paths remoteDir
            let remoteTxs = readAll remotePaths.LedgerPath
            Assert.Contains(remoteTxs, (fun t -> t.Summary = "Add shared.fs")))

[<Fact>]
let ``pull fetches remote transactions and fast-forwards local HEAD`` () =
    withTempDir (fun root ->
        let localDir  = Path.Combine(root, "local_repo")
        let remoteDir = Path.Combine(root, "remote_repo")
        Directory.CreateDirectory localDir  |> ignore
        Directory.CreateDirectory remoteDir |> ignore

        let _ = init localDir "Alice Local"
        let _ = init remoteDir "Remote Central"

        let remotePaths = paths remoteDir
        File.WriteAllText(Path.Combine(remoteDir, "server.fs"), "let start_server () = 8080")
        stageAll remotePaths
        let _ = commit remotePaths "bob" "Add server.fs on remote"

        let localPaths = paths localDir
        let pullResult = pull localPaths remoteDir
        match pullResult with
        | Error err -> Assert.Fail($"Pull failed: {err}")
        | Ok (txCount, blobCount) ->
            Assert.True(txCount >= 1)

            let localTxs = readAll localPaths.LedgerPath
            Assert.Contains(localTxs, (fun t -> t.Summary = "Add server.fs on remote"))
            Assert.True(File.Exists(Path.Combine(localDir, "server.fs"))))
