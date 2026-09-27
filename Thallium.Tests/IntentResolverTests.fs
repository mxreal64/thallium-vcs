// SPDX-License-Identifier: MPL-2.0

module Thallium.Tests.IntentResolverTests

open System.Text
open Xunit
open Thallium.Core.Domain
open Thallium.Core.IntentResolver

let private blob (text: string) =
    let bytes = Encoding.UTF8.GetBytes(text)
    let hash = System.Security.Cryptography.SHA256.HashData(bytes)
    let hex = hash |> Array.map (fun (b: byte) -> b.ToString("x2")) |> String.concat ""
    BlobId hex

let private textBytes (s: string) = Encoding.UTF8.GetBytes s
let private lookup (store: Map<BlobId, byte[]>) (bid: BlobId) = store |> Map.tryFind bid |> Option.defaultValue [||]

[<Fact>]
let ``Add intent when OldBlob is None`` () =
    let store : Map<BlobId, byte[]> = Map.empty
    let fc = { Path = "new.fs"; OldBlob = None; NewBlob = Some (BlobId "x"); Intent = Unknown; Annotation = None }
    let intent = resolveIntent "human" (lookup store) fc
    Assert.Equal(Add, intent)

[<Fact>]
let ``Remove intent when NewBlob is None`` () =
    let store : Map<BlobId, byte[]> = Map.empty
    let fc = { Path = "old.fs"; OldBlob = Some (BlobId "x"); NewBlob = None; Intent = Unknown; Annotation = None }
    let intent = resolveIntent "human" (lookup store) fc
    Assert.Equal(Remove, intent)

[<Fact>]
let ``AIGenerated wraps Add for agent author`` () =
    let store : Map<BlobId, byte[]> = Map.empty
    let fc = { Path = "new.fs"; OldBlob = None; NewBlob = Some (BlobId "x"); Intent = Unknown; Annotation = None }
    let intent = resolveIntent "agent:gpt-4o" (lookup store) fc
    Assert.Equal(AIGenerated, intent)

[<Fact>]
let ``Modify intent when content differs substantially`` () =
    let aText = String.replicate 100 "original content line
"
    let bText = String.replicate 100 "completely different data
"
    let aBid = blob aText
    let bBid = blob bText
    let store = Map.ofList [aBid, textBytes aText; bBid, textBytes bText]
    let fc = { Path = "a.fs"; OldBlob = Some aBid; NewBlob = Some bBid; Intent = Unknown; Annotation = None }
    let intent = resolveIntent "human" (lookup store) fc
    Assert.True(intent = Modify || intent = Refactor)

[<Fact>]
let ``Cross-file move detection`` () =
    let content = "let shared = 42
"
    let bid = blob content
    let store = Map.ofList [bid, textBytes content]

    let removal = { Path = "src/old.fs"; OldBlob = Some bid; NewBlob = None; Intent = Remove; Annotation = None }
    let addition = { Path = "src/new/module.fs"; OldBlob = None; NewBlob = Some bid; Intent = Add; Annotation = None }

    let resolved = detectCrossFileMoves (lookup store) [removal; addition]
    let moveCount = resolved |> List.filter (fun fc -> fc.Intent = Move) |> List.length
    Assert.Equal(2, moveCount)

[<Fact>]
let ``summarise produces human-readable text`` () =
    let changes =
        [ { Path = "a.fs"; OldBlob = None;        NewBlob = Some (BlobId "x"); Intent = Add;    Annotation = None }
          { Path = "b.fs"; OldBlob = Some (BlobId "y"); NewBlob = None;        Intent = Remove; Annotation = None } ]
    let s = summarise "alice" changes
    Assert.Contains("Added", s)
    Assert.Contains("Removed", s)
