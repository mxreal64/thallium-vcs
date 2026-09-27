// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.Domain

open System
open System.Collections.Generic


[<Struct>]
type BlobId = BlobId of string

[<Struct>]
type TxId = TxId of string

[<Struct>]
type SandboxId = SandboxId of string


type IntentTag =
    | Add              
    | Remove           
    | Modify           
    | Move             
    | Rename           
    | Refactor         
    | AIGenerated      
    | MergedByEngine   
    | Revert           
    | Unknown          


type FileChange =
    {
      Path        : string
     
      OldBlob     : BlobId option
     
      NewBlob     : BlobId option
     
      Intent      : IntentTag
     
      Annotation  : string option }


type Transaction =
    {
      TxId        : TxId
     
      ParentId    : TxId option
     
      Timestamp   : DateTimeOffset
     
      Author      : string
     
      Summary     : string
     
      Changes     : FileChange list
     
      FromSandbox : SandboxId option }


type SandboxState =
    { SandboxId   : SandboxId
     
      BaseTxId    : TxId
     
     
      Overlay     : Map<string, BlobId option>  
     
      ExpiresAt   : DateTimeOffset
     
      CreatedAt   : DateTimeOffset }


type ChoiceOption =
    { Label       : string        
      Description : string
      Changes     : FileChange list }

type ChoiceCard =
    { ConflictPath : string
      OptionA      : ChoiceOption  
      OptionB      : ChoiceOption  
      OptionC      : ChoiceOption  
      Rationale    : string }

type MergeResult =
    | AutoMerged  of Transaction           
    | NeedsReview of ChoiceCard list       
    | Incompatible of string               


type SymbolDef =
    {
      Name         : string
      Kind         : string         
      FilePath     : string
      StartLine    : int
      EndLine      : int
     
      Body         : string }

type ContextPayload =
    { TargetSymbol  : string
      Definition    : SymbolDef option
     
      Dependencies  : SymbolDef list
     
      RecentHistory : Transaction list
     
      Warning       : string option }


type RepoConfig =
    { RepoId      : string
      Name        : string
      CreatedAt   : DateTimeOffset
      DefaultAuthor : string }
