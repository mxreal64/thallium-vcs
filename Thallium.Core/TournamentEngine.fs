/// Thallium.Core.TournamentEngine
/// Runs speculative parallel tournament evaluations across candidate sandboxes and commits the winning mutation.
module Thallium.Core.TournamentEngine

open System
open System.IO
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager
open Thallium.Core.SandboxRunner

type CandidateEvaluation =
    { SandboxId    : SandboxId
      BaseTxId     : TxId
      Passed       : bool
      ExitCode     : int
      DurationMs   : int64
      Score        : float
      FileChanges  : int
      CommandRun   : string
      OutputSample : string }

/// Calculate candidate score:
/// Passing test = 1000 base points.
/// Execution time penalty: -0.1 points per ms.
/// Code diff penalty: -5 points per changed file.
let private computeScore (passed: bool) (durationMs: int64) (changesCount: int) : float =
    if not passed then
        0.0
    else
        let basePoints = 1000.0
        let timePenalty = float durationMs * 0.05
        let filePenalty = float changesCount * 5.0
        max 1.0 (basePoints - timePenalty - filePenalty)

/// Run test suite across all candidate sandboxes in parallel and return ranked evaluations (highest score first).
let evaluateTournament
    (objectsDir  : string)
    (ledgerPath  : string)
    (candidates  : SandboxState list)
    (testCmdOpt  : string option)
    : CandidateEvaluation list =

    let asyncEvaluations =
        candidates
        |> List.map (fun sb ->
            async {
                let testResult = runTestInSandbox objectsDir ledgerPath sb testCmdOpt
                let changesCount = sb.Overlay.Count
                let score = computeScore testResult.Passed testResult.DurationMs changesCount
                let outputSample =
                    if testResult.StandardOut.Length > 200 then
                        testResult.StandardOut.Substring(0, 200) + "..."
                    else testResult.StandardOut

                return
                    { SandboxId    = sb.SandboxId
                      BaseTxId     = sb.BaseTxId
                      Passed       = testResult.Passed
                      ExitCode     = testResult.ExitCode
                      DurationMs   = testResult.DurationMs
                      Score        = score
                      FileChanges  = changesCount
                      CommandRun   = testResult.CommandRun
                      OutputSample = outputSample }
            })

    Async.RunSynchronously(Async.Parallel asyncEvaluations)
    |> Array.toList
    |> List.sortByDescending (fun eval -> eval.Score)

/// Evaluate candidates and automatically commit the winning sandbox to the ledger.
/// Returns Some (Transaction, CandidateEvaluation) or None if no candidates passed.
let autoCommitWinner
    (objectsDir  : string)
    (ledgerPath  : string)
    (tlDir       : string)
    (candidates  : SandboxState list)
    (testCmdOpt  : string option)
    (author      : string)
    (summaryMsg  : string)
    : (Transaction * CandidateEvaluation) option =

    let ranked = evaluateTournament objectsDir ledgerPath candidates testCmdOpt
    match ranked |> List.tryFind (fun r -> r.Passed) with
    | Some winnerEval ->
        let (SandboxId sbid) = winnerEval.SandboxId
        let shortSb = sbid.Substring(0, min 8 sbid.Length)
        let msg =
            if String.IsNullOrWhiteSpace summaryMsg then
                $"Tournament Winner: Sandbox {shortSb} (Score {winnerEval.Score:F1})"
            else summaryMsg
        let winnerSb = candidates |> List.find (fun sb -> sb.SandboxId = winnerEval.SandboxId)
        let tx = commit objectsDir ledgerPath winnerSb author msg
        // Drop other losing sandboxes if they are stored in repo
        for sb in candidates do
            removeFromRepo tlDir sb.SandboxId

        Some (tx, winnerEval)
    | None ->
        None
