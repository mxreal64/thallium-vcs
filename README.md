<p align="center">
  <img src="assets/logo.png" alt="Thallium Logo" width="120" />
</p>

<h1 align="center">Thallium</h1>
<p align="center"><strong>AI-Native Version Control System</strong></p>

<p align="center">
  <a href="#quickstart">Quickstart</a> ·
  <a href="#cli-reference">CLI</a> ·
  <a href="#json-rpc-api">JSON-RPC API</a> ·
  <a href="#architecture">Architecture</a> ·
  <a href="#benchmarks">Benchmarks</a>
</p>

---

## Why Thallium?

Git was designed for humans writing code in terminals. AI agents don't work that way.

| Problem | Git | Thallium |
|---|---|---|
| Branching cost | Full working-tree copy | Copy-on-write sandboxes (microseconds) |
| Commit metadata | Author + message only | Typed intent tags (`AIGenerated`, `Move`, `Refactor`, …) |
| Multi-agent writes | Race conditions, no isolation | Isolated sandboxes, atomic commit |
| Querying history | Parse `git log` text | Structured JSON-RPC API |
| Merging AI changes | Line-level, conflict-heavy | Set-union merge + SandboxRunner validation |
| Context for AI | None built-in | `fetchContext` — symbol graph extraction |
| Git interop | — | Import, export, clone, sync |

Thallium is a **layered** VCS — it can sit on top of Git (via `tl git`) or **replace it entirely for AI-heavy workflows**.

---

## Quickstart

### install

```bash
git clone https://github.com/mxreal64/thallium
cd thallium
bash build.sh

# until i hv a pkgbuild or sumn
alias tl="<path to clone>/thallium/tl"
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

# Tag as AI-generated
tl commit -a -m "Implement feature X" --author "agent:<ai>"
```

### Inspect history

```bash
tl log
tl log --limit 5
tl diff <tx_a_id> <tx_b_id>
tl status
```

### Sandboxes (isolated workspaces)

```bash
# Create a sandbox from HEAD
tl sandbox create

# List sandboxes
tl sandbox list

# Drop a sandbox
tl sandbox drop <sandbox-id>
```

### Merge

```bash
tl merge <tx_id_a> <tx_id_b>
# Auto-merges when safe; presents Choice Cards for conflicts
```

### Git interop

```bash
tl git clone https://github.com/user/repo .
tl git import /path/to/existing/.git
tl git export /path/to/output.git
tl git sync              # bidirectional sync with companion .git
```

### Other commands

```bash
tl checkout <tx_id>      # restore working tree to a past transaction
tl rollback              # undo HEAD
tl gc                    # prune unreachable blobs
tl test                  # run project test suite in a sandbox
tl dashboard             # open embedded web dashboard
tl serve                 # start JSON-RPC daemon on :7373
```

---

## CLI Reference

| Command | Description |
|---|---|
| `tl init <path>` | Initialize a new Thallium repository |
| `tl commit [-a] [-m msg] [files…]` | Create a new transaction |
| `tl status` | Show staged/unstaged changes |
| `tl log [--limit N]` | List transactions (newest first) |
| `tl diff <tx_a> <tx_b>` | Show line-level diff between two transactions |
| `tl checkout <tx_id>` | Restore working tree to a transaction |
| `tl rollback` | Revert HEAD to previous transaction |
| `tl merge <tx_a> <tx_b>` | 3-tier merge (auto + Choice Cards) |
| `tl sandbox create` | Create a COW sandbox from HEAD |
| `tl sandbox list` | List all sandboxes |
| `tl sandbox drop <id>` | Drop a sandbox |
| `tl git import <path>` | Import a Git repo's history |
| `tl git export <path>` | Export to a bare Git repo |
| `tl git clone <url> <dest>` | Clone a remote Git repo into Thallium |
| `tl git sync` | Bidirectional sync with companion `.git` |
| `tl gc` | Garbage-collect unreachable blobs |
| `tl test` | Run test suite in an isolated sandbox |
| `tl dashboard` | Launch embedded dashboard (default: :7374) |
| `tl serve` | Start JSON-RPC daemon (default: :7373) |

---

## JSON-RPC API

Start the daemon: `tl serve` (or `tl-rpc`).  
Endpoint: `http://localhost:7373/rpc`  
Protocol: JSON-RPC 2.0 over HTTP POST.

examples:

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

Atomically commits a sandbox as a new transaction.

```json
{"jsonrpc":"2.0","id":4,"method":"tl_sandbox_commit","params":{"sandboxId":"sb-a1b2c3","message":"AI agent edit","author":"agent:claude"}}
```

```json
{"jsonrpc":"2.0","id":4,"result":{"txId":"2c94e243..."}}
```

### `tl_sandbox_drop`

Drops a sandbox and reclaims its overlay.

```json
{"jsonrpc":"2.0","id":5,"method":"tl_sandbox_drop","params":{"sandboxId":"sb-a1b2c3","paths":["src/new.rs"]}}
```

```json
{"jsonrpc":"2.0","id":5,"result":{"ok":true}}
```

### `tl_log`

Returns recent transactions as structured JSON.

```json
{"jsonrpc":"2.0","id":6,"method":"tl_log","params":{"limit":10}}
```

```json
{"jsonrpc":"2.0","id":6,"result":{"transactions":[{"id":"2c94e243","message":"Add logo","author":"agent:deepmind","intents":["AIGenerated"],"timestamp":"..."}]}}
```

---

## how

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
          │  ObjectStore  ←── SHA-256 blobs        │
          │  Ledger       ←── append-only DAG log  │
          │  SandboxMgr   ←── COW overlays         │
          │  IntentResolver ← Levenshtein + AST    │
          │  MergeEngine  ←── 3-tier merge         │
          │  DiffEngine   ←── Myers/LCS line diff  │
          │  ContextEngine ← symbol graph          │
          │  GitBridge    ←── import/export/sync   │
          │  GarbageCollector                      │
          │  DashboardServer ← embedded SPA        │
          └────────────────────────────────────────┘
                       │
          ┌────────────▼────────────┐
          │    .tl/ (repo store)    │
          │  objects/  ledger.bin   │
          │  HEAD      index.json   │
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

Thallium is not "Git but better." It's a **programmable VCS substrate** — the diff, merge, and history APIs are first-class, designed to be called by agents, not humans.

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
