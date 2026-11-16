#!/usr/bin/env python3
"""Strict artificial item gift consumer contract; no real accounts or persistence."""
import copy
import datetime as dt
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse
from uuid import UUID

SENDER = "20000000-0000-4000-8000-000000001140"
RECIPIENT = "30000000-0000-4000-8000-000000001140"
INSTANCE = "40000000-0000-4000-8000-000000001140"
I2 = "40000000-0000-4000-8000-000000001141"
GIFT = "50000000-0000-4000-8000-000000001140"
G2 = "50000000-0000-4000-8000-000000001141"
UNKNOWN = "60000000-0000-4000-8000-000000009999"
OFFER = "message:70000000-0000-4000-8000-000000001140"
RETURN = "message:70000000-0000-4000-8000-000000001141"
OFFER2 = "message:70000000-0000-4000-8000-000000001142"
ROOT = "/api/v1/app/item-gifts"
BASE = dt.datetime(2026, 11, 16, 12, tzinfo=dt.timezone.utc)
START = time.monotonic()
SAVED = {}
SAVED_RAW = {}
def iso(value): return value.isoformat(timespec="milliseconds").replace("+00:00", "Z")
def now(): return iso(BASE + dt.timedelta(seconds=time.monotonic() - START))
def stamp(): return iso(BASE)
def original(instance=INSTANCE):
    return {"instanceId": instance, "itemKey": "hat", "properties": {"power": 9007199254740991, "label": "雪/😀\0tail", "bound": True}, "equipped": False, "createdAt": stamp()}
def limits(lower=False):
    return {"openGiftsPerPlayer": 1 if lower else 3, "retainedGiftsPerProject": 50, "projectWritesPerMinute": 10, "historyPerPlayer": 20, "receivedPageSize": 50, "playerWriteIntervalMillis": 1000, "expirySeconds": 604800, "terminalRetentionSeconds": 604800, "operationRetentionSeconds": 2592000}
def gift(id=GIFT, instance=INSTANCE, status="offered", revision=2, declined=False, offer=OFFER, returned=None):
    terminal = status in ("claimed", "returned")
    return {"id": id, "revision": revision, "senderId": SENDER, "recipientId": RECIPIENT, "returnOwnerId": SENDER, "itemReference": {"instanceId": instance, "itemKey": "hat"}, "currencyKey": "1coin.gold", "fee": 5, "configRevision": 7, "status": status, "createdAt": stamp(), "expiresAt": iso(BASE + dt.timedelta(days=7)), "terminalAt": stamp() if terminal else None, "declinedAt": stamp() if declined else None, "offerEntryId": offer, "returnEntryId": returned, "ownerUnavailable": False}
def state(user=SENDER, revision=4, sent=None, history=None, received=None, lower=False):
    return {"state": {"userId": user, "revision": revision, "sent": sent or [], "history": history or [], "received": received or [], "operationWindow": {"serverTime": now(), "acceptAfter": iso(BASE - dt.timedelta(days=30))}, "limits": limits(lower)}, "serverTime": now()}
def inventory(user=SENDER, revision=10, instances=None):
    return {"userId": user, "revision": revision, "instances": instances or [], "limits": {"catalog": 6000, "inventory": 250, "receipts": 80}, "serverTime": now()}
def values(user=SENDER, revision=5):
    return {"userId": user, "day": "2026-11-16", "definitionVersion": 1, "revision": revision, "values": [{"key": "1coin.gold", "balance": 95 if user == SENDER else 0, "earnedToday": 0, "dailyCap": None}], "operationWindow": {"serverTime": now(), "oldestAcceptedAt": iso(BASE - dt.timedelta(days=30))}}
def receipt():
    body = SAVED["send"]
    return {"operationId": body["operationId"], "issuedAt": body["issuedAt"], "giftId": GIFT, "giftRevision": 2, "recordedAt": stamp(), "recipientId": RECIPIENT, "itemReference": {"instanceId": INSTANCE, "itemKey": "hat"}, "currencyKey": "1coin.gold", "fee": 5, "configRevision": 7}
