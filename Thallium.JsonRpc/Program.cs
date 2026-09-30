

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.FSharp.Core;
using Thallium.Core;

var port   = 7373;
var repoDir = Directory.GetCurrentDirectory();

for (int i = 0; i < args.Length; i++) {
    if (args[i] == "--port" && i + 1 < args.Length) port = int.Parse(args[i + 1]);
    if (args[i] == "--repo" && i + 1 < args.Length) repoDir = args[i + 1];
}

var paths = Repository.requireRoot(repoDir);

Console.WriteLine($"[tl-rpc] Listening on http://localhost:{port}/rpc");
Console.WriteLine($"[tl-rpc] Repository: {paths.Root}");

var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();


while (true) {
    var ctx = await listener.GetContextAsync();
    _ = Task.Run(() => HandleRequest(ctx, paths));
}

static async Task HandleRequest(HttpListenerContext ctx, Repository.RepoPaths paths) {
    ctx.Response.ContentType = "application/json";
    ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");

    if (ctx.Request.HttpMethod == "OPTIONS") {
        ctx.Response.StatusCode = 200;
        ctx.Response.Close();
        return;
    }

    if (ctx.Request.Url?.AbsolutePath != "/rpc") {
        await WriteError(ctx, null, -32600, "Use POST /rpc");
        return;
    }

    string body;
    using (var sr = new StreamReader(ctx.Request.InputStream))
        body = await sr.ReadToEndAsync();

    JsonObject? req = null;
    string? id      = null;
    try {
        req = JsonNode.Parse(body)?.AsObject();
        id  = req?["id"]?.ToString();
    }
    catch {
        await WriteError(ctx, null, -32700, "Parse error");
        return;
    }

    var method = req?["method"]?.ToString();
    var prms   = req?["params"]?.AsObject();

    try {
        JsonObject result = method switch {
            "tl_fetch_context"   => await FetchContext(paths, prms),
            "tl_create_sandbox"  => await CreateSandbox(paths, prms),
            "tl_sandbox_write"   => await SandboxWrite(paths, prms),
            "tl_sandbox_commit"  => await SandboxCommit(paths, prms),
            "tl_sandbox_drop"    => await SandboxDrop(paths, prms),
            "tl_log"             => await GetLog(paths, prms),
            _                    => throw new RpcException(-32601, $"Method not found: {method}")
        };

        var resp = new JsonObject {
            ["jsonrpc"] = "2.0",
            ["id"]      = id,
            ["result"]  = result
        };
        await WriteJson(ctx, resp.ToJsonString());
    }
    catch (RpcException ex) {
        await WriteError(ctx, id, ex.Code, ex.Message);
    }
    catch (Exception ex) {
        await WriteError(ctx, id, -32603, ex.Message);
    }
}


static Task<JsonObject> FetchContext(Repository.RepoPaths paths, JsonObject? prms) {
    var symbol = prms?["target_symbol"]?.ToString()
                 ?? throw new RpcException(-32602, "Missing target_symbol");

    var payload = ContextEngine.fetchContext(paths.ObjectsDir, paths.LedgerPath, symbol);

    var result = new JsonObject {
        ["target_symbol"] = payload.TargetSymbol,
        ["found"]         = FSharpOption<Domain.SymbolDef>.get_IsSome(payload.Definition)
    };

    if (FSharpOption<Domain.SymbolDef>.get_IsSome(payload.Definition)) {
        var def = payload.Definition.Value;
        result["definition"] = new JsonObject {
            ["name"]       = def.Name,
            ["kind"]       = def.Kind,
            ["file_path"]  = def.FilePath,
            ["start_line"] = def.StartLine,
            ["end_line"]   = def.EndLine,
            ["body"]       = def.Body
        };
    }

    var deps = new System.Text.Json.Nodes.JsonArray();
    foreach (var dep in payload.Dependencies)
        deps.Add(new JsonObject { ["name"] = dep.Name, ["kind"] = dep.Kind, ["file"] = dep.FilePath });
    result["dependencies"] = deps;

    var hist = new System.Text.Json.Nodes.JsonArray();
    foreach (var tx in payload.RecentHistory)
        hist.Add(new JsonObject {
            ["tx_id"]     = ((Domain.TxId)tx.TxId).Item,
            ["author"]    = tx.Author,
            ["summary"]   = tx.Summary,
            ["timestamp"] = tx.Timestamp.ToString("o")
        });
    result["recent_history"] = hist;

    if (FSharpOption<string>.get_IsSome(payload.Warning))
        result["warning"] = payload.Warning.Value;

    return Task.FromResult(result);
}

