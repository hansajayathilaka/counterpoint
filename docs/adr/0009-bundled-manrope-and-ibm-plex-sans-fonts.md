# ADR-0009: Bundle Manrope and IBM Plex Sans as embedded fonts

**Status:** Accepted
**Date:** 2026-09-19
**Deciders:** P3-T17

## Context

The "UI redesign v2" (`P3-T17`-`P3-T23`, planned 2026-09-19) is grounded in an owner-approved
Claude Artifact prototype that specifies Manrope for display/headings/totals and IBM Plex Sans
for body text. The prototype's own HTML used Google Fonts CDN `<link>` tags to load both
typefaces at runtime.

That is not an option here. `CLAUDE.md` is explicit: "No code path in a sale, return, price
lookup or report may touch the network," and the redesign plan added `NFR-U5` for exactly this
reason - "No font, icon or other asset used by the application may be loaded over the network.
Every typeface the UI uses must be bundled with the installer and resolved from an embedded
application resource." A live font fetch would also silently break the whole application the
first time the shop's internet connection is down, which for this single-terminal offline-first
product is not a rare edge case, it is Tuesday.

## Options considered

**Live Google Fonts CDN fetch (the prototype's own approach).** Rejected outright - violates the
network ban above and NFR-U5, and would fail differently (blank/fallback font, not a crash) every
time the shop's connection is down, which is worse than a design defect: an invisible one.

**Rely on a font already installed on the shop's Windows terminal (e.g. Segoe UI) instead of
bundling anything.** Rejected: does not deliver the approved design (Manrope/IBM Plex Sans are
specific, deliberate choices in the prototype, not "any reasonable sans-serif"), and a clean
Windows install is not guaranteed to have either typeface present.

**Bundle the fonts as embedded Avalonia resources.** Chosen. Both Manrope and IBM Plex Sans are
released under the SIL Open Font License 1.1, which explicitly permits bundling, embedding and
redistribution (including inside a commercial application) provided the font is not sold on its
own and the licence text travels with the font files - exactly this repository's own posture on
every other dependency (see `docs/00_ENGINEERING_GUIDE.md`'s "do not add a dependency without a
note in `docs/adr/`" rule, and ADR-0007/ADR-0008 for the same licence-driven reasoning applied to
a package rather than a font).

## Decision

Both typefaces are committed as binary `.ttf` files under
`src/Counterpoint.Ui/Assets/Fonts/{Manrope,IBMPlexSans}/`, each folder carrying its own upstream
`OFL.txt` licence text unmodified alongside the font file, marked `<AvaloniaResource>` via the
existing `Assets\**` glob in `Counterpoint.Ui.csproj`. Both files are variable fonts (Manrope
spans weight 200-800; IBM Plex Sans spans width 75-100% and weight 100-700), sourced from the
canonical upstream projects (`github.com/googlefonts/manrope`, `github.com/IBM/plex`) via their
Google Fonts OFL mirror, which redistributes the identical, unmodified upstream binaries plus
licence file Google Fonts itself serves - the same font bytes the prototype's CDN link would have
downloaded, just resolved from disk instead of a socket.

`Styles/Tokens.Light.axaml`/`Tokens.Dark.axaml` each expose two new resources, identical in both
files because a typeface does not change with the theme variant:

```xml
<FontFamily x:Key="DisplayFontFamily">avares://Counterpoint.Ui/Assets/Fonts/Manrope#Manrope</FontFamily>
<FontFamily x:Key="BodyFontFamily">avares://Counterpoint.Ui/Assets/Fonts/IBMPlexSans#IBM Plex Sans</FontFamily>
```

The `avares://.../Assets/Fonts/<Family>#<Name>` form (a folder, not a specific file) is
Avalonia's own supported font-collection resolution (`EmbeddedFontCollection`/
`FontFamilyLoader.LoadFontAssets`): it scans every `.ttf`/`.otf` file in the named folder and
matches its typographic family name against the fragment, entirely offline, with zero network
access on the resolution path - proven by a dedicated test (SRS NFR-U5).

## Consequences

The installer now carries two extra font files (~165 KB Manrope, ~525 KB IBM Plex Sans) - a
noticeable size increase, but licenced outright and shipped in full, not a network dependency to
track for uptime. The two `OFL.txt` files are the provenance and redistribution record for future
upgrades: replacing either binary with a newer upstream release only requires the same licence
file to still say OFL 1.1, and the family name inside the font (used for the `#Manrope`/`#IBM
Plex Sans` fragment) must not change. This is the maintenance burden the redesign plan itself
already named: "two bundled font families to track for licence provenance and upstream updates."
No consuming screen is changed by this task - `P3-T18`-`P3-T21` are the tasks that actually apply
`DisplayFontFamily`/`BodyFontFamily` to a `FontFamily=` on a real view.
