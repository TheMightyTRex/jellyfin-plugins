"""Light theme for every made-up person, for the screenshots (SCREENSHOTS.md: light theme)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import jf
jf.sign_in()
for u in jf.call("GET", "/Users"):
    p = jf.call("GET", f"/DisplayPreferences/usersettings?userId={u['Id']}&client=emby")
    p.setdefault("CustomPrefs", {}).update({"appTheme": "light", "dashboardTheme": "light"})
    jf.call("POST", f"/DisplayPreferences/usersettings?userId={u['Id']}&client=emby", p)
print("light")
