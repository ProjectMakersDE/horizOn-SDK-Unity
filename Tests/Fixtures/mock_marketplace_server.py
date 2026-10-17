#!/usr/bin/env python3
"""Strict artificial Marketplace/escrow consumer contract; no real accounts or persistence."""
import copy
import datetime as dt
import json
import os
import threading
import time
from base64 import urlsafe_b64encode
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse
from uuid import UUID

PROJECT = "10000000-0000-4000-8000-000000001015"
SELLER = "20000000-0000-4000-8000-000000001015"
BUYER = "30000000-0000-4000-8000-000000001015"
OTHER = "30000000-0000-4000-8000-000000001016"
INSTANCE = "40000000-0000-4000-8000-000000001015"
LISTING = "50000000-0000-4000-8000-000000001015"
L2 = "50000000-0000-4000-8000-000000001016"
L3 = "50000000-0000-4000-8000-000000001017"
I2 = "40000000-0000-4000-8000-000000001016"
I3 = "40000000-0000-4000-8000-000000001017"
UNKNOWN = "60000000-0000-4000-8000-000000009999"
ENTRY = "message:70000000-0000-3000-8000-000000001015"
ROOT = "/api/v1/app/marketplace"
BASE = dt.datetime(2026, 10, 17, 12, tzinfo=dt.timezone.utc)
START = time.monotonic()
SAVED = {}
SAVED_RAW = {}
def iso(value): return value.isoformat(timespec="milliseconds").replace("+00:00", "Z")
def now(): return iso(BASE + dt.timedelta(seconds=time.monotonic() - START))
def stamp(): return iso(BASE)
def original(instance=INSTANCE):
    return {"instanceId": instance, "itemKey": "hat", "properties": {"power": 9007199254740991, "label": "雪/😀\0tail", "bound": True}, "equipped": False, "createdAt": stamp()}
def limits(lower=False):
    return {"openListingsPerPlayer": 1 if lower else 3000, "retainedListingsPerProject": 1 if lower else 12000, "receiptsPerPlayer": 1 if lower else 160, "historyPerPlayer": 1 if lower else 180, "projectWritesPerMinute": 1 if lower else 200, "playerWriteIntervalMillis": 1000, "minDurationSeconds": 300, "maxDurationSeconds": 604800, "terminalRetentionSeconds": 604800, "operationRetentionSeconds": 2592000, "queryCandidates": 256, "pageSize": 50}
def row(id=LISTING, instance=INSTANCE, status="active", revision=1, buyer=None):
    terminal = status in ("sold", "returned")
    return {"id": id, "revision": revision, "sellerId": SELLER, "buyerId": buyer, "returnOwnerId": SELLER, "item": None if terminal else original(instance), "itemReference": {"instanceId": instance, "itemKey": "hat"}, "currencyKey": "1coin.gold", "price": 101, "feeBps": 333, "feeAmount": 3, "sellerNet": 98, "status": status, "createdAt": stamp(), "expiresAt": iso(BASE + dt.timedelta(days=1)), "terminalAt": stamp() if terminal else None, "returnEntryId": ENTRY if status in ("return_delivered", "returned") else None, "ownerUnavailable": False}
def state(user=SELLER, revision=4, opened=None, history=None, lower=True):
    return {"state": {"userId": user, "revision": revision, "openListings": opened or [], "history": history or [], "operationWindow": {"serverTime": now(), "acceptAfter": iso(BASE - dt.timedelta(days=30))}, "moderation": {"banUntil": None, "reason": ""}, "limits": limits(lower)}, "serverTime": now()}
def large_state(lower=False):
    opened = [row(str(UUID(int=UUID("91000000-0000-4000-8000-000000000000").int + i)), str(UUID(int=UUID("92000000-0000-4000-8000-000000000000").int + i))) for i in range(2051)]
    history = [row(str(UUID(int=UUID("93000000-0000-4000-8000-000000000000").int + i)), str(UUID(int=UUID("94000000-0000-4000-8000-000000000000").int + i)), "sold", 2, OTHER) for i in range(151)]
    return state(opened=opened, history=history, lower=lower, revision=5 if lower else 4)
def inventory(user=SELLER, revision=10, instances=None):
    return {"userId": user, "revision": revision, "instances": instances or [], "limits": {"catalog": 6000, "inventory": 250, "receipts": 80}, "serverTime": now()}
