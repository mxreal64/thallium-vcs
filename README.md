<p align="center">
  <img src="assets/logo.png" alt="Thallium Logo" width="120" />
</p>

<h1 align="center">Thallium</h1>
<p align="center"><strong>AI-Native Version Control System</strong></p>

<p align="center">
  <a href="#why-thallium">Why Thallium?</a> ·
  <a href="#quickstart">Quickstart</a> ·
  <a href="#cli-reference">CLI Reference</a> ·
  <a href="#speculative-tournaments">Tournaments</a> ·
  <a href="#policy-engine">Policy Engine</a> ·
  <a href="#json-rpc-api">JSON-RPC API</a> ·
  <a href="#architecture">Architecture</a> ·
  <a href="#benchmarks">Benchmarks</a>
</p>

---

## Why Thallium?

Git was designed for humans writing code in terminals. AI agents don't work that way.

| Problem | Git | Thallium |
|---|---|---|
| Branching cost | Full working-tree copy | Copy-on-write micro-sandboxes (microseconds) |
| Commit metadata | Author + message only | Typed intent tags (`AIGenerated`, `Move`, `Refactor`, …) + Agent Provenance (model, tokens, reasoning) |
| Multi-agent writes | Race conditions, no isolation | Isolated COW sandboxes, atomic commit ledger |
| Agent competition | Serial manual review | Speculative parallel tournaments (`tl tournament`) with automated test scoring |
| Safety & Guardrails | Hook scripts / external CI | Built-in repository policy engine (`tl policy`) for immutable paths & human approvals |
| Querying history | Parse `git log` text | Structured JSON-RPC 2.0 daemon |
| Merging AI changes | Line-level, conflict-heavy | Set-union merge + pre-tested Choice Cards (`MergeEngine`) |
| Context for AI | None built-in | `fetchContext` — symbol graph extraction |
| Remote collaboration | Branch push/pull | Decentralized transaction & blob sync (`tl remote`, `tl push`, `tl pull`) |
| Git interop | — | Import, export, clone, sync |

Thallium is a **layered** VCS — it can sit on top of Git (via `tl git`) or **replace it entirely for AI-heavy workflows**.

---

## Quickstart

### Install & Build

```bash
git clone https://github.com/mxreal64/thallium-vcs
cd thallium-vcs
bash build.sh

# until i hv a pkgbuild or sumn
alias tl="<path to clone>/thallium-vcs/tl"
```

### Init a repo

```bash
cd my-project
tl init .
```

### Stage and commit

```bash
# Stage everything (respects .tlignore; auto-ignores bin/, obj/, .git/)
tl commit -a -m "Initial commit"

# Stage specific files
tl commit <files> -m "Refactor parser"

# Tag as AI-generated with agent provenance
tl commit -a -m "Implement feature X" --author "agent:claude-3-7-sonnet"
```

### Speculative Multi-Agent Tournaments

```bash
# Evaluate competing candidate sandboxes in parallel and auto-commit the winner
tl tournament sb_alpha sb_beta sb_gamma -c "dotnet test" --auto-commit
```

### Safety & Policy Guardrails

```bash
# Initialize security policy (.tl/policy.json)
tl policy init

# Inspect active rules
tl policy show

# Validate working tree against policy rules
tl policy check --author "agent:claude-3-7-sonnet"
```

### Sandboxes (isolated workspaces)

```bash
# Create an ephemeral sandbox from HEAD
tl sandbox create

# List sandboxes
tl sandbox list

# Drop a sandbox
tl sandbox drop <sandbox-id>
```

### Inspect history & Merge

```bash
tl log
tl log --oneline -n 5
tl status
tl merge <tx_id>         # Auto-merges when safe; presents pre-tested Choice Cards for conflicts
```

### Remote Sync & Git Interop

```bash
# Remote sync
tl remote add origin https://thallium.internal/repo
tl push origin
tl pull origin

# Git interop
tl git clone https://github.com/user/repo .
tl git import /path/to/existing/.git
tl git export /path/to/output.git
tl git sync              # bidirectional sync with companion .git
```

### Other commands

```bash
tl checkout <tx_id>      # restore working tree to a past transaction
tl rollback <tx_id>      # non-destructive revert to previous transaction
tl gc                    # prune unreachable blobs and reclaim disk space
tl test                  # run project test suite in an isolated sandbox overlay
tl dashboard             # open embedded Continuous Timeline Web Dashboard (:7374)
tl serve                 # start JSON-RPC daemon on :7373
```

---

## CLI Reference

