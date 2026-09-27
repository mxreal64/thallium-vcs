// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.Ledger

open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open Thallium.Core.Domain


let private jsonOptions =
    let o = JsonSerializerOptions(WriteIndented = false)
    o.Converters.Add(JsonFSharpConverter())
    o

let private serialize (tx: Transaction) : byte[] =
    JsonSerializer.SerializeToUtf8Bytes(tx, jsonOptions)

let private deserialize (data: byte[]) : Transaction =
    JsonSerializer.Deserialize<Transaction>(ReadOnlySpan<byte>(data), jsonOptions)


let private writeFrame (stream: Stream) (payload: byte[]) =
    let lenBytes = BitConverter.GetBytes(payload.Length)
    stream.Write(lenBytes, 0, 4)
    stream.Write(payload, 0, payload.Length)
    stream.WriteByte(byte '
')

let private readAllFrames (stream: Stream) : byte[] list =
    let mutable frames = []
    let lenBuf = Array.zeroCreate<byte> 4
    let mutable eof = false
    while not eof do
        let read = stream.Read(lenBuf, 0, 4)
        if read = 0 then eof <- true
        elif read < 4 then
            eof <- true
        else
            let len = BitConverter.ToInt32(lenBuf, 0)
            let payload = Array.zeroCreate<byte> len
            let _ = stream.Read(payload, 0, len)
            let _ = stream.ReadByte()
            frames <- payload :: frames
    List.rev frames


let append (ledgerPath: string) (tx: Transaction) =
    use stream = new FileStream(ledgerPath,
                                FileMode.Append,
                                FileAccess.Write,
                                FileShare.None)
    writeFrame stream (serialize tx)
    stream.Flush()

let readAll (ledgerPath: string) : Transaction list =
    if not (File.Exists ledgerPath) then []
    else
        use stream = new FileStream(ledgerPath,
                                    FileMode.Open,
                                    FileAccess.Read,
                                    FileShare.Read)
        readAllFrames stream |> List.map deserialize

let tip (ledgerPath: string) : Transaction option =
    readAll ledgerPath |> List.tryLast

let findById (ledgerPath: string) (id: TxId) : Transaction option =
    readAll ledgerPath |> List.tryFind (fun tx -> tx.TxId = id)

let walkBack (ledgerPath: string) (startId: TxId) : Transaction list =
    let allById =
        readAll ledgerPath
        |> List.map (fun tx -> tx.TxId, tx)
        |> Map.ofList
    let rec go (id: TxId) acc =
        match Map.tryFind id allById with
        | None    -> acc
        | Some tx ->
            match tx.ParentId with
            | None        -> tx :: acc
            | Some parent -> go parent (tx :: acc)
    go startId [] |> List.rev

let resolveTree (ledgerPath: string) (txId: TxId) : Map<string, BlobId> =
    walkBack ledgerPath txId
    |> List.rev
    |> List.fold (fun tree tx ->
        tx.Changes |> List.fold (fun t fc ->
            match fc.NewBlob with
            | Some blob -> Map.add fc.Path blob t
            | None      -> Map.remove fc.Path t  
        ) tree
    ) Map.empty

let count (ledgerPath: string) : int =
    readAll ledgerPath |> List.length