def values(user=SELLER, revision=5):
    return {"userId": user, "day": "2026-10-17", "definitionVersion": 1, "revision": revision, "values": [{"key": "1coin.gold", "balance": 98 if user == SELLER else 0, "earnedToday": 98 if user == SELLER else 0, "dailyCap": None}], "operationWindow": {"serverTime": now(), "oldestAcceptedAt": iso(BASE - dt.timedelta(days=30))}}
def receipt(name):
    body = SAVED[name]; kind = name
    listing_id = LISTING if kind == "create" else L2 if kind == "buy" else L3
    instance = INSTANCE if kind == "create" else I2 if kind == "buy" else I3
    return {"operationId": body["operationId"], "issuedAt": body["issuedAt"], "kind": kind, "listingId": listing_id, "listingRevision": 1 if kind == "create" else 2, "recordedAt": stamp(), "itemReference": {"instanceId": instance, "itemKey": "hat"}, "currencyKey": "1coin.gold", "price": 101, "feeAmount": 3, "sellerNet": 98}
def written(name, replay=False):
    listing = row(status="sold", revision=2, buyer=OTHER) if name == "create" and replay else row() if name == "create" else row(L2, I2, "sold", 2, BUYER) if name == "buy" else row(L3, I3, "return_pending", 2)
    return {"receipt": receipt(name), "listing": listing, "replayed": replay, "serverTime": now()}
def inbox(claimed=False):
    return {"userId": SELLER, "cursor": "market-return-claimed" if claimed else "market-return", "revision": 2 if claimed else 1, "broadcastRevision": 1, "serverTime": now(), "pollAfterSeconds": 120, "unchanged": False, "entries": [{"id": ENTRY, "kind": "message", "type": "marketplace", "reference": L3, "textKey": "marketplace.item_return", "parameters": {}, "createdAt": stamp(), "expiresAt": iso(BASE - dt.timedelta(days=1)), "read": claimed, "claimed": claimed, "overdue": True, "attachment": {"values": [], "itemReturn": {"listingId": L3, "instanceId": I3, "itemKey": "hat"}}}], "counts": {"marketplace": {"total": 1, "unread": 0 if claimed else 1, "claimable": 0 if claimed else 1}}, "limits": {"targeted": 1, "broadcasts": 1, "freeTexts": 1, "freeTextBytes": 256, "freeTextRetentionDays": 7}}
def claim(replay=False): return {"entryId": ENTRY, "replayed": replay, "inbox": inbox(True), "playerValues": values(revision=6), "items": [{"instanceId": I3, "itemKey": "hat"}]}
def q(user=SELLER, **fields): return {"userId": [user], **{k: [str(v)] for k, v in fields.items()}}
CURSOR = urlsafe_b64encode(f"{PROJECT}|hat|1|200|101|{LISTING}".encode()).decode().rstrip("=")
def rule_page(after=False): return {"revision": 7, "rules": [{"itemKey": "wand" if after else "hat", "active": True, "minPrice": 1, "maxPrice": 200}], "nextCursor": None if after else "hat", "serverTime": now()}
def limit_view(): return {"limits": limits(), "config": {"revision": 7, "enabled": True, "currencyKey": "1coin.gold", "feeBps": 333}, "capabilities": {"marketplace": True, "realtime": False}, "serverTime": now()}
EXPECTED = []
def expect(method, path, response, query=None, body=None, user=SELLER, status=200, save=None, repeat=None, delay=0):
    EXPECTED.append(dict(method=method, path=path, response=response, query=query or {}, body=body, user=user, status=status, save=save, repeat=repeat, delay=delay))
