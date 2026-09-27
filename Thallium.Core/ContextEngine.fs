// SPDX-License-Identifier: MPL-2.0

module Thallium.Core.ContextEngine

open System
open System.IO
open System.Text.RegularExpressions
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger


module private Patterns =
   
    let fsharpLet    = Regex(@"^let\s+(rec\s+)?(?<name>\w+)", RegexOptions.Multiline)
    let fsharpType   = Regex(@"^type\s+(?<name>\w+)", RegexOptions.Multiline)
    let fsharpModule = Regex(@"^module\s+(?<name>[\w.]+)", RegexOptions.Multiline)
   
    let csharpMethod = Regex(@"(?:public|private|protected|internal|static|async).*?\s+(?<name>\w+)\s*\(", RegexOptions.Multiline)
    let csharpClass  = Regex(@"class\s+(?<name>\w+)", RegexOptions.Multiline)
    let rustFn       = Regex(@"^(?:pub\s+)?fn\s+(?<name>\w+)", RegexOptions.Multiline)
    let goFunc       = Regex(@"^func\s+(?<name>\w+)", RegexOptions.Multiline)
    let pyDef        = Regex(@"^def\s+(?<name>\w+)|^class\s+(?<name>\w+)", RegexOptions.Multiline)

let private patternsForExt (ext: string) : Regex list =
    match ext.ToLowerInvariant() with
    | ".fs" | ".fsi" | ".fsx" -> [Patterns.fsharpLet; Patterns.fsharpType; Patterns.fsharpModule]
    | ".cs"                   -> [Patterns.csharpMethod; Patterns.csharpClass]
    | ".rs"                   -> [Patterns.rustFn]
    | ".go"                   -> [Patterns.goFunc]
    | ".py"                   -> [Patterns.pyDef]
    | _                       -> [Patterns.csharpMethod; Patterns.fsharpLet]

let private kindForPattern (r: Regex) =
    if r.ToString().Contains("class") then "type"
    elif r.ToString().Contains("module") then "module"
    elif r.ToString().Contains("type") then "type"
    else "function"

let private extractSymbols (filePath: string) (text: string) : (string * string * int * int * string) list =
    let lines    = text.Split('
')
    let ext      = Path.GetExtension(filePath)
    let patterns = patternsForExt ext

    let hits =
        patterns |> List.collect (fun pat ->
            pat.Matches(text)
            |> Seq.cast<Match>
            |> Seq.map (fun m ->
                let name = m.Groups.["name"].Value
                let kind = kindForPattern pat
                let startLine = text.[..m.Index].Split('
').Length
                name, kind, startLine)
            |> Seq.toList)
        |> List.distinctBy (fun (name, _, startLine) -> name, startLine)
        |> List.sortBy (fun (_, _, startLine) -> startLine)

   
    hits |> List.mapi (fun i (name, kind, startLine) ->
        let endLine =
            if i + 1 < hits.Length then
                let (_, _, nextStart) = hits.[i+1]
                nextStart - 1
            else
                lines.Length
        let body =
            let s = max 0 (startLine - 1)
            let e = min (lines.Length - 1) (endLine - 1)
            lines.[s..e] |> String.concat "
"
        name, kind, startLine, endLine, body)


let private parseSymbolRef (ref: string) : string option * string =
    let parts = ref.Split("::")
    if parts.Length > 1 then
        let hint = parts.[0..parts.Length-2] |> String.concat "/"
        Some hint, parts.[parts.Length-1]
    else
        None, ref

let private matchesHint (hint: string option) (path: string) =
    match hint with
    | None   -> true
    | Some h ->
        let norm = path.Replace('\', '/').ToLowerInvariant()
        norm.Contains(h.ToLowerInvariant())


let fetchContext
    (objectsDir  : string)
    (ledgerPath  : string)
    (targetSymbol: string)
    : ContextPayload =

    let tip = tip ledgerPath

    match tip with
    | None ->
        { TargetSymbol  = targetSymbol
          Definition    = None
          Dependencies  = []
          RecentHistory = []
          Warning       = Some "Repository has no commits yet." }
    | Some tx ->
        let tree = resolveTree ledgerPath tx.TxId
        let (hint, simpleName) = parseSymbolRef targetSymbol

       
        let found =
            tree
            |> Map.toSeq
            |> Seq.filter (fun (path, _) ->
                matchesHint hint path &&
                (Path.GetExtension path |> patternsForExt |> List.isEmpty |> not))
            |> Seq.tryPick (fun (path, bid) ->
                match getText objectsDir bid with
                | None -> None
                | Some text ->
                    extractSymbols path text
                    |> List.tryFind (fun (name, _, _, _, _) ->
                        name.Equals(simpleName, StringComparison.OrdinalIgnoreCase))
                    |> Option.map (fun (name, kind, sl, el, body) ->
                        { Name      = name
                          Kind      = kind
                          FilePath  = path
                          StartLine = sl
                          EndLine   = el
                          Body      = body }))

       
        let dependencies =
            match found with
            | None -> []
            | Some def ->
               
                let allSymbols =
                    tree
                    |> Map.toSeq
                    |> Seq.collect (fun (path, bid) ->
                        match getText objectsDir bid with
                        | None -> Seq.empty
                        | Some text ->
                            extractSymbols path text
                            |> Seq.map (fun (n,k,sl,el,b) ->
                                { Name=n; Kind=k; FilePath=path; StartLine=sl; EndLine=el; Body=b }))
                    |> Seq.toList

                allSymbols
                |> List.filter (fun sym ->
                    sym.Name <> def.Name &&
                    def.Body.Contains(sym.Name))
                |> List.truncate 20 

       
        let history =
            match found with
            | None -> []
            | Some def ->
                readAll ledgerPath
                |> List.filter (fun t ->
                    t.Changes |> List.exists (fun fc -> fc.Path = def.FilePath))
                |> List.rev
                |> List.truncate 10

        { TargetSymbol  = targetSymbol
          Definition    = found
          Dependencies  = dependencies
          RecentHistory = history
          Warning       = if found.IsNone then Some $"Symbol '{targetSymbol}' not found in any tracked file." else None }

let getFileContent
    (objectsDir : string)
    (ledgerPath : string)
    (filePath   : string)
    : string option =
    let tip = tip ledgerPath
    match tip with
    | None -> None
    | Some tx ->
        let tree = resolveTree ledgerPath tx.TxId
        match Map.tryFind filePath tree with
        | None     -> None
        | Some bid -> getText objectsDir bid
