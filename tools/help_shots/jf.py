"""Small client for the throwaway Jellyfin (help centre screenshots, 17 November 2026). Signs in through the API with
the Authorization: MediaBrowser header. Example data only."""
import json, os, sys, urllib.request, urllib.error, base64

BASE = os.environ.get("JF", "http://127.0.0.1:18900")
# The work folder (tools/help_shots/run.sh) holds the test password made for the throwaway server.
WORK = os.environ.get("WORK", os.getcwd())
PASSWORD = open(os.path.join(WORK, "admin-password")).read().strip()
CLIENT = 'MediaBrowser Client="Help shots", Device="Script", DeviceId="help-shots-script", Version="1.0"'
TOKEN = None


def call(method, path, body=None, raw=None, ctype="application/json", ok404=False):
    h = {"Authorization": CLIENT + (f', Token="{TOKEN}"' if TOKEN else "")}
    data = None
    if raw is not None:
        data, h["Content-Type"] = raw, ctype
    elif body is not None:
        data, h["Content-Type"] = json.dumps(body).encode(), "application/json"
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=h)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            t = r.read()
            return json.loads(t) if t and r.headers.get("Content-Type", "").startswith("application/json") else t
    except urllib.error.HTTPError as e:
        if ok404 and e.code == 404:
            return None
        raise SystemExit(f"{method} {path}: {e.code} {e.read()[:300]}")


def sign_in(user="Alex"):
    global TOKEN
    r = call("POST", "/Users/AuthenticateByName", {"Username": user, "Pw": PASSWORD})
    TOKEN = r["AccessToken"]
    return r