expect("GET", ROOT + "/limits", limit_view, q())
expect("GET", ROOT + "/rules", rule_page, q(limit=1))
expect("GET", ROOT + "/rules", lambda: rule_page(True), q(after="hat", limit=1, catalogRevision=7))
expect("GET", ROOT, lambda: large_state(False), q())
expect("GET", ROOT, lambda: large_state(True), q())
expect("GET", ROOT + "/listings", lambda: {"listings": [], "nextCursor": CURSOR, "serverTime": now()}, q(itemKey="hat", minPrice=1, maxPrice=200, limit=1))
expect("GET", ROOT + "/listings", lambda: {"listings": [row()], "nextCursor": None, "serverTime": now()}, q(itemKey="hat", minPrice=1, maxPrice=200, limit=1, cursor=CURSOR))
expect("GET", ROOT + "/listings/" + LISTING, lambda: {"listing": row(), "serverTime": now()}, q())
expect("GET", "/api/v1/app/items", lambda: inventory(revision=9, instances=[original()]), q())
expect("GET", ROOT + "/rules", rule_page, q(limit=50))
expect("POST", ROOT + "/listings", {"code": "STATE_CONFLICT"}, body="create", status=503, save="create", delay=.2)
expect("GET", ROOT + "/operations/__CREATE__", lambda: {"receipt": receipt("create"), "serverTime": now()}, q())
expect("GET", "/api/v1/app/items", {"code": "STATE_CONFLICT"}, q(), status=409)
expect("GET", "/api/v1/app/items", inventory, q())
expect("GET", "/api/v1/app/player-values", values, q())
expect("GET", ROOT, lambda: state(revision=6, history=[row(status="sold", revision=2, buyer=OTHER)]), q())
expect("POST", ROOT + "/listings", lambda: written("create", True), body="create", repeat="create")
expect("POST", ROOT + "/listings", {"code": "OPERATION_CONFLICT"}, body="changed-create", status=409)
expect("GET", ROOT + "/operations/" + UNKNOWN, {"code": "OPERATION_NOT_FOUND"}, q(), status=404)
expect("GET", ROOT + "/listings/" + L2, lambda: {"listing": row(L2, I2), "serverTime": now()}, q(BUYER), user=BUYER)
expect("POST", ROOT + "/listings/" + L2 + "/buy", {"code": "STATE_CONFLICT"}, body="buy", user=BUYER, status=503, save="buy")
expect("POST", ROOT + "/listings/" + L2 + "/buy", lambda: written("buy", True), body="buy", user=BUYER, repeat="buy")
expect("GET", "/api/v1/app/items", lambda: inventory(BUYER, 20), q(BUYER), user=BUYER)
expect("GET", "/api/v1/app/player-values", lambda: values(BUYER, 5), q(BUYER), user=BUYER)
expect("GET", ROOT, lambda: state(BUYER, 7, history=[row(L2, I2, "sold", 2, BUYER)]), q(BUYER), user=BUYER)
expect("POST", ROOT + "/listings/" + LISTING + "/buy", {"code": "LISTING_UNAVAILABLE"}, body="losing-buy", user=BUYER, status=409)
expect("GET", ROOT + "/listings/" + L3, lambda: {"listing": row(L3, I3), "serverTime": now()}, q())
expect("POST", ROOT + "/listings/" + L3 + "/cancel", lambda: written("cancel"), body="cancel", save="cancel")
expect("GET", "/api/v1/app/items", inventory, q())
expect("GET", "/api/v1/app/player-values", lambda: values(revision=6), q())
expect("GET", ROOT, lambda: state(revision=8, opened=[row(L3, I3, "return_pending", 2)]), q())
expect("POST", ROOT + "/listings/" + L3 + "/retry-return", lambda: {"listing": row(L3, I3, "return_pending", 2), "delivery": "pending", "serverTime": now()}, body={"userId": SELLER})
expect("POST", ROOT + "/listings/" + L3 + "/retry-return", lambda: {"listing": row(L3, I3, "return_delivered", 3), "delivery": "delivered", "serverTime": now()}, body={"userId": SELLER})
expect("POST", "/api/v1/app/inbox/claim", {"code": "INVENTORY_FULL"}, body={"userId": SELLER, "entryId": ENTRY}, status=422)
expect("POST", "/api/v1/app/inbox/claim", claim, body={"userId": SELLER, "entryId": ENTRY})
expect("GET", "/api/v1/app/items", lambda: inventory(revision=11, instances=[original(I3)]), q())
expect("POST", "/api/v1/app/items/discard", lambda: inventory(revision=12), body={"userId": SELLER, "instanceId": I3, "expectedRevision": 11})
expect("POST", "/api/v1/app/inbox/claim", lambda: claim(True), body={"userId": SELLER, "entryId": ENTRY})
expect("GET", "/api/v1/app/items", lambda: inventory(revision=12), q())
expect("POST", ROOT + "/listings/" + L3 + "/retry-return", lambda: {"listing": row(L3, I3, "returned", 4), "delivery": "returned", "serverTime": now()}, body={"userId": SELLER})
expect("GET", ROOT + "/listings/" + L3, {"code": "LISTING_NOT_FOUND"}, q(BUYER), user=BUYER, status=404)
expect("GET", ROOT + "/limits", lambda: {**limit_view(), "limits": {**limits(), "openListingsPerPlayer": 2147483648}}, q(BUYER), user=BUYER)
expect("GET", ROOT, lambda: state(BUYER, 8), q(BUYER), user=BUYER, delay=.2)
expect("GET", ROOT, lambda: state(BUYER, 9), q(BUYER), user=BUYER, delay=.4)
expect("GET", ROOT, lambda: state(BUYER, 8), q(BUYER), user=BUYER)
expect("GET", ROOT + "/limits", {"code": "SERVER_ERROR"}, q(BUYER), user=BUYER, status=503, delay=.2)

