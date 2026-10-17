"""Shared response helpers for the SDK transport contract servers."""
import json
import sys


def send_json(handler, status, payload, headers=None):
    """Send a JSON response with an explicit Content-Length.

    BaseHTTPRequestHandler speaks HTTP/1.0 and sends no Content-Length or
    Connection header by default. Godot's HTTPClient then treats the
    connection as keep-alive with no body and returns an empty body, so every
    response must declare its length like the real server does.
    """
    body = payload if isinstance(payload, bytes) else json.dumps(payload).encode()
    handler.send_response(status)
    handler.send_header("Content-Type", "application/json")
    for name, value in (headers or {}).items():
        handler.send_header(name, value)
    handler.send_header("Content-Length", str(len(body)))
    handler.end_headers()
    handler.wfile.write(body)


def report_rejection(message):
    """Print a contract mismatch at once, so it is visible even if the runner stops early."""
    print(f"contract mismatch: {message}", file=sys.stderr, flush=True)
