"""Adds (add) or removes (remove) the TheMightyTRexUK repository in the throwaway Jellyfin, or the old Picture
shrinker address (old-add, old-remove), so each screenshot shows the page as its step describes it."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import jf
NEW = ("TheMightyTRexUK", "https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugins/main/manifest.json")
OLD = ("Picture shrinker", "https://raw.githubusercontent.com/TheMightyTRex/jellyfin-plugin-picture-shrinker/main/manifest.json")
jf.sign_in()
repos = jf.call("GET", "/Repositories")
for arg in sys.argv[1:]:
    name, url = OLD if arg.startswith("old") else NEW
    repos = [r for r in repos if r["Url"] != url]
    if arg.endswith("add"):
        repos.append({"Name": name, "Url": url, "Enabled": True})
jf.call("POST", "/Repositories", repos)
print([r["Name"] for r in jf.call("GET", "/Repositories")])
