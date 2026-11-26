"""Checks the packages in the real language server of the VS Code C# extension.

Starts Microsoft.CodeAnalysis.LanguageServer over stdio, opens a project that references the
ShkoOnline.EarlyBound package, and asks for completions at given positions. It proves that the
source generator and the completion provider are loaded from the NuGet package by an IDE host,
which unit tests cannot.

Usage:
    python3 eng/verify-language-server.py <server> <project.csproj> <source.cs> <log-dir> <check> ...

    <server>   the Microsoft.CodeAnalysis.LanguageServer executable, found in the C# extension's
               folder: ~/.vscode/extensions/ms-dotnettools.csharp-*/.roslyn/
    <check>    '<text with | at the caret>=><expected entry>[,<expected entry>...]'
               The text has to occur in the source file.

Environment:
    LSP_WARMUP=1   ask for diagnostics first, as an editor does when a file opens. Needed for
                   completions that depend on generated types.
    LSP_DIAG=1     also print hover, diagnostics and server log messages.

Example (after "dotnet pack -c Release" and "dotnet build samples/Samples.slnx"):
    LSP_WARMUP=1 python3 eng/verify-language-server.py "$SERVER" \
        samples/Contoso.Plugins/Contoso.Plugins.csproj samples/Contoso.Plugins/ProjectDueDatePlugin.cs /tmp/lsp-logs \
        'new QueryExpression("|sho_project")=>account,sho_project' \
        'project.|Sho_DueDate == null=>Sho_DueDate'
"""
import json, os, subprocess, sys, threading, time, pathlib, queue

server, project, source, logdir = sys.argv[1:5]
checks = [a.split("=>", 1) for a in sys.argv[5:]]

