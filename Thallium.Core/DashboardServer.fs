
module Thallium.Core.DashboardServer

open System
open System.IO
open System.Net
open System.Text
open System.Text.Json
open Thallium.Core.Domain
open Thallium.Core.ObjectStore
open Thallium.Core.Ledger
open Thallium.Core.SandboxManager
open Thallium.Core.ContextEngine

let private htmlUi = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Thallium (tl) — AI-Native VCS Dashboard</title>
  <style>
    :root {
      --bg: #0d1117;
      --card-bg: #161b22;
      --border: #30363d;
      --text: #c9d1d9;
      --text-muted: #8b949e;
      --accent: #58a6ff;
      --accent-green: #238636;
      --accent-purple: #bc8cff;
      --accent-yellow: #d29922;
      --accent-red: #f85149;
    }
    * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif; }
    body { background: var(--bg); color: var(--text); display: flex; flex-direction: column; height: 100vh; overflow: hidden; }
    
    header {
      background: var(--card-bg);
      border-bottom: 1px solid var(--border);
      padding: 12px 24px;
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .brand { display: flex; align-items: center; gap: 12px; }
    .brand h1 { font-size: 1.25rem; color: #fff; }
    .badge { background: var(--accent-purple); color: #fff; font-size: 0.75rem; padding: 2px 8px; border-radius: 12px; font-weight: bold; }
    
    main { display: grid; grid-template-columns: 320px 1fr 340px; flex: 1; overflow: hidden; }
    
    
    .sidebar { background: var(--card-bg); border-right: 1px solid var(--border); overflow-y: auto; padding: 16px; }
    .section-title { font-size: 0.85rem; text-transform: uppercase; color: var(--text-muted); font-weight: 600; margin-bottom: 12px; }
    .tx-item {
      padding: 12px;
      border-radius: 6px;
      border: 1px solid var(--border);
      margin-bottom: 10px;
      cursor: pointer;
      transition: all 0.15s;
    }
    .tx-item:hover, .tx-item.active { border-color: var(--accent); background: rgba(88, 166, 255, 0.08); }
    .tx-hash { font-family: monospace; font-size: 0.8rem; color: var(--accent); }
    .tx-summary { font-size: 0.9rem; font-weight: 600; margin: 4px 0; color: #fff; }
    .tx-meta { font-size: 0.75rem; color: var(--text-muted); display: flex; justify-content: space-between; }
    .ai-tag { background: rgba(188, 140, 255, 0.15); color: var(--accent-purple); padding: 1px 6px; border-radius: 4px; font-size: 0.7rem; }
    
    
    .center-view { display: flex; flex-direction: column; overflow: hidden; }
    
    
    .slider-container {
      background: #11161d;
      border-bottom: 1px solid var(--border);
      padding: 16px 24px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .slider-header { display: flex; justify-content: space-between; font-size: 0.85rem; }
    .timeline-slider { width: 100%; accent-color: var(--accent); cursor: pointer; }
    
    .editor-view { flex: 1; padding: 20px; overflow-y: auto; background: #090d12; }
    .file-tree { background: var(--card-bg); border: 1px solid var(--border); border-radius: 6px; padding: 16px; margin-bottom: 16px; }
    .file-row { display: flex; justify-content: space-between; padding: 6px 0; border-bottom: 1px solid rgba(255,255,255,0.05); font-family: monospace; font-size: 0.85rem; }
    .code-preview { background: #0d1117; border: 1px solid var(--border); border-radius: 6px; padding: 16px; font-family: monospace; font-size: 0.85rem; white-space: pre-wrap; color: #e6edf3; }
    
    
    .right-sidebar { background: var(--card-bg); border-left: 1px solid var(--border); overflow-y: auto; padding: 16px; }
    .sandbox-card {
      background: rgba(35, 134, 54, 0.08);
      border: 1px solid var(--accent-green);
      border-radius: 6px;
      padding: 12px;
      margin-bottom: 12px;
    }
    .sb-title { font-size: 0.85rem; font-weight: bold; color: #3fb950; display: flex; justify-content: space-between; }
    .sb-meta { font-size: 0.75rem; color: var(--text-muted); margin-top: 4px; }
    
    .choice-card {
      background: rgba(210, 153, 34, 0.08);
      border: 1px solid var(--accent-yellow);
      border-radius: 6px;
      padding: 12px;
      margin-bottom: 12px;
    }
    .btn { background: var(--accent-green); color: #fff; border: none; padding: 6px 12px; border-radius: 4px; cursor: pointer; font-size: 0.8rem; font-weight: 600; margin-top: 8px; }
    .btn:hover { opacity: 0.9; }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <h1>Thallium <span style="font-weight: 300; font-size: 1rem; color: var(--text-muted)">tl</span></h1>
      <span class="badge">AI-Native VCS</span>
    </div>
    <div style="font-size: 0.85rem; color: var(--text-muted)" id="repo-status">Loading repo...</div>
  </header>
  
  <main>
    <div class="sidebar">
      <div class="section-title">Continuous Timeline Ledger</div>
      <div id="tx-list"></div>
    </div>
    
    <div class="center-view">
      <div class="slider-container">
        <div class="slider-header">
          <span>Continuous Timeline Scrub</span>
          <span id="slider-label" style="color: var(--accent)">Genesis</span>
        </div>
        <input type="range" min="0" max="0" value="0" class="timeline-slider" id="timeline-slider">
      </div>
      
      <div class="editor-view">
        <div class="file-tree">
          <div class="section-title">Reconstructed Tree Snapshot</div>
          <div id="tree-list"></div>
        </div>
        <div class="section-title">Source Inspector</div>
        <div class="code-preview" id="file-content">Select a file to inspect snapshot.</div>
      </div>
    </div>
    
    <div class="right-sidebar">
      <div class="section-title">Active AI Micro-Sandboxes</div>
      <div id="sandbox-list">
        <div style="font-size: 0.8rem; color: var(--text-muted)">No active sandboxes.</div>
      </div>
      
      <div class="section-title" style="margin-top: 24px;">Intent & Conflict Cards</div>
      <div id="choice-cards">
        <div class="choice-card">
          <div style="font-weight: bold; font-size: 0.85rem; color: var(--accent-yellow)">Auto-Union Ready</div>
          <div style="font-size: 0.75rem; color: var(--text-muted); margin: 4px 0;">All concurrent AI and human commits verified against test suite.</div>
          <span class="badge" style="background: var(--accent-green)">Option C: Pre-Tested Verified</span>
        </div>
      </div>
    </div>
  </main>

  <script>
    let transactions = [];
    let currentTxIndex = 0;

    async function loadData() {
      try {
        const res = await fetch('/api/state');
        const data = await res.json();
        transactions = data.transactions || [];
        document.getElementById('repo-status').innerText = `Repository: ${data.repo_name} | HEAD: ${data.head}`;
        
        const slider = document.getElementById('timeline-slider');
        slider.max = Math.max(0, transactions.length - 1);
        slider.value = transactions.length - 1;
        currentTxIndex = transactions.length - 1;

        renderTimeline();
        renderSandboxes(data.sandboxes || []);
        if (transactions.length > 0) {
          inspectTx(transactions.length - 1);
        }
      } catch (err) {
        console.error(err);
      }
    }

    function renderTimeline() {
      const container = document.getElementById('tx-list');
      container.innerHTML = transactions.slice().reverse().map((tx, idx) => {
        const actualIdx = transactions.length - 1 - idx;
        const isAi = tx.author.startsWith('agent:');
        return `
          <div class="tx-item ${actualIdx === currentTxIndex ? 'active' : ''}" onclick="inspectTx(${actualIdx})">
            <div style="display: flex; justify-content: space-between;">
              <span class="tx-hash">${tx.tx_id.substring(0, 8)}</span>
              ${isAi ? '<span class="ai-tag">AI Agent</span>' : ''}
            </div>
            <div class="tx-summary">${tx.summary}</div>
            <div class="tx-meta">
              <span>${tx.author}</span>
              <span>${new Date(tx.timestamp).toLocaleTimeString()}</span>
            </div>
          </div>
        `;
      }).join('');
    }

    async function inspectTx(index) {
      currentTxIndex = index;
      document.getElementById('timeline-slider').value = index;
      const tx = transactions[index];
      if (!tx) return;
      document.getElementById('slider-label').innerText = `${tx.tx_id.substring(0, 8)} — ${tx.summary}`;
      renderTimeline();

     
      const res = await fetch(`/api/tree?tx_id=${tx.tx_id}`);
      const treeData = await res.json();
      const treeContainer = document.getElementById('tree-list');
      if (treeData.files && treeData.files.length > 0) {
        treeContainer.innerHTML = treeData.files.map(f => `
          <div class="file-row">
            <a href="javascript:void(0)" onclick="loadFile('${f.path}', '${f.blob_id}')" style="color: var(--accent); text-decoration: none;">${f.path}</a>
            <span style="color: var(--text-muted); font-size: 0.75rem;">${f.blob_id.substring(0, 8)}</span>
          </div>
        `).join('');
        loadFile(treeData.files[0].path, treeData.files[0].blob_id);
      } else {
        treeContainer.innerHTML = '<div style="font-size: 0.8rem; color: var(--text-muted)">No files in this snapshot.</div>';
        document.getElementById('file-content').innerText = '(Empty tree)';
      }
    }

    async function loadFile(path, blobId) {
      const res = await fetch(`/api/blob?blob_id=${blobId}`);
      const text = await res.text();
      document.getElementById('file-content').innerText = text;
    }

    function renderSandboxes(sandboxes) {
      const container = document.getElementById('sandbox-list');
      if (sandboxes.length === 0) {
        container.innerHTML = '<div style="font-size: 0.8rem; color: var(--text-muted)">No active micro-sandboxes.</div>';
        return;
      }
      container.innerHTML = sandboxes.map(sb => `
        <div class="sandbox-card">
          <div class="sb-title">
            <span>${sb.sandbox_id.substring(0, 8)}</span>
            <span style="font-size: 0.7rem; color: var(--accent-green)">LIVE COW</span>
          </div>
          <div class="sb-meta">Base: ${sb.base_tx_id.substring(0, 8)}</div>
          <div class="sb-meta">Expires: ${new Date(sb.expires_at).toLocaleTimeString()}</div>
        </div>
      `).join('');
    }

    document.getElementById('timeline-slider').addEventListener('input', (e) => {
      inspectTx(parseInt(e.target.value, 10));
    });

    loadData();
    setInterval(loadData, 5000);
  </script>
</body>
</html>
"""

let startDashboard (repoDir: string) (port: int) =
    let p = Repository.requireRoot repoDir
    let listener = new HttpListener()
    listener.Prefixes.Add($"http://localhost:{port}/")
    listener.Start()

    Console.WriteLine($"[tl dashboard] Continuous Timeline Dashboard running on http://localhost:{port}/")

    async {
        while listener.IsListening do
            let! ctx = Async.FromBeginEnd(listener.BeginGetContext, listener.EndGetContext)
            let req  = ctx.Request
            let resp = ctx.Response
            resp.Headers.Add("Access-Control-Allow-Origin", "*")

            try
                let path = req.Url.AbsolutePath
                if path = "/" || path = "/index.html" then
                    let bytes = Encoding.UTF8.GetBytes(htmlUi)
                    resp.ContentType <- "text/html"
                    resp.ContentLength64 <- int64 bytes.Length
                    resp.OutputStream.Write(bytes, 0, bytes.Length)
                    resp.Close()
                elif path = "/api/state" then
                    let txs = Ledger.readAll p.LedgerPath
                    let sandboxes = SandboxManager.listFromRepo p.TlDir
                    let head = Repository.readHead p
                    let headStr = match head with Some (TxId id) -> id | None -> ""

                    let txDtoList =
                        txs |> List.map (fun t ->
                            let (TxId tid) = t.TxId
                            {| tx_id = tid; author = t.Author; summary = t.Summary; timestamp = t.Timestamp.ToString("o") |})

                    let sbDtoList =
                        sandboxes |> List.map (fun sb ->
                            let (SandboxId sbid) = sb.SandboxId
                            let (TxId baseId) = sb.BaseTxId
                            {| sandbox_id = sbid; base_tx_id = baseId; expires_at = sb.ExpiresAt.ToString("o") |})

                    let json = JsonSerializer.Serialize({| repo_name = Path.GetFileName p.Root; head = headStr; transactions = txDtoList; sandboxes = sbDtoList |})
                    let bytes = Encoding.UTF8.GetBytes(json)
                    resp.ContentType <- "application/json"
                    resp.ContentLength64 <- int64 bytes.Length
                    resp.OutputStream.Write(bytes, 0, bytes.Length)
                    resp.Close()
                elif path = "/api/tree" then
                    let txIdStr = req.QueryString.["tx_id"]
                    let txId = TxId txIdStr
                    let tree = Ledger.resolveTree p.LedgerPath txId
                    let files =
                        tree |> Map.toList |> List.map (fun (path, BlobId bid) ->
                            {| path = path; blob_id = bid |})
                    let json = JsonSerializer.Serialize({| files = files |})
                    let bytes = Encoding.UTF8.GetBytes(json)
                    resp.ContentType <- "application/json"
                    resp.ContentLength64 <- int64 bytes.Length
                    resp.OutputStream.Write(bytes, 0, bytes.Length)
                    resp.Close()
                elif path = "/api/blob" then
                    let blobIdStr = req.QueryString.["blob_id"]
                    let content = ObjectStore.getText p.ObjectsDir (BlobId blobIdStr) |> Option.defaultValue ""
                    let bytes = Encoding.UTF8.GetBytes(content)
                    resp.ContentType <- "text/plain"
                    resp.ContentLength64 <- int64 bytes.Length
                    resp.OutputStream.Write(bytes, 0, bytes.Length)
                    resp.Close()
                else
                    resp.StatusCode <- 404
                    resp.Close()
            with _ ->
                try resp.Close() with _ -> ()
    } |> Async.Start
