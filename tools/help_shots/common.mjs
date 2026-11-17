// Shared: a signed-in page on the throwaway Jellyfin. The token comes from the API (AuthenticateByName with the
// Authorization: MediaBrowser header) and goes into the web app's own stored credentials.
import { chromium } from "playwright";
import fs from "fs";
export const BASE = process.env.JF || "http://127.0.0.1:18900";
const CLIENT = 'MediaBrowser Client="Help shots", Device="Playwright", DeviceId="help-shots-playwright", Version="1.0"';
export async function signedIn({ width = 1280, height = 800, user = "Alex" } = {}) {
  const pw = fs.readFileSync("/work/admin-password", "utf8").trim();
  const info = await (await fetch(`${BASE}/System/Info/Public`)).json();
  const auth = await (await fetch(`${BASE}/Users/AuthenticateByName`, { method: "POST",
    headers: { Authorization: CLIENT, "Content-Type": "application/json" }, body: JSON.stringify({ Username: user, Pw: pw }) })).json();
  const creds = { Servers: [{ ManualAddress: BASE, manualAddressOnly: true, Id: info.Id, Name: info.ServerName, LocalAddress: BASE,
    DateLastAccessed: Date.now(), LastConnectionMode: 2, UserId: auth.User.Id, AccessToken: auth.AccessToken }] };
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width, height }, colorScheme: "light", locale: "en-GB", timezoneId: "Europe/London" });
  await context.addInitScript((c) => {
    localStorage.setItem("jellyfin_credentials", JSON.stringify(c));
    localStorage.setItem("enableAutoLogin", "true");
    const uid = c.Servers[0].UserId;
    localStorage.setItem(uid + "-appTheme", "light");
    localStorage.setItem(uid + "-dashboardTheme", "light");
  }, creds);
  return { browser, context, token: auth.AccessToken, userId: auth.User.Id, serverId: info.Id };
}
