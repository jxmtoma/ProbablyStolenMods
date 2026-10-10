Real-Time Shop 0.2.0 - stable release

Adds an opt-in active-time shop clock to Probably Stolen. Choose Real-time
mode when creating a NEW game; the choice defaults OFF. Existing vanilla
saves are not automatically converted. The calendar shows the clock.

Default day: 30 active minutes, mapping 08:00 to 20:00. Ordinary customer
patience: 120 seconds. Closing grace: 60 seconds. Dialogue, native pause,
focus loss and protected transitions hold time. Trading panels let time
advance but defer unsafe dismissal/closure. Ordinary patrol visits can
leave safely; unresolved rent and other protected interactions block closure.

AFTER CLOSING
Closing the shop no longer freezes the clock. Evening time advances at the
same rate as the shop day, through midnight. Use the native bedroll and
sleep screen when ready. Midnight alone does not advance the native day.
At 02:00 a reminder appears. At 03:00 the clock stops advancing and requests
native sleep as soon as interactions, pause, transitions and native sleep
requirements allow it. It does not dismiss protected visitors, return you
from an outing, resolve rent, pay fines or bypass a disabled sleep button.

Sleep gives at least eight hours of rest, with earliest wake at 08:00 and
latest at 11:00. A 01:00 bedtime wakes at 09:00; 03:00 wakes at 11:00.
Closing remains 20:00, so late sleep shortens the next trading day. Bedtime
is rounded down to its displayed minute. The native overnight sequence,
expenses, day counter and save run once. The morning report holds time;
closing it resumes time. There is no added fine or patience penalty.

On nights marked by the game's native outing flag, wake remains 08:00 to
avoid stacking this new delay on the existing native customer penalty.
The native outing logic is untouched. Short Sleeper has dormant name,
description and icon content, but is absent from this build's normal
25-entry starting-perk list. Its native IsShortSleeper check always returns
false; it is not a confirmed obtainable perk. Full-game availability is
unverified. The mod does not activate or add it. Outing time still advances;
forced sleep waits until you return safely to the shop.

Compatibility is restricted to the verified Probably Stolen Windows Demo:
Steam app 4349200, public branch, build 25382790;
visible in-game version: VERSION 049-Rev5-L;
internal Unity Application.version: 0.46D (not an older-release label).
Runtime: Unity 2021.3.45f2, MelonLoader 0.7.3 Open-Beta, .NET 6.
This release targets the exact native binaries of this installed build.
Required GameAssembly.dll SHA-256:
3BEA17EEEC77ADAB6418918C28A8AA9A06F290582A2972639A48BD7E5A01AB44
Verified IL2CPP global-metadata.dat SHA-256:
EFDCA95772FDCE327B51E0C7E8D320E7055917BE5C669DB47C1F7B36389F1F41
The mod checks the GameAssembly hash and loader version and disables itself
on mismatch. The metadata hash above records provenance; it is not a new guard.
Other game builds, platforms, loader versions and mod combinations are not
certified. Own a normally licensed game copy; this package includes no game
or loader files and does not change Steam ownership or achievement behavior.

BEFORE INSTALLING: close the game and back up the entire save folder,
including every .es3 file and both .realtime-shop.json and
.realtime-choice.json sidecars, plus your current mod DLL and configuration.
Default Windows save location:
%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen
Keep matched native saves and sidecars together. A mismatched/missing consent
or timer sidecar prevents restoring real-time mode; do not edit their hashes.

INSTALL
1. Obtain MelonLoader 0.7.3 and its .NET 6 prerequisites separately. Start
   the supported game once with the loader to generate native interop files.
2. With the game closed, copy Mods\ProbablyStolen.RealTimeShop.dll into
   the game's Mods folder. Keep only one copy/version of this mod.
3. Start normally. Create a new game and explicitly select Real-time mode.
   Leave it unchecked for normal game timing. No special launch flags needed.

CONFIGURATION
Defaults are created under [RealTimeShop] in UserData\MelonPreferences.cfg.
RealTimeShop.cfg.example is a reference: merge that section if needed;
do not overwrite settings for other mods. Restart after changes. A restored
day retains its saved timing; new days use the configured timing. Valid
ranges: DayMinutes 1-1440, PatienceSeconds 1-86400,
ClosingGraceSeconds 0-86400. This release ships 30/120/60, not test timing.
Use the game's normal pause/menu controls; there is no custom F8 shortcut.

UNINSTALL / ROLLBACK
Close the game. Move this mod DLL out of Mods. Preserve your backups and
sidecars; do not delete native saves. Without the mod, native game behavior
applies. Timer sidecars now use version 2 and read version 1. 0.1.0-rc.1 cannot
read version 2.
To roll back to 0.1.0-rc.1, restore the backed-up DLL, configuration and matched
native save plus BOTH sidecars from before this upgrade. If you
also roll back progress, restore a matched native save plus BOTH sidecars
from the same backup, with the game closed. Restore only your own files.
Re-enabling against a native save changed while the mod was absent may not
restore its previous real-time consent/timing. Avoid overwriting the backup.

VALIDATION / LIMITS
See CHANGELOG.txt for release validation. Release validation is not an exhaustive playthrough. Native save creation UI, long campaigns,
every scripted visit, inspections, rent payment/refusal outcomes and other
mods are not all covered. Tests use disposable fixtures near the relevant clock
times; they are not full-length campaign playthroughs. Actual outing travel,
the outing customer-penalty magnitude, crashes during native save, and every unsafe
interaction at the 03:00 cutoff remain outside native test coverage.
Short Sleeper was absent from the inspected native perk-selection screen;
no active-perk behavior or full-game perk availability is certified.
Report mod problems with this version, game/loader version and relevant
MelonLoader log lines; remove personal identifiers before sharing logs.



LICENSE AND PRIVACY
Original mod code is MIT licensed, including commercial reuse. See LICENSE.txt
and LICENSE-NOTICE.txt. The mod makes no network requests and transmits no
player data. Diagnostics stay in local MelonLoader logs.

SAVE / ECONOMY / CONFLICT EFFECTS
Opted-in runs use the native save writer and two hash-bound sidecars. Timed
customer departures, admission cutoff and shorter late-start trading days
can change the number of trades and income. Native overnight expenses and
security outcomes still apply. Keep a matched backup before installing.
Other clock, customer, shop-closing, sleep, save or new-game UI mods may
conflict through shared Harmony patches. No combinations are certified.
This package uses MelonLoader's Mods folder, not the native IMod/manifest.xml
installation described in the game's separate modding API guide.