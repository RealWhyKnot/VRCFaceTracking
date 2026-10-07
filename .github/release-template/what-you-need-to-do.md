## What you need to do

Fresh install: follow the install steps above. Existing installs are offered this update when the app starts; accept it and the app downloads, verifies and applies the update, then restarts. Installed copies update by running the new setup silently. Extracted copies get the new archive copied over their folder, or you can close the app and extract it over the old folder by hand.

Switching from an extracted copy to the setup: run the setup, then start VRCFaceTracking from the Start menu. Settings and modules carry over. Delete the old folder once the installed copy works.

Beta releases log verbosely by default. Stable releases log warnings and errors only; turn on verbose logging under Settings, Diagnostics before reproducing a problem. When reporting a bug, attach the newest `vrcft_*.log` and `vrcft_module_*.log` from the log folder (Settings, Diagnostics, Open folder).