def entry(id, type, key, reference, parameters, gift_id, instance, direction, claimed=False):
    return {"id": id, "kind": "message", "type": type, "reference": reference, "textKey": key, "parameters": parameters, "createdAt": stamp(), "expiresAt": iso(BASE + dt.timedelta(days=7)), "read": claimed, "claimed": claimed, "overdue": False, "attachment": {"values": [], "itemGift": {"giftId": gift_id, "instanceId": instance, "itemKey": "hat", "direction": direction}}}
def inbox(user, entries, revision=1):
    return {"userId": user, "cursor": "gift-" + str(revision), "revision": revision, "broadcastRevision": 1, "serverTime": now(), "pollAfterSeconds": 120, "unchanged": False, "entries": entries, "counts": {"item_gift": {"total": len(entries), "unread": len(entries), "claimable": len(entries)}}, "limits": {"targeted": 10, "broadcasts": 1, "freeTexts": 1, "freeTextBytes": 256, "freeTextRetentionDays": 7}}
def recipient_inbox(): return inbox(RECIPIENT, [entry(OFFER, "item_gift", "horizon.item_gifts.offer", GIFT, {"senderId": SENDER}, GIFT, INSTANCE, "offer"), entry(OFFER2, "item_gift", "horizon.item_gifts.offer", G2, {"senderId": SENDER}, G2, I2, "offer")])
def claim(user, entry_id, instance, replay=False): return {"entryId": entry_id, "replayed": replay, "inbox": inbox(user, [], 2), "playerValues": values(user, 6), "items": [{"instanceId": instance, "itemKey": "hat"}]}
def q(user=SENDER, **fields): return {"userId": [user], **{k: [str(v)] for k, v in fields.items()}}
def config_view(): return {"config": {"revision": 7, "enabled": True, "currencyKey": "1coin.gold", "fee": 5}, "limits": limits(), "serverTime": now()}
EXPECTED = []
def expect(method, path, response, query=None, body=None, user=SENDER, status=200, save=None, repeat=None, delay=0):
    EXPECTED.append(dict(method=method, path=path, response=response, query=query or {}, body=body, user=user, status=status, save=save, repeat=repeat, delay=delay))
