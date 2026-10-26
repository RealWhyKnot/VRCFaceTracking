# Privacy policy

VRCFaceTracking doesn't collect personal data. It has no analytics, telemetry or crash reporting.

## Logs

Diagnostic logs stay on your machine and are never uploaded. By default they go in `%LocalAppData%\VRCFaceTracking\logs`. You can pick another folder under "Log folder" in Settings, or set the `VRCFT_LOG_DIRECTORY` environment variable. At startup the app deletes logs beyond the number set in "Log files to keep", and you can delete the rest whenever you like.

## Network connections

These are all the connections the app makes on its own:

- OSC to VRChat over UDP, sent to the address in Settings (`127.0.0.1` unless you change it).
- OSCQuery, which lets VRChat and the app find each other. The app runs a small HTTP server on `127.0.0.1`, announces it with mDNS multicast on your local network, and reads your avatar's parameters from VRChat's own OSCQuery server.
- The module list from `registry.vrcft.io`, fetched at every startup and when you open the Module Registry.
- Module downloads when you install or update a module. The module's author sets the download link, so the file can come from any host.
- An update check against `api.github.com` at startup on release builds. You can turn it off under Updates in Settings. If you accept an update, it downloads from GitHub.
- The contributors list in Settings normally ships with the app. If a build doesn't include it, the app fetches it from `api.github.com`.

The servers it contacts see your IP address, the same as any website you visit.

Tracking modules you install run their own code. They can make their own connections and have their own privacy policies.
