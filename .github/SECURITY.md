# Security policy

VRCFaceTracking reads face and eye tracking data, loads tracking modules and sends OSC to VRChat.

## Reporting

Please don't open a public issue. Use [a private security advisory](https://github.com/RealWhyKnot/VRCFaceTracking/security/advisories/new) so we can work out a fix before anything is published.

I try to reply within 7 days. There's no bug bounty.

## In scope

- Tracking data sent anywhere other than the OSC targets you set.
- A release archive or installer that doesn't match what the release page publishes.
- The app or installer doing more than the README says, such as writing outside its own folders.
- A module or another program on the network gaining control of the app without the user's say.

## Out of scope

- Problems in a third-party tracking module itself.
- Problems in VRChat or SteamVR.
- Attacks that need admin access to your PC already.