static Task<JsonObject> CreateSandbox(Repository.RepoPaths paths, JsonObject? prms) {
    var baseState = prms?["base_state"]?.ToString();
    var minutes   = prms?["lifetime_minutes"]?.GetValue<int>() ?? 10;

    Domain.TxId baseTxId;
    if (string.IsNullOrEmpty(baseState) || baseState == "HEAD") {
        var head = Repository.readHead(paths);
        if (FSharpOption<Domain.TxId>.get_IsNone(head))
            throw new RpcException(-32603, "Repository has no HEAD.");
        baseTxId = head.Value;
    }
    else {
        baseTxId = Domain.TxId.NewTxId(baseState);
    }

    var sb      = SandboxManager.create(baseTxId, TimeSpan.FromMinutes(minutes));
    SandboxManager.saveToRepo(paths.TlDir, sb);
    var sbIdStr = ((Domain.SandboxId)sb.SandboxId).Item;

    return Task.FromResult(new JsonObject {
        ["sandbox_id"]  = sbIdStr,
        ["base_tx_id"]  = ((Domain.TxId)sb.BaseTxId).Item,
        ["expires_at"]  = sb.ExpiresAt.ToString("o"),
        ["created_at"]  = sb.CreatedAt.ToString("o")
    });
}

static Task<JsonObject> SandboxWrite(Repository.RepoPaths paths, JsonObject? prms) {
    var sbId    = prms?["sandbox_id"]?.ToString() ?? throw new RpcException(-32602, "Missing sandbox_id");
    var path    = prms?["path"]?.ToString()        ?? throw new RpcException(-32602, "Missing path");
    var content = prms?["content"]?.ToString()     ?? "";

    var id = Domain.SandboxId.NewSandboxId(sbId);
    var sb = SandboxManager.loadFromRepo(paths.TlDir, id);
    if (FSharpOption<Domain.SandboxState>.get_IsNone(sb))
        throw new RpcException(-32603, "Sandbox not found or expired.");

    var bytes   = Encoding.UTF8.GetBytes(content);
    var updated = SandboxManager.writeFile(paths.ObjectsDir, sb.Value, path, bytes);
    SandboxManager.saveToRepo(paths.TlDir, updated);

    return Task.FromResult(new JsonObject {
        ["ok"]          = true,
        ["sandbox_id"]  = sbId,
        ["path"]        = path,
        ["bytes_written"] = bytes.Length
    });
}