| Command | Description |
|---|---|
| `tl init [dir]` | Initialize a new Thallium repository |
| `tl commit [-a] [-m msg] [files…]` | Create a new atomic transaction with semantic intent tagging |
| `tl status` | Show working tree modifications vs HEAD |
| `tl log [--oneline] [-n N]` | Display continuous transaction timeline with author badges |
| `tl checkout <tx_id>` | Restore working tree to a historical transaction |
| `tl rollback <tx_id>` | Append a non-destructive revert transaction |
| `tl merge <tx_id>` | Logical set-union merge with pre-tested Choice Cards |
| `tl sandbox create` | Create an ephemeral zero-copy COW sandbox from HEAD |
| `tl sandbox list` | List all active sandboxes |
| `tl sandbox drop <id>` | Drop a sandbox and reclaim its overlay |
| `tl tournament <sandboxes...>` | Run parallel speculative tournament evaluation across candidate sandboxes |
| `tl policy <init\|show\|check>` | Manage repository security policies and agent permissions |
| `tl remote <add\|remove\|list>` | Manage configured remote sync repositories |
| `tl push [remote]` | Push missing transactions and content-addressed blobs |
| `tl pull [remote]` | Fetch and fast-forward transactions from remote |
| `tl git import <path>` | Import a Git repository's history into Thallium |
| `tl git export <path>` | Export Thallium transactions to a Git repository |
| `tl git clone <url> <dest>` | Clone a remote Git repo into Thallium |
| `tl git sync` | Bidirectional sync with companion `.git` |
| `tl gc` | Prune orphaned blobs and optimize repository storage |
| `tl test [-t target] [-c cmd]` | Run test suite in an isolated sandbox overlay |
| `tl dashboard` / `tl ui` | Launch embedded Continuous Timeline Web Dashboard (default: `:7374`) |
| `tl serve [--port N] [--repo path]` | Start JSON-RPC 2.0 daemon (default: `:7373`) |

---

## Speculative Tournaments

When multiple agents (or prompts) generate candidate solutions for a complex issue, `tl tournament` benchmarks them concurrently:

$$\text{Score} = \frac{1000.0}{\max(\text{DurationMs}, 10.0)} \times \frac{1.0}{1.0 + 0.1 \times \text{FileChanges}}$$

```bash
tl tournament sb_alpha sb_beta sb_gamma -c "dotnet test" --auto-commit
```

Outputs a formatted leaderboard with ranking, pass/fail status, execution time, file churn, and automatically promotes the winning candidate to `HEAD`.

---

## Policy Engine

Protect sensitive infrastructure and secrets against unintended autonomous agent writes:

- **Protected Paths**: Immutable file patterns (`.env*`, `infra/**`, `secrets/**`, `*.pem`, `*.key`).
- **Human-Required Paths**: Disallow agent commits without human approval (`auth/**`, `security/**`, `billing/**`).
- **Disallow Agent Deletions**: Prevents agents from performing accidental mass file deletions.
- **Max Files Per Commit**: Bounds bulk modifications per transaction.

---

## JSON-RPC API

Start the daemon: `tl serve` (or `tl-rpc`).  
Endpoint: `http://localhost:7373/rpc`  
Protocol: JSON-RPC 2.0 over HTTP POST.

### `tl_fetch_context`

Extracts symbols and dependency graph for a set of files.

```json
{"jsonrpc":"2.0","id":1,"method":"tl_fetch_context","params":{"paths":["src/main.rs","src/lib.rs"]}}
```

```json
{"jsonrpc":"2.0","id":1,"result":{"symbols":[{"name":"parse","file":"src/lib.rs","line":14}],"dependencies":{"src/main.rs":["src/lib.rs"]}}}
```

### `tl_create_sandbox`

Creates an isolated COW sandbox from HEAD.

```json
{"jsonrpc":"2.0","id":2,"method":"tl_create_sandbox","params":{}}
```

```json
{"jsonrpc":"2.0","id":2,"result":{"sandboxId":"sb-a1b2c3"}}
```

### `tl_sandbox_write`

Writes a file into a sandbox.

```json
{"jsonrpc":"2.0","id":3,"method":"tl_sandbox_write","params":{"sandboxId":"sb-a1b2c3","path":"src/new.rs","content":"fn main() {}"}}
```

```json
{"jsonrpc":"2.0","id":3,"result":{"ok":true}}
```

### `tl_sandbox_commit`

