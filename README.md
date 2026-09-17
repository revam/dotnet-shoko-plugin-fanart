# Shoko Fanart.tv Artwork Plugin

A [Shoko](https://shokoanime.com/) plugin that adds series and movie artwork
from [Fanart.tv](https://fanart.tv/) to the TMDB entries in your collection,
through Shoko's image manager.

Fanart.tv is where the artwork Shoko has no other source for lives: transparent
logos, banners, high resolution backgrounds and disc art, all drawn and uploaded
by people rather than scraped off a distributor's press kit.

## Scope

**Series and movie artwork only.** No season artwork, and no episode artwork.

That is a deliberate limit rather than a missing feature. Fanart.tv numbers its
season artwork by TheTVDB seasons, and Shoko's seasons come from TMDB. The two
agree often enough to be tempting and disagree often enough to be wrong, and
there is no honest way to attach a TheTVDB season 2 poster to a TMDB season 2
unless the two happen to agree on both season and episode counts. So
`seasonposter`, `seasonthumb` and `seasonbanner` are read from the response,
recognised, and dropped.

## How it works

- **Keying, for shows.** Fanart.tv's TV API is keyed by TheTVDB ID and has no
  other way in. A TMDB show carries a `TvdbShowID`, TMDB is what supplies that
  translation, and Shoko already stores it, so the whole lookup path is
  `TMDB show -> TvdbShowID -> GET /v3.2/tv/{tvdb_id}`. No Trakt, no TheTVDB API
  key, and no community mapping list.
- **Shows with no TheTVDB ID are skipped.** On a real 6,689 show library, 4,864
  shows carry one, so roughly a quarter of shows have no lookup path here at
  all. They are counted and reported in the sweep's log line rather than being
  guessed at from their titles, because a wrong artwork match is worse than no
  artwork.
- **Keying, for movies.** The movie API accepts a TMDB or an IMDB ID, and Shoko
  always has the TMDB one for a TMDB movie, so movies need no translation step
  and no extra coverage caveat. `ImdbMovieID` is stored on TMDB movies as well
  and is not needed.
- **What the artwork is attached to.** The TMDB show or movie itself, not the
  shoko series in front of it. A shoko series already reads the images of
  everything it is linked to, so artwork attached to the TMDB show appears on
  the series anyway, survives the series being removed and re-added, and is not
  duplicated when two shoko series link to the same TMDB show.
- **Registering the template URL.** Shoko stores one template URL per image
  source and rebuilds a download URL as `string.Format(template, resourceID)`.
  It ships defaults for AniDB, TMDB and AniList only, so the plugin registers
  `https://assets.fanart.tv/fanart/{0}` for Fanart.tv on first use, and leaves
  an existing value alone in case you point it at a mirror of your own. Each
  image's resource ID is the rest of its asset URL. A URL that is not a
  full-size Fanart.tv asset URL, or whose path would not fit the 128 character
  column, is skipped rather than stored as something that cannot be downloaded.
- **Ordering.** Within one Shoko image type, the asset kind ranks first and the
  number of likes breaks ties inside a kind, with the resource ID as the final
  tie-break so repeated sweeps do not reshuffle unchanged artwork. That is why a
  4K background outranks a 1080p one with more likes, and an `hdtvlogo` outranks
  a `clearlogo` with four times the likes: the higher resolution version of the
  same thing wins.
- **Withdrawn artwork.** A sweep also removes this plugin's own links to artwork
  Fanart.tv no longer lists, which is how a deleted or replaced upload stops
  being offered. Links you marked as preferred are left alone, and the image
  itself is never deleted, only the link.
- **The sweep.** Nothing in core asks an image contributor to refresh anything,
  so the plugin owns its own cadence: one recurring job walks the collection
  every seven days by default. Seven days is also how long a newly uploaded
  image takes to reach a project API key, so sweeping more often mostly spends
  requests. Two actions are registered for the impatient: "Refresh Fanart.tv
  Artwork" on a series, and "Sweep Fanart.tv Artwork" for the whole collection.

## Artwork mapping

Shoko has five image types. Fanart.tv has roughly twice as many asset kinds, so
the ones with no honest counterpart are dropped rather than forced into the
nearest slot.

### TV shows

| Fanart.tv | Shoko | |
|---|---|---|
| `tvposter` | Primary | |
| `show4kbackground` | Backdrop | Ranked above the 1080p version |
| `showbackground` | Backdrop | |
| `hdtvlogo` | Logo | Ranked above the SD version |
| `clearlogo` | Logo | |
| `tvbanner` | Banner | |
| `tvthumb` | dropped | A 500x281 thumbnail. Shoko has no thumbnail type, and it is too small to pass off as a backdrop |
| `hdclearart`, `clearart` | dropped | Transparent key art, which is not a logo, a banner or a backdrop |
| `characterart` | dropped | Art of a single character, with no counterpart in Shoko |
| `seasonposter`, `seasonthumb`, `seasonbanner` | dropped | Season artwork, numbered by TheTVDB seasons. See Scope |

### Movies

| Fanart.tv | Shoko | |
|---|---|---|
| `movieposter` | Primary | |
| `movie4kbackground` | Backdrop | Ranked above the 1080p version |
| `moviebackground` | Backdrop | |
| `hdmovielogo` | Logo | Ranked above the SD version |
| `movielogo` | Logo | |
| `moviebanner` | Banner | |
| `moviedisc` | Disc | The only user of Shoko's Disc type |
| `moviethumb` | dropped | A 1000x562 thumbnail, and Shoko has no thumbnail type |
| `hdmovieclearart`, `movieart` | dropped | Transparent key art, as above |

Asset kinds this table has never heard of are ignored. Fanart.tv adds kinds
server-side without notice, and guessing at one from its name is how artwork
ends up in the wrong slot. Adding a new one is one line in
`FanartArtworkMap`.

## Configuration

Fanart.tv requires an API key for all access, and the key belongs to whoever
installed the plugin: a key bundled with a release would be shared by every
install and rate limited accordingly. Get one at
[fanart.tv/get-an-api-key](https://fanart.tv/get-an-api-key/).

| Setting | Default | |
|---|---|---|
| API Key | unset | Your project key, sent as `api_key`. Without it the plugin stays loaded, logs once per sweep, and does nothing |
| Personal API Key | unset | Optional, sent as `client_key`. It only changes freshness: new uploads reach a project key after seven days and a personal key after two |
| Sweep Interval | 7 days | Read when the recurring job is registered, so a change needs a restart |
| Include Movies | on | Movie artwork uses the same API and key, keyed on the TMDB movie ID |
| Maximum Images Per Type | 5 | Per entity and per image type, most-liked first |
| Download Artwork | on | Marks the artwork as desired, which is what makes the server download it. With it off, the artwork is registered and visible but only fetched on demand |
| Remove Withdrawn Artwork | on | Removes this plugin's links to artwork Fanart.tv no longer lists |

No setting is marked `[Required]`: a required setting that is unset fails
validation, and a configuration that fails validation stops the whole plugin
from loading. A missing key is checked for at runtime instead.

## Unverified against the live API

**The response models in this plugin have never seen a real Fanart.tv
response.** Fanart.tv requires an API key for all access and none was available
while the plugin was written, so every shape was modelled from Fanart.tv's own
published client
([fanart-tv/fanart.tv-api](https://github.com/fanart-tv/fanart.tv-api)) and its
documentation. Every test fixture is hand-written to match, and is labelled as
such in `tests/Fixtures/README.md` rather than implying it is a capture.

What that means:

- The tests prove what the plugin does with that shape: what is mapped, what is
  dropped, how artwork is ordered and capped, and what is written through the
  image manager. They cannot prove the shape is right.
- The parsing is built to survive being wrong about the details. Only `name` and
  the identifier fields are hard-coded; artwork is read from whatever top-level
  keys hold arrays, unknown fields are ignored rather than rejected, and every
  numeric field is read from either a JSON string or a JSON number.
- Everything that models the wire format lives in `source/Api/FanartModels.cs`,
  so verifying it later is one file, one diff, and a fixture replacement.
- One entry is a known guess: `tvposter` is not listed in the vendor client's
  typed fields although it is a real asset kind on the site. If it never
  arrives, that mapping simply never matches.

## Building

```sh
dotnet build
dotnet test
```

Drop `source/bin/Release/net10.0/Shoko.Plugin.Fanart.dll` into your Shoko data
directory's `plugins/` folder.

## Credits

All artwork comes from [Fanart.tv](https://fanart.tv/) and the people who upload
it there. This plugin only fetches it.

## License

MIT. See [LICENSE](LICENSE).
