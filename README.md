# Real-Time Shop 0.2.0

An unofficial, opt-in real-time shop clock and after-hours sleep schedule for Probably Stolen's Windows Demo. Original mod code is MIT licensed, including commercial reuse.

Compatibility is intentionally narrow: Steam Demo app **4349200**, public build **25382790**, visible **049-Rev5-L**. Unity's internal `Application.version` is **0.46D** for those same tested binaries. Other builds and the full game are not certified. The mod disables itself when its GameAssembly hash or MelonLoader version checks fail.

Read [README.txt](README.txt) before installation for save backups, timing, economy effects, conflicts, uninstall and coverage limits. Install the compiled mod through **MelonLoader 0.7.3**, using the game's `Mods` folder. This is not a native `IMod`/`manifest.xml` package.

## Build

Use Windows, PowerShell, the .NET SDK 10.0.401 (release build toolchain), and the .NET 6 reference pack. Obtain the game through a licensed installation and MelonLoader 0.7.3 from its official distribution. Let the loader generate the game's interop assemblies. Dependencies and game files are not included here; see [DEPENDENCIES.txt](DEPENDENCIES.txt).

```powershell
./Build.ps1 -GameDir 'D:/SteamLibrary/steamapps/common/Probably Stolen Demo'
./Test.ps1
```

Building reads dependency references and writes only local `bin`/`obj` outputs. It does not install the mod or launch the game. Output: `src/RealTimeShop/bin/Release/net6.0/ProbablyStolen.RealTimeShop.dll`. All runtime references use `Private=false`. To supply references separately, build the project with `-p:LoaderDir=... -p:InteropDir=...`.

Deterministic release compilation uses `ContinuousIntegrationBuild`, no PDB, and a source path map. For byte-for-byte reproduction, use the same SDK/reference packs and dependency versions. Interfaces are dynamically referenced; Il2CppInterop remains separately replaceable and mod source is provided for rebuilding/relinking. Modification and reverse engineering to debug library modifications are not restricted.

## Validation

The public checks exercise original clock, consent, closing and security policies with synthetic inputs. They do not run native gameplay. The release also undergoes private, isolated native automation with licensed game files and disposable saves; those copyrighted/private fixtures, test adapters and logs are excluded from publication. See VALIDATION.txt for the final release results and exact binary hash.

## Conduct and privacy

The mod makes no network requests and transmits no player data. Its diagnostics remain in local loader logs. It changes opted-in save behavior, customer timing, available trading time and therefore potential income; it preserves native overnight expenses and security outcomes. Other mods touching these systems may conflict.

No game source, assets, generated interop assemblies, saves, or loader binaries are distributed. Native UI controls are reused only at runtime. The mod does not activate dormant or unreleased perks. The game's [modding code of conduct](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md) and [native API guide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md) are separate from this loader's installation instructions.

MIT covers the original project. See [LICENSE](LICENSE), [LICENSE-NOTICE.txt](LICENSE-NOTICE.txt), and `licenses/` for dependency notices and applicable license texts.
