
module Thallium.Core.ObjectStore

open System
open System.IO
open System.Security.Cryptography
open Thallium.Core.Domain


let private sha256hex (data: byte[]) : string =
    use h = SHA256.Create()
    h.ComputeHash(data)
    |> Array.map (fun b -> b.ToString("x2"))
    |> String.concat ""

let private blobPath (objectsDir: string) (BlobId hash) =
    let prefix = hash.[0..1]
    let rest   = hash.[2..]
    Path.Combine(objectsDir, prefix, rest)


let put (objectsDir: string) (data: byte[]) : BlobId =
    let hash   = sha256hex data
    let id     = BlobId hash
    let target = blobPath objectsDir id
    if not (File.Exists target) then
        Directory.CreateDirectory(Path.GetDirectoryName(target)) |> ignore
        File.WriteAllBytes(target, data)
    id

let get (objectsDir: string) (id: BlobId) : byte[] option =
    let path = blobPath objectsDir id
    if File.Exists path then Some (File.ReadAllBytes path)
    else None

let getText (objectsDir: string) (id: BlobId) : string option =
    get objectsDir id |> Option.map Text.Encoding.UTF8.GetString

let putText (objectsDir: string) (text: string) : BlobId =
    put objectsDir (Text.Encoding.UTF8.GetBytes text)

let exists (objectsDir: string) (id: BlobId) : bool =
    blobPath objectsDir id |> File.Exists

let listAll (objectsDir: string) : BlobId seq =
    if not (Directory.Exists objectsDir) then Seq.empty
    else
        Directory.EnumerateFiles(objectsDir, "*", SearchOption.AllDirectories)
        |> Seq.map (fun f ->
            let prefix = Path.GetFileName(Path.GetDirectoryName(f))
            let rest   = Path.GetFileName(f)
            BlobId (prefix + rest))
