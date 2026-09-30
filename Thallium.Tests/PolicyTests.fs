/// Tests for PolicyEngine.
module Thallium.Tests.PolicyTests

open System
open System.IO
open Xunit
open Thallium.Core.Domain
open Thallium.Core.PolicyEngine

[<Fact>]
let ``Protected path rejects commits targeting .env and infra files`` () =
    let policy =
        { ProtectedPaths       = [ ".env*"; "infra/**" ]
          MaxFilesPerCommit    = Some 10
          DisallowAgentDeletes = true
          RequireHumanForPaths = [ "auth/**" ] }

    let tx : Transaction =
        { TxId        = TxId "tx1"
          ParentId    = None
          Timestamp   = DateTimeOffset.UtcNow
          Author      = "alice"
          Summary     = "Modify env"
          Changes     = [ { Path = ".env.production"; OldBlob = None; NewBlob = Some (BlobId "x"); Intent = Add; Annotation = None } ]
          FromSandbox = None
          AgentData   = None }

    match validateTransaction policy tx with
    | Ok () -> Assert.Fail("Expected policy validation to fail for protected path.")
    | Error errors ->
        Assert.Contains(errors, (fun (err: string) -> err.Contains("is protected by repository policy")))

[<Fact>]
let ``AI agent cannot modify human-required path`` () =
    let policy =
        { ProtectedPaths       = [ ".env*" ]
          MaxFilesPerCommit    = Some 10
          DisallowAgentDeletes = true
          RequireHumanForPaths = [ "auth/**" ] }

    let agentTx : Transaction =
        { TxId        = TxId "tx2"
          ParentId    = None
          Timestamp   = DateTimeOffset.UtcNow
          Author      = "agent:gpt-4o"
          Summary     = "AI auth tweak"
          Changes     = [ { Path = "auth/tokens.fs"; OldBlob = None; NewBlob = Some (BlobId "y"); Intent = Add; Annotation = None } ]
          FromSandbox = None
          AgentData   = None }

    let humanTx : Transaction =
        { agentTx with Author = "alice" }

    match validateTransaction policy agentTx with
    | Ok () -> Assert.Fail("Expected agent auth modification to be blocked.")
    | Error errors ->
        Assert.Contains(errors, (fun (err: string) -> err.Contains("requires human authorship")))

    match validateTransaction policy humanTx with
    | Ok () -> ()
    | Error errors -> Assert.Fail($"Human commit should have succeeded: {errors}")

[<Fact>]
let ``AI agent cannot delete files when DisallowAgentDeletes is true`` () =
    let policy =
        { ProtectedPaths       = []
          MaxFilesPerCommit    = Some 10
          DisallowAgentDeletes = true
          RequireHumanForPaths = [] }

    let agentDeleteTx : Transaction =
        { TxId        = TxId "tx3"
          ParentId    = None
          Timestamp   = DateTimeOffset.UtcNow
          Author      = "agent:claude-3-5"
          Summary     = "AI delete file"
          Changes     = [ { Path = "src/old.fs"; OldBlob = Some (BlobId "z"); NewBlob = None; Intent = Remove; Annotation = None } ]
          FromSandbox = None
          AgentData   = None }

    match validateTransaction policy agentDeleteTx with
    | Ok () -> Assert.Fail("Expected agent file deletion to be blocked.")
    | Error errors ->
        Assert.Contains(errors, (fun (err: string) -> err.Contains("is forbidden from deleting file")))
