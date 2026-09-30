/// Thallium.Core.PolicyEngine
/// Security and compliance policy guard to enforce repository constraints on human and agent commits.
module Thallium.Core.PolicyEngine

open System
open System.IO
open System.Text.Json
open System.Text.RegularExpressions
open Thallium.Core.Domain
open Thallium.Core.IntentResolver

type RepoPolicy =
    { ProtectedPaths       : string list
      MaxFilesPerCommit    : int option
      DisallowAgentDeletes : bool
      RequireHumanForPaths : string list }

let defaultPolicy : RepoPolicy =
    { ProtectedPaths       = [ ".env*"; "infra/**"; "secrets/**"; "*.pem"; "*.key" ]
      MaxFilesPerCommit    = Some 100
      DisallowAgentDeletes = true
      RequireHumanForPaths = [ "auth/**"; "security/**"; "billing/**" ] }

let private jsonOptions =
    let o = JsonSerializerOptions(WriteIndented = true)
    o.Converters.Add(System.Text.Json.Serialization.JsonFSharpConverter())
    o

/// Convert simple glob pattern (e.g. "infra/**", "*.env") into a Regex
let globToRegex (pattern: string) : Regex =
    let escaped = Regex.Escape(pattern.Replace('\\', '/'))
                       .Replace(@"\*\*", ".*")
                       .Replace(@"\*", "[^/]*")
                       .Replace(@"\?", ".")
    Regex($"^{escaped}$", RegexOptions.IgnoreCase)

/// Check if a given file path matches any pattern in a pattern list
let isPathMatching (patterns: string list) (path: string) : bool =
    let normPath = path.Replace('\\', '/')
    patterns
    |> List.exists (fun pat ->
        let r = globToRegex pat
        r.IsMatch(normPath))

/// Load policy from .tl/policy.json or return None
let loadPolicy (tlDir: string) : RepoPolicy option =
    let policyPath = Path.Combine(tlDir, "policy.json")
    if File.Exists policyPath then
        try
            let json = File.ReadAllText policyPath
            let policy = JsonSerializer.Deserialize<RepoPolicy>(json, jsonOptions)
            Some policy
        with _ -> None
    else
        None

/// Save policy to .tl/policy.json
let savePolicy (tlDir: string) (policy: RepoPolicy) : unit =
    let policyPath = Path.Combine(tlDir, "policy.json")
    let json = JsonSerializer.Serialize(policy, jsonOptions)
    File.WriteAllText(policyPath, json)

let private isAiAuthor (author: string) : bool =
    author.StartsWith("agent:", StringComparison.OrdinalIgnoreCase) ||
    author.StartsWith("ai:", StringComparison.OrdinalIgnoreCase) ||
    author.Contains("[AI]")

/// Validate a transaction against repository policy rules.
/// Returns Ok () if valid, or Error (list of violation reasons).
let validateTransaction (policy: RepoPolicy) (tx: Transaction) : Result<unit, string list> =
    let violations = System.Collections.Generic.List<string>()
    let isAgent = isAiAuthor tx.Author

    // 1. Max files per commit
    match policy.MaxFilesPerCommit with
    | Some maxFiles when tx.Changes.Length > maxFiles ->
        violations.Add($"Commit exceeds maximum allowed files ({tx.Changes.Length} > {maxFiles}).")
    | _ -> ()

    for fc in tx.Changes do
        // 2. Protected paths (completely immutable without policy override)
        if isPathMatching policy.ProtectedPaths fc.Path then
            violations.Add($"Path '{fc.Path}' is protected by repository policy.")

        // 3. Human-required paths
        if isAgent && isPathMatching policy.RequireHumanForPaths fc.Path then
            violations.Add($"Path '{fc.Path}' requires human authorship; AI agent '{tx.Author}' cannot modify it.")

        // 4. Agent deletion restrictions
        if isAgent && policy.DisallowAgentDeletes && (fc.NewBlob.IsNone || fc.Intent = Remove) then
            violations.Add($"Agent '{tx.Author}' is forbidden from deleting file '{fc.Path}'.")

    if violations.Count = 0 then
        Ok ()
    else
        Error (List.ofSeq violations)
