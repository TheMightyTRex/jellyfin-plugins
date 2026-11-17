"""Sets up the throwaway Jellyfin with made-up example data: the start wizard, four made-up people, a films and a
series library (no internet metadata), made-up cast, and large pictures for the shrinker to work on."""
import base64, glob, json, os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import jf

info = jf.call("GET", "/System/Info/Public")
if not info.get("StartupWizardCompleted"):
    jf.call("POST", "/Startup/Configuration", {"ServerName": "Example server", "UICulture": "en-GB", "MetadataCountryCode": "GB", "PreferredMetadataLanguage": "en"})
    jf.call("GET", "/Startup/User")
    jf.call("POST", "/Startup/User", {"Name": "Alex", "Password": jf.PASSWORD})
    jf.call("POST", "/Startup/RemoteAccess", {"EnableRemoteAccess": True, "EnableAutomaticPortMapping": False})
    jf.call("POST", "/Startup/Complete")
me = jf.sign_in()
users = {u["Name"]: u for u in jf.call("GET", "/Users")}
for name in ("Sam", "Priya", "Jordan"):
    if name not in users:
        jf.call("POST", "/Users/New", {"Name": name, "Password": jf.PASSWORD})
# Libraries with no internet metadata or pictures: nothing real comes in.
none = lambda t: {"Type": t, "MetadataFetchers": [], "MetadataFetcherOrder": [], "ImageFetchers": [], "ImageFetcherOrder": []}
libs = {l["Name"] for l in jf.call("GET", "/Library/VirtualFolders")}
for name, kind, path, types in (("Films", "movies", "/media/films", ["Movie"]), ("Series", "tvshows", "/media/series", ["Series", "Season", "Episode"])):
    if name not in libs:
        opts = {"EnableRealtimeMonitor": False, "SaveLocalMetadata": False, "EnableInternetProviders": False,
                "PathInfos": [{"Path": path}], "TypeOptions": [none(t) for t in types], "MetadataSavers": [],
                "DisabledLocalMetadataReaders": [], "EnableChapterImageExtraction": False, "ExtractChapterImagesDuringLibraryScan": False}
        jf.call("POST", f"/Library/VirtualFolders?name={name}&collectionType={kind}&refreshLibrary=true", {"LibraryOptions": opts})
for _ in range(60):
    items = jf.call("GET", f"/Users/{me['User']['Id']}/Items?Recursive=true&IncludeItemTypes=Movie,Series&Fields=People")["Items"]
    if len(items) >= 7:
        break
    time.sleep(3)
print(len(items), "films and series")
P = os.path.join(jf.WORK, "pictures")
cast = [["Alex Rowan", "Priya Hale", "Mara Ellison"], ["Sam Ellery", "Jordan Vale"], ["Tom Ashby", "Priya Hale"]]
for n, it in enumerate(items):
    s = it["Name"].split(" (")[0].lower().replace(" ", "-")
    full = jf.call("GET", f"/Users/{me['User']['Id']}/Items/{it['Id']}")
    full["People"] = [{"Name": p, "Type": "Actor", "Role": "Example role"} for p in cast[n % 3]]
    jf.call("POST", f"/Items/{it['Id']}", full)
    for kind, f in (("Primary", f"poster-{s}.jpg"), ("Backdrop", f"backdrop-{s}.jpg"), ("Thumb", f"thumb-{s}.jpg"), ("Logo", f"logo-{s}.png")):
        path = os.path.join(P, f)
        if os.path.exists(path):
            ctype = "image/png" if f.endswith(".png") else "image/jpeg"
            jf.call("POST", f"/Items/{it['Id']}/Images/{kind}", raw=base64.b64encode(open(path, "rb").read()), ctype=ctype)
for p in ("Alex Rowan", "Priya Hale", "Sam Ellery", "Jordan Vale", "Mara Ellison", "Tom Ashby"):
    r = jf.call("GET", f"/Persons/{p.replace(' ', '%20')}", ok404=True)
    if r:
        f = os.path.join(P, "person-" + p.lower().replace(" ", "-") + ".jpg")
        jf.call("POST", f"/Items/{r['Id']}/Images/Primary", raw=base64.b64encode(open(f, "rb").read()), ctype="image/jpeg")
    print(p, "found" if r else "not found")
print("done")