class Handler(BaseHTTPRequestHandler):
    index = 0
    failed = False
    lock = threading.Lock()
    def log_message(self, *_): pass
    def do_GET(self): self.handle_contract()
    def do_POST(self): self.handle_contract()
    def handle_contract(self):
        try:
            with self.lock:
                step = type(self).index
                assert step < len(EXPECTED), "Unexpected extra transport attempt"
                expected = EXPECTED[step]; type(self).index += 1
            path = expected["path"].replace("__CREATE__", SAVED.get("create", {}).get("operationId", ""))
            actual = urlparse(self.path)
            assert self.command == expected["method"] and actual.path == path, (step + 1, self.command, actual.path, expected["method"], path)
            assert parse_qs(actual.query) == expected["query"], (step + 1, parse_qs(actual.query), expected["query"])
            assert self.headers.get("X-API-Key") == "market-fixture-key"
            assert self.headers.get("Authorization") == "Bearer market-" + ("seller" if expected["user"] == SELLER else "buyer") + "-session"
            raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
            body = json.loads(raw) if raw else None
            target = expected["body"]
            if isinstance(target, str):
                kind = "create" if "create" in target else "buy" if "buy" in target else "cancel"
                original_body = {"userId": expected["user"], "operationId": body["operationId"], "issuedAt": body["issuedAt"]}
                assert str(UUID(body["operationId"])) == body["operationId"]
                assert iso(dt.datetime.fromisoformat(body["issuedAt"].replace("Z", "+00:00"))) == body["issuedAt"]
                if kind == "create": original_body.update(instanceId=INSTANCE, expectedInventoryRevision=9, expectedConfigRevision=7, price=102 if target == "changed-create" else 101, durationSeconds=300)
                elif kind == "buy": original_body.update(expectedListingRevision=1, expectedPrice=101, expectedCurrencyKey="1coin.gold")
                else: original_body.update(expectedListingRevision=1)
                assert body == original_body, (step + 1, body, original_body)
                if expected["repeat"]: assert raw == SAVED_RAW[expected["repeat"]], "Original bytes changed on retry"
                if target == "changed-create": assert body["operationId"] == SAVED["create"]["operationId"] and body["issuedAt"] == SAVED["create"]["issuedAt"]
                if expected["save"]: SAVED[expected["save"]] = copy.deepcopy(body); SAVED_RAW[expected["save"]] = raw
            else: assert body == target, (step + 1, body, target)
            if expected["delay"]: time.sleep(expected["delay"])
            value = expected["response"]() if callable(expected["response"]) else expected["response"]
            encoded = json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode()
            self.send_response(expected["status"]); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(encoded))); self.end_headers(); self.wfile.write(encoded)
        except (BrokenPipeError, ConnectionResetError): pass
        except Exception as error:
            type(self).failed = True
            print("FAIL Marketplace exact fixture:", repr(error), flush=True)
            self.send_error(500)

def main():
    server = ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("HORIZON_MARKETPLACE_FIXTURE_PORT", "20115"))), Handler)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    if os.environ.get("MOCK_SERVER_READY_FILE"):
        with open(os.environ["MOCK_SERVER_READY_FILE"], "w") as file: file.write("ready")
    deadline = time.monotonic() + 240
    while Handler.index < len(EXPECTED) and not Handler.failed and time.monotonic() < deadline: time.sleep(.05)
    time.sleep(.5); server.shutdown(); server.server_close()
    assert not Handler.failed and Handler.index == len(EXPECTED), ("Missing or invalid requests", Handler.index, len(EXPECTED))
    print(f"PASS Marketplace strict HTTP: {len(EXPECTED)} exact requests/all10 routes,2051 retained opens+151 history,original bytes/recovery,two-player buy/cancel/Inbox original claim and no synthetic items", flush=True)

if __name__ == "__main__": main()
