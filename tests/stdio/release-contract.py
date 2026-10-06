"""Host-free MCP release contract: real stdio server, disposable data root, no CAD target.

Run: python tests/stdio/release-contract.py --server <built Bimwright.Ipt.Server.dll> --output <directory>
The per-tool sweep exercises error/empty-state handling, not live Inventor success coverage.
"""
import argparse
import asyncio
import json
import os
import re
import time
from pathlib import Path
import tempfile


async def run_mode(server, flags, output, label, sweep=False):
    with tempfile.TemporaryDirectory(prefix="ipt-release-stdio-") as folder:
        root = Path(folder).resolve()
        assert root.is_relative_to(Path(tempfile.gettempdir()).resolve())
        env = {k: v for k, v in os.environ.items() if not k.startswith("BIMWRIGHT_")}
        process = await asyncio.create_subprocess_exec(
            "dotnet", str(server), "--local-app-data", str(root), *flags,
            stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE, env=env, limit=4 * 1024 * 1024)
        errors = asyncio.create_task(process.stderr.read())
        sequence = 0

        async def request(method, params):
            nonlocal sequence
            sequence += 1
            process.stdin.write((json.dumps({"jsonrpc": "2.0", "id": sequence,
                                            "method": method, "params": params}) + "\n").encode())
            await process.stdin.drain()
            while True:
                line = await asyncio.wait_for(process.stdout.readline(), 30)
                assert line, f"Server exited during {method}"
                message = json.loads(line)
                if message.get("id") == sequence:
                    return message

        try:
            init = await request("initialize", {"protocolVersion": "2025-03-26", "capabilities": {},
                                               "clientInfo": {"name": "ipt-release-contract", "version": "1"}})
            assert "result" in init, init
            instructions = init['result']['instructions']
            assert 'inventor_send_code is enabled by default' in instructions
            assert '--disable-send-code' in instructions and '--read-only' in instructions
            assert 'PLUGIN_ENABLE_SEND_CODE' not in instructions, 'Obsolete add-in opt-in guidance'
            version = json.loads((Path(__file__).resolve().parents[2] / 'server.json').read_text('utf-8'))['version']
            assert init['result']['serverInfo']['version'].removesuffix('.0') == version.removesuffix('.0')
            process.stdin.write(b'{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
            await process.stdin.drain()
            listed = await request("tools/list", {})
            tools = listed["result"]["tools"]
            by_name = {x["name"]: x for x in tools}
            assert len(by_name) == len(tools)
            if "--read-only" in flags:
                assert all(t.get("annotations", {}).get("readOnlyHint") is True for t in tools)
                denied = await request("tools/call", {"name": "inventor_save_document", "arguments": {}})
                assert "error" in denied or denied.get("result", {}).get("isError") is True
            if "inventor_send_code" in by_name:
                assert not by_name["inventor_send_code"].get("annotations"), by_name["inventor_send_code"]
            for tool in tools:
                assert tool.get("description"), tool["name"]
                if tool["name"] != "inventor_send_code":
                    assert set(tool.get("annotations", {})) >= {"readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint"}, tool["name"]
                    if tool['annotations']['destructiveHint']:
                        assert re.search(r'undo|undone|rollback|roll back', tool['description'], re.I), tool['name']
                else:
                    assert not tool.get('annotations'), 'send_code must have no permission annotations'
                    assert tool.get('_meta', {}).get('anthropic/requiresUserInteraction') is not True
            targets = await request("tools/call", {"name": "inventor_list_available_targets", "arguments": {}}) if "inventor_list_available_targets" in by_name else None
            if targets:
                assert json.loads(targets["result"]["content"][0]["text"]) == []
            if "inventor_list_baked_tools" in by_name:
                baked = await request("tools/call", {"name": "inventor_list_baked_tools", "arguments": {}})
                assert json.loads(baked["result"]["content"][0]["text"]) == {"tools": []}
                assert not list(root.rglob("bake.db")), "Read-only inspection created a database"
            calls = []
            if sweep:
                def value(schema):
                    if isinstance(schema, bool): return {}
                    if "enum" in schema: return schema["enum"][0]
                    kind = schema.get("type", "string")
                    if isinstance(kind, list): kind = next((x for x in kind if x != "null"), "string")
                    return {"string": "release_fixture", "integer": 1, "number": 1.0,
                            "boolean": False, "array": [], "object": {}}.get(kind, "release_fixture")
                for tool in tools:
                    schema = tool["inputSchema"]
                    args = {k: value(schema["properties"][k]) for k in schema.get("required", [])}
                    started = time.perf_counter()
                    reply = await request("tools/call", {"name": tool["name"], "arguments": args})
                    elapsed_ms = round((time.perf_counter() - started) * 1000, 3)
                    # Tool/handler validation must return an MCP result; an uncaught exception fails.
                    assert "result" in reply, (tool["name"], reply)
                    assert reply["result"].get("content"), tool["name"]
                    result_bytes = len(json.dumps(reply['result'], ensure_ascii=False, separators=(',', ':')).encode('utf-8'))
                    calls.append({"tool": tool["name"], "scenario": "no-target-or-empty-store",
                                  "status": "pass", "isError": reply["result"].get("isError", False),
                                  "duration_ms": elapsed_ms, "result_bytes": result_bytes,
                                  "estimated_tokens": (result_bytes + 3) // 4})
            await asyncio.sleep(0.05)
            logs = list(root.rglob("*calls*.jsonl"))
            assert bool(logs) == ("--enable-call-log" in flags), (label, logs)
            record = {"label": label, "flags": flags, "initialize": init["result"], "tools": tools,
                      "count": len(tools), "per_tool_checks": calls, "call_log_files": [x.name for x in logs]}
            (output / (label + ".json")).write_text(json.dumps(record, ensure_ascii=False, indent=2), encoding="utf-8")
            return record
        finally:
            process.stdin.close()
            try: await asyncio.wait_for(process.wait(), 8)
            except asyncio.TimeoutError:
                process.kill()
                await process.wait()
            (output / (label + ".stderr.log")).write_bytes(await errors)


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--server", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    modes = [("default", [], True), ("all", ["--toolsets", "all"], False),
             ("code-off", ["--disable-send-code"], False),
             ("read-only", ["--toolsets", "all", "--read-only"], False),
             ("query", ["--toolsets", "query"], False),
             ("log-on", ["--enable-call-log", "--toolsets", "meta"], False)]
    records = []
    for label, flags, sweep in modes:
        records.append(await run_mode(args.server.resolve(), flags, args.output, label, sweep))
        print(label, records[-1]["count"], "PASS", flush=True)
    all_tools = records[1]["tools"]
    expected = {x["name"] for x in all_tools if x.get("annotations", {}).get("readOnlyHint") is True}
    actual = {x["name"] for x in records[3]["tools"]}
    assert actual == expected, (actual - expected, expected - actual)
    assert "inventor_send_code" not in {x["name"] for x in records[2]["tools"]}
    repo = Path(__file__).resolve().parents[2]
    declared = json.loads((repo / 'docs/testing/readonly-tools.json').read_text('utf-8'))['tools']
    assert sorted(declared) == sorted(expected), 'Read-only inventory differs from the built server'
    for readme in repo.glob('README*.md'):
        text = readme.read_text('utf-8')
        block = text.split('<!-- BEGIN GENERATED READONLY -->')[1].split('<!-- END GENERATED READONLY -->')[0]
        listed = re.findall(r'mcp__ipt-mcp__(inventor_\w+)', block)
        assert sorted(listed) == sorted(expected), readme.name
        assert 'docs/benchmarks/README.md' in text, readme.name
    print("PASS: stdio contracts, exact annotation/read-only parity and per-tool host-free sweep")


if __name__ == "__main__":
    asyncio.run(main())