Atomically commits a sandbox as a new transaction with agent decision provenance and policy validation.

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "tl_sandbox_commit",
  "params": {
    "sandboxId": "sb-a1b2c3",
    "message": "AI agent refactor",
    "author": "agent:claude-3-7-sonnet",
    "agent_metadata": {
      "agent_model": "claude-3-7-sonnet",
      "prompt_tokens": 1250,
      "completion_tokens": 420,
      "rationale": "Extracted duplicated auth helper to reduce cyclomatic complexity",
      "parent_plan": "task-auth-cleanup"
    }
  }
}
```

```json
{"jsonrpc":"2.0","id":4,"result":{"txId":"2c94e243..."}}
```

### `tl_sandbox_drop`

Drops a sandbox and reclaims its overlay.

```json
{"jsonrpc":"2.0","id":5,"method":"tl_sandbox_drop","params":{"sandboxId":"sb-a1b2c3"}}
```

```json
{"jsonrpc":"2.0","id":5,"result":{"ok":true}}
```

### `tl_log`

Returns recent transactions with agent metadata as structured JSON.

```json
{"jsonrpc":"2.0","id":6,"method":"tl_log","params":{"limit":10}}
```

```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "result": {
    "transactions": [
      {
        "id": "2c94e243",
        "message": "Add logo",
        "author": "agent:deepmind",
        "intents": ["AIGenerated"],
        "timestamp": "2026-09-30T12:00:00Z",
        "agent": {
          "model": "gemini-2.5-pro",
          "rationale": "Added SVG vector asset"
        }
      }
    ]
  }
}
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    User / AI Agent                      │
└──────────────────────┬──────────────────────────────────┘
                       │
          ┌────────────┴─────────────┐
          │                          │
   ┌──────▼──────┐           ┌───────▼────────┐
   │ Thallium.Cli│           │Thallium.JsonRpc│
   │   (C# CLI)  │           │   (C# daemon)  │
   │  tl <cmd>   │           │   :7373/rpc    │
   └──────┬──────┘           └───────┬────────┘
          │                          │
          └────────────┬─────────────┘
                       │
          ┌────────────▼───────────────────────────┐
          │           Thallium.Core (F#)           │
          │                                        │
          │  ObjectStore       ←── SHA-256 blobs   │
          │  Ledger            ←── append-only log │
          │  SandboxMgr        ←── COW overlays    │
          │  TournamentEngine  ←── parallel eval   │
          │  PolicyEngine      ←── security rules  │
          │  RemoteSync        ←── push/pull delta │
          │  MergeEngine       ←── 3-tier merge    │
          │  SandboxRunner     ←── test validation │
          │  ContextEngine     ←── symbol graph    │
          │  GitBridge         ←── import/export   │
          │  GarbageCollector  ←── storage compact │
          │  DashboardServer   ←── embedded SPA    │
          └────────────────────────────────────────┘
                       │
          ┌────────────▼────────────┐
          │    .tl/ (repo store)    │
          │  objects/    ledger.log │
          │  HEAD        policy.json│
          │  remotes.json           │
          │  sandboxes/*.json       │
          └─────────────────────────┘
```

**Why F# + C#?**  
The core engine (`Thallium.Core`) uses F# for its algebraic types, pattern matching on discriminated unions, and railway-oriented error handling — ideal for a domain-model-heavy storage engine (also cuz i found out F# exists). The CLI and RPC layers use C# for familiarity, `System.CommandLine`, and `Microsoft.AspNetCore` — both run on the same CLR with zero FFI overhead.

---

## Benchmarks

Measured on a single core.

| Operation | Throughput | Latency |
|---|---|---|
| Blob write (SHA-256 + store) | 31,446 ops/sec | 0.032 ms |
| Blob read | 172,413 ops/sec | 0.006 ms |
| Sandbox creation (COW) | 120,482/sec | 0.0083 ms |
| Ledger append (binary) | 5,900 tx/sec | 0.170 ms |
| DAG reconstruction (depth 2000) | — | 117 ms |
| Intent classification | 43,956 pairs/sec | 0.023 ms |
| 3-way merge | 12,346/sec | 0.081 ms |
| GC (6,000 blobs, 5,999 orphaned) | — | ~172 KB reclaimed |

---

## Prior Art

| Project | What it is | Thallium's distinction |
|---|---|---|
| [Jujutsu (jj)](https://github.com/jj-vcs/jj) | Git-compatible, anonymous branches | No AI API; no intent tagging; CLI-only |
| [Pijul](https://pijul.org) | Patch-theory VCS | No sandbox model; no AI integration |
| [Sapling](https://sapling-scm.com) | Meta's Git fork for engineers | Git-compatible shim, not AI-native |
| [Darcs](http://darcs.net) | Haskell patch-theory VCS | Unmaintained for scale; no JSON-RPC |

Thallium is not "Git but better." It's a **programmable VCS substrate** — the diff, merge, tournament, policy, and history APIs are first-class, designed to be called by agents, not humans.

---

## Contributing

1. Fork the repo
2. `bash build.sh` — runs tests before packaging
3. Open a PR with your intent tag in the commit message (`[Refactor]`, `[Fix]`, etc.)

---

## License

copyright mxreal64, 2026

Thallium is licensed under the **Mozilla Public License 2.0**.

- Modifications to Thallium's source files must be shared back under MPL-2.0.
- Applications that *use* Thallium (via CLI or JSON-RPC) may be proprietary.
- AI agents integrating via `tl serve` are not subject to copyleft.

See [LICENSE](LICENSE) for the full text.
