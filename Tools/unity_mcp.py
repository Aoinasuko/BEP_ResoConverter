"""Local Unity MCP client/stdio bridge. Credentials stay in Unity's descriptor."""
import argparse
import json
import os
from pathlib import Path
import sys
import urllib.request


def descriptor(project):
    expected = os.path.normcase(os.path.abspath(project))
    for path in (Path(os.environ['LOCALAPPDATA']) / 'UnityMCP' / 'instances').glob('*.json'):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        actual = os.path.normcase(os.path.abspath(data['projectPath']))
        if actual in (expected, os.path.join(expected, 'assets')):
            return data
    raise RuntimeError('Open the Unity project with the Unity MCP package: ' + project)


def request(project, payload):
    data = descriptor(project)
    req = urllib.request.Request(data['mcpUrl'], data=json.dumps(payload).encode(), headers={
        'Authorization': 'Bearer ' + data['token'],
        'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream'
    })
    with urllib.request.urlopen(req, timeout=60) as response:
        body = response.read().decode('utf-8')
    if not body.strip():
        return None
    if body.startswith('event:') or body.startswith('data:'):
        messages = [line[5:].strip() for line in body.splitlines() if line.startswith('data:')]
        return json.loads(messages[-1]) if messages else None
    return json.loads(body)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', default='E:/Avatar/Test')
    parser.add_argument('--stdio', action='store_true')
    parser.add_argument('--list', action='store_true')
    parser.add_argument('--tool')
    parser.add_argument('--args', default='{}')
    parser.add_argument('--args-file')
    args = parser.parse_args()
    if args.stdio:
        for line in sys.stdin:
            payload = None
            try:
                payload = json.loads(line)
                result = request(args.project, payload)
                if result is not None:
                    print(json.dumps(result, ensure_ascii=True), flush=True)
            except Exception as exc:
                if payload is not None and 'id' in payload:
                    print(json.dumps({'jsonrpc': '2.0', 'id': payload['id'], 'error': {
                        'code': -32603, 'message': str(exc)}}), flush=True)
        return
    request(args.project, {'jsonrpc':'2.0', 'id':1, 'method':'initialize', 'params':{
        'protocolVersion':'2025-03-26', 'capabilities':{},
        'clientInfo':{'name':'BEP-ResoConverter', 'version':'1.0'}}})
    request(args.project, {'jsonrpc':'2.0', 'method':'notifications/initialized'})
    params = json.loads(Path(args.args_file).read_text(encoding='utf-8-sig') if args.args_file else args.args)
    result = request(args.project, {'jsonrpc':'2.0', 'id':2,
        'method':'tools/list' if args.list else 'tools/call',
        'params':{} if args.list else {'name':args.tool, 'arguments':params}})
    output = result.get('result', result) if result else None
    if isinstance(output, dict) and 'structuredContent' in output:
        output = output['structuredContent']
    print(json.dumps(output, ensure_ascii=True, indent=2))


if __name__ == '__main__':
    main()