expect("GET", ROOT + "/config", config_view, q())
expect("GET", "/api/v1/app/items", lambda: inventory(revision=9, instances=[original()]), q())
expect("POST", ROOT, {"code": "STATE_CONFLICT"}, body="send", status=409, save="send", delay=.2)
expect("GET", ROOT + "/operations/__SEND__", lambda: {"receipt": receipt(), "serverTime": now()}, q())
expect("GET", "/api/v1/app/items", {"code": "STATE_CONFLICT"}, q(), status=409)
expect("GET", "/api/v1/app/items", inventory, q())
expect("GET", "/api/v1/app/player-values", values, q())
expect("GET", ROOT, lambda: state(revision=5, sent=[gift()], lower=True), q())
expect("POST", ROOT, lambda: {"receipt": receipt(), "gift": gift(), "replayed": True, "serverTime": now()}, body="send", repeat="send")
expect("POST", ROOT, {"code": "OPERATION_CONFLICT"}, body="changed-send", status=409)
expect("POST", ROOT, {"code": "RECIPIENT_NOT_FOUND"}, body="unknown-send", status=404)
expect("GET", "/api/v1/app/inbox", recipient_inbox, q(RECIPIENT), user=RECIPIENT)
expect("GET", ROOT, lambda: state(RECIPIENT, 3, received=[gift(), gift(G2, I2, offer=OFFER2)]), q(RECIPIENT), user=RECIPIENT)
expect("POST", ROOT + "/" + GIFT + "/decline", lambda: {"gift": gift(status="return_delivered", revision=4, declined=True, returned=RETURN), "replayed": False, "returnDelivery": "delivered", "serverTime": now()}, body={"userId": RECIPIENT, "expectedGiftRevision": 2}, user=RECIPIENT)
expect("POST", ROOT + "/" + GIFT + "/decline", lambda: {"gift": gift(status="return_delivered", revision=4, declined=True, returned=RETURN), "replayed": True, "returnDelivery": "delivered", "serverTime": now()}, body={"userId": RECIPIENT, "expectedGiftRevision": 2}, user=RECIPIENT)
expect("POST", "/api/v1/app/inbox/claim", lambda: claim(RECIPIENT, OFFER2, I2), body={"userId": RECIPIENT, "entryId": OFFER2}, user=RECIPIENT)
expect("GET", ROOT + "/" + GIFT, lambda: {"gift": gift(status="return_delivered", revision=4, declined=True, returned=RETURN), "serverTime": now()}, q(RECIPIENT), user=RECIPIENT)
expect("POST", "/api/v1/app/inbox/claim", lambda: claim(SENDER, RETURN, INSTANCE), body={"userId": SENDER, "entryId": RETURN})
expect("GET", ROOT + "/" + GIFT, lambda: {"gift": gift(status="returned", revision=5, declined=True, returned=RETURN), "serverTime": now()}, q())
expect("GET", ROOT + "/operations/" + UNKNOWN, {"code": "OPERATION_NOT_FOUND"}, q(), status=404)
expect("GET", ROOT, lambda: state(revision=6), q(), delay=.2)

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
            path = expected["path"].replace("__SEND__", SAVED.get("send", {}).get("operationId", ""))
            actual = urlparse(self.path)
            assert self.command == expected["method"] and actual.path == path, (step + 1, self.command, actual.path, expected["method"], path)
            assert parse_qs(actual.query) == expected["query"], (step + 1, parse_qs(actual.query), expected["query"])
            assert self.headers.get("X-API-Key") == "gift-fixture-key"
            assert self.headers.get("Authorization") == "Bearer gift-" + ("sender" if expected["user"] == SENDER else "recipient") + "-session"
            raw = self.rfile.read(int(self.headers.get("Content-Length", "0")))
            body = json.loads(raw) if raw else None
            target = expected["body"]
            if isinstance(target, str):
                assert list(body.keys()) == ["userId", "operationId", "issuedAt", "recipientId", "instanceId", "expectedInventoryRevision", "expectedConfigRevision"], (step + 1, body)
                assert str(UUID(body["operationId"])) == body["operationId"]
                assert iso(dt.datetime.fromisoformat(body["issuedAt"].replace("Z", "+00:00"))) == body["issuedAt"]
                wanted = {"userId": SENDER, "operationId": body["operationId"], "issuedAt": body["issuedAt"], "recipientId": RECIPIENT if target != "changed-send" else "30000000-0000-4000-8000-000000001149", "instanceId": INSTANCE, "expectedInventoryRevision": 9, "expectedConfigRevision": 7}
                assert body == wanted, (step + 1, body, wanted)
                if expected["repeat"]: assert raw == SAVED_RAW[expected["repeat"]], "Original bytes changed on retry"
                if target == "changed-send": assert body["operationId"] == SAVED["send"]["operationId"] and body["issuedAt"] == SAVED["send"]["issuedAt"]
                if target == "unknown-send": assert body["operationId"] == UNKNOWN
                if expected["save"]: SAVED[expected["save"]] = copy.deepcopy(body); SAVED_RAW[expected["save"]] = raw
            else: assert body == target, (step + 1, body, target)
            if expected["delay"]: time.sleep(expected["delay"])
            value = expected["response"]() if callable(expected["response"]) else expected["response"]
            encoded = json.dumps(value, ensure_ascii=True, separators=(",", ":")).encode()
            self.send_response(expected["status"]); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(encoded))); self.end_headers(); self.wfile.write(encoded)
        except (BrokenPipeError, ConnectionResetError): pass
        except Exception as error:
            type(self).failed = True
            print("FAIL Item gifts exact fixture:", repr(error), flush=True)
            self.send_error(500)

def main():
    server = ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("HORIZON_ITEM_GIFTS_FIXTURE_PORT", "20140"))), Handler)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    if os.environ.get("MOCK_SERVER_READY_FILE"):
        with open(os.environ["MOCK_SERVER_READY_FILE"], "w") as file: file.write("ready")
    deadline = time.monotonic() + 240
    while Handler.index < len(EXPECTED) and not Handler.failed and time.monotonic() < deadline: time.sleep(.05)
    time.sleep(.5); server.shutdown(); server.server_close()
    assert not Handler.failed and Handler.index == len(EXPECTED), ("Missing or invalid requests", Handler.index, len(EXPECTED))
    print(f"PASS Item gifts strict HTTP: {len(EXPECTED)} exact requests/all6 routes,original bytes/recovery,decline replay,offer and return claims through Inbox and no synthetic items", flush=True)

if __name__ == "__main__": main()