static Task<JsonObject> SandboxCommit(Repository.RepoPaths paths, JsonObject? prms) {
    var sbId    = prms?["sandbox_id"]?.ToString() ?? throw new RpcException(-32602, "Missing sandbox_id");
    var author  = prms?["author"]?.ToString()      ?? "agent:unknown";
    var message = prms?["message"]?.ToString()     ?? "";

    var id = Domain.SandboxId.NewSandboxId(sbId);
    var sb = SandboxManager.loadFromRepo(paths.TlDir, id);
    if (FSharpOption<Domain.SandboxState>.get_IsNone(sb))
        throw new RpcException(-32603, "Sandbox not found or expired.");

    // Extract optional AgentMetadata
    Domain.AgentMetadata? agentData = null;
    var agentNode = prms?["agent_metadata"] as JsonObject;
    if (agentNode != null) {
        var model = agentNode["model"]?.ToString() ?? "agent:unspecified";
        var prompt = agentNode["prompt"]?.ToString() ?? "";
        var reasoning = agentNode["reasoning_summary"]?.ToString();
        var tokens = agentNode["tokens_used"]?.GetValue<int>();
        var temp = agentNode["temperature"]?.GetValue<double>();
        var sess = agentNode["session_id"]?.ToString();

        agentData = new Domain.AgentMetadata(
            model,
            prompt,
            reasoning != null ? FSharpOption<string>.Some(reasoning) : FSharpOption<string>.None,
            tokens.HasValue ? FSharpOption<int>.Some(tokens.Value) : FSharpOption<int>.None,
            temp.HasValue ? FSharpOption<double>.Some(temp.Value) : FSharpOption<double>.None,
            sess != null ? FSharpOption<string>.Some(sess) : FSharpOption<string>.None
        );
    }

    var tx = SandboxManager.commit(paths.ObjectsDir, paths.LedgerPath, sb.Value, author, message);

    if (agentData != null) {
        tx = new Domain.Transaction(
            tx.TxId,
            tx.ParentId,
            tx.Timestamp,
            tx.Author,
            tx.Summary,
            tx.Changes,
            tx.FromSandbox,
            FSharpOption<Domain.AgentMetadata>.Some(agentData)
        );
    }

    // Policy check
    var policyOpt = PolicyEngine.loadPolicy(paths.TlDir);
    if (FSharpOption<PolicyEngine.RepoPolicy>.get_IsSome(policyOpt)) {
        var valRes = PolicyEngine.validateTransaction(policyOpt.Value, tx);
        if (valRes.IsError) {
            var errors = valRes.ErrorValue;
            throw new RpcException(-32000, $"Policy violation: {string.Join("; ", errors)}");
        }
    }

    Repository.writeHead(paths, tx.TxId);
    SandboxManager.removeFromRepo(paths.TlDir, id);

    return Task.FromResult(new JsonObject {
        ["tx_id"]    = ((Domain.TxId)tx.TxId).Item,
        ["summary"]  = tx.Summary,
        ["changes"]  = tx.Changes.Length
    });
}

static Task<JsonObject> SandboxDrop(Repository.RepoPaths paths, JsonObject? prms) {
    var sbId = prms?["sandbox_id"]?.ToString() ?? throw new RpcException(-32602, "Missing sandbox_id");
    SandboxManager.removeFromRepo(paths.TlDir, Domain.SandboxId.NewSandboxId(sbId));
    return Task.FromResult(new JsonObject { ["ok"] = true });
}

static Task<JsonObject> GetLog(Repository.RepoPaths paths, JsonObject? prms) {
    var limit = prms?["limit"]?.GetValue<int>() ?? 20;
    var txs   = Ledger.readAll(paths.LedgerPath);

    var arr = new System.Text.Json.Nodes.JsonArray();
    foreach (var tx in System.Linq.Enumerable.TakeLast(txs, limit)) {
        arr.Add(new JsonObject {
            ["tx_id"]     = ((Domain.TxId)tx.TxId).Item,
            ["parent_id"] = FSharpOption<Domain.TxId>.get_IsSome(tx.ParentId)
                            ? ((Domain.TxId)tx.ParentId.Value).Item : null,
            ["author"]    = tx.Author,
            ["summary"]   = tx.Summary,
            ["timestamp"] = tx.Timestamp.ToString("o"),
            ["changes"]   = tx.Changes.Length
        });
    }

    return Task.FromResult(new JsonObject { ["transactions"] = arr, ["total"] = txs.Length });
}


static async Task WriteJson(HttpListenerContext ctx, string json) {
    var bytes = Encoding.UTF8.GetBytes(json);
    ctx.Response.StatusCode        = 200;
    ctx.Response.ContentLength64   = bytes.Length;
    await ctx.Response.OutputStream.WriteAsync(bytes);
    ctx.Response.Close();
}

static async Task WriteError(HttpListenerContext ctx, string? id, int code, string message) {
    var err = new JsonObject {
        ["jsonrpc"] = "2.0",
        ["id"]      = id,
        ["error"]   = new JsonObject { ["code"] = code, ["message"] = message }
    };
    await WriteJson(ctx, err.ToJsonString());
}

class RpcException(int code, string message) : Exception(message) {
    public int Code { get; } = code;
}