proc = subprocess.Popen(
    [server, "--stdio", "--logLevel", "Information", "--extensionLogDirectory", logdir],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

inbox = queue.Queue()
def reader():
    out = proc.stdout
    while True:
        headers = {}
        line = out.readline()
        if not line:
            inbox.put(None); return
        while line and line.strip():
            k, _, v = line.decode().partition(":")
            headers[k.strip().lower()] = v.strip()
            line = out.readline()
        body = out.read(int(headers.get("content-length", "0")))
        inbox.put(json.loads(body))
threading.Thread(target=reader, daemon=True).start()

def send(msg):
    data = json.dumps(msg).encode()
    proc.stdin.write(b"Content-Length: %d\r\n\r\n" % len(data) + data)
    proc.stdin.flush()

next_id = [0]
notifications = []
def request(method, params, timeout=180):
    next_id[0] += 1
    rid = next_id[0]
    send({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            msg = inbox.get(timeout=1)
        except queue.Empty:
            continue
        if msg is None:
            raise RuntimeError("server exited")
        if "id" in msg and "method" in msg:            # server -> client request: answer politely
            result = [None] * len(msg.get("params", {}).get("items", [])) if msg["method"] == "workspace/configuration" else None
            send({"jsonrpc": "2.0", "id": msg["id"], "result": result})
            continue
        if msg.get("id") == rid:
            if "error" in msg:
                raise RuntimeError(f"{method}: {msg['error']}")
            return msg.get("result")
        notifications.append(msg)
    raise TimeoutError(method)

def notify(method, params):
    send({"jsonrpc": "2.0", "method": method, "params": params})

def wait_for(method, timeout=240):
    deadline = time.time() + timeout
    while time.time() < deadline:
        for n in notifications:
            if n.get("method") == method:
                notifications.remove(n); return n
        try:
            msg = inbox.get(timeout=1)
        except queue.Empty:
            continue
        if msg is None:
            raise RuntimeError("server exited")
        if "id" in msg and "method" in msg:
            result = [None] * len(msg.get("params", {}).get("items", [])) if msg["method"] == "workspace/configuration" else None
            send({"jsonrpc": "2.0", "id": msg["id"], "result": result})
            continue
        notifications.append(msg)
    raise TimeoutError(method)

root = pathlib.Path(project).parent
uri = lambda p: pathlib.Path(p).resolve().as_uri()
init = request("initialize", {
    "processId": os.getpid(),
    "rootUri": uri(root),
    "workspaceFolders": [{"uri": uri(root), "name": root.name}],
    "capabilities": {
        "textDocument": {"completion": {"completionItem": {"snippetSupport": False, "resolveSupport": {"properties": ["documentation", "detail", "additionalTextEdits", "textEdit"]}},
                                        "completionList": {"itemDefaults": ["editRange", "commitCharacters", "data"]}}},
        "workspace": {"configuration": True, "workspaceFolders": True},
    },
})
caps = init["capabilities"]
print("server:", init.get("serverInfo"))
print("completion trigger characters:", "".join(caps.get("completionProvider", {}).get("triggerCharacters", [])))
notify("initialized", {})
notify("project/open", {"projects": [uri(project)]})
wait_for("workspace/projectInitializationComplete")
print("project loaded")

text = pathlib.Path(source).read_text(encoding="utf-8")
ok = True
version = 0
opened = False
if os.environ.get("LSP_WARMUP"):
    # An editor asks for diagnostics as soon as a file opens, which runs the source generators.
    notify("textDocument/didOpen", {"textDocument": {"uri": uri(source), "languageId": "csharp", "version": version, "text": text}})
    opened = True
    request("textDocument/diagnostic", {"textDocument": {"uri": uri(source)}, "identifier": "DocumentCompilerSemantic"}, timeout=180)
    print("warmed up")
for marker, expected in checks:
    before, _, after = marker.partition("|")
    idx = text.find(before + after)
    if idx < 0:
        print(f"MARKER NOT FOUND: {marker!r}"); ok = False; continue
    offset = idx + len(before)
    line = text.count("\n", 0, offset)
    col = offset - (text.rfind("\n", 0, offset) + 1)
    if not opened:
        notify("textDocument/didOpen", {"textDocument": {"uri": uri(source), "languageId": "csharp", "version": version, "text": text}})
        opened = True
    labels = []
    # Roslyn loads completion providers from analyzer references in the background; the first requests can miss them.
    for attempt in range(40):
        result = request("textDocument/completion", {"textDocument": {"uri": uri(source)}, "position": {"line": line, "character": col}, "context": {"triggerKind": 1}})
        items = (result or {}).get("items", []) if isinstance(result, dict) else (result or [])
        labels = [i["label"] for i in items]
        if all(e in [l.split(" = ")[0] for l in labels] for e in expected.split(",")):
            break
        time.sleep(0.5)
    labels = [l.split(" = ")[0] for l in labels]
    found = [e for e in expected.split(",") if e in labels]
    status = "OK  " if len(found) == len(expected.split(",")) else "FAIL"
    if status == "FAIL": ok = False
    print(f"{status} {marker!r}: {len(labels)} items; expected {expected}; first: {labels[:8]}")

if os.environ.get("LSP_DIAG"):
    idx = text.find("ToEntity<Sho_Project>") + len("ToEntity<Sho_")
    line = text.count("\n", 0, idx); col = idx - (text.rfind("\n", 0, idx) + 1)
    for attempt in range(3):
        hover = request("textDocument/hover", {"textDocument": {"uri": uri(source)}, "position": {"line": line, "character": col}})
        print("hover Sho_Project:", json.dumps(hover)[:300] if hover else None)
        if hover: break
        time.sleep(3)
    for ident in ("DocumentCompilerSemantic", "syntax", None):
        try:
            params = {"textDocument": {"uri": uri(source)}}
            if ident: params["identifier"] = ident
            diag = request("textDocument/diagnostic", params, timeout=120)
            items = (diag or {}).get("items", [])
            print("diagnostics[%s]: %d" % (ident, len(items)))
            for d in items[:8]:
                print("  ", d.get("code"), d["range"]["start"], d.get("message")[:160])
        except Exception as e:
            print("diagnostic request failed:", ident, e)
    logs = [n for n in notifications if n.get("method") in ("window/logMessage", "window/showMessage")]
    print("log messages:", len(logs))
    for n in logs:
        m = n["params"].get("message", "")
        if any(k in m.lower() for k in ("error", "exception", "fail", "generator", "analyzer", "earlybound")):
            print("  LOG:", m[:400].replace("\n", " | "))
try:
    request("shutdown", None, timeout=10); notify("exit", None)
except Exception:
    pass
proc.kill()
sys.exit(0 if ok else 1)
