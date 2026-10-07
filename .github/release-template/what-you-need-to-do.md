## What you need to do

New installs: follow the install steps above. Existing installs get offered this update when the app starts. Accept it and the app downloads, verifies and applies the update, then restarts. Installed copies update by running the new setup silently. Extracted copies get the new archive copied over their folder, or you can close the app and extract it over the old folder yourself.

To switch from an extracted copy to the setup, run the setup and start VRCFaceTracking from the Start menu. Settings and modules carry over. Delete the old folder once the installed copy works.

Beta releases log verbosely by default and stable releases log only warnings and errors. Before reproducing a problem on a stable release, turn on verbose logging under Settings, Diagnostics. Bug reports need the newest `vrcft_*.log` and `vrcft_module_*.log` from the log folder (Settings, Diagnostics, Open folder).
