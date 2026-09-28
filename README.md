# Shoko Fanart.tv Artwork Plugin

A [Shoko](https://shokoanime.com/) plugin that adds series and movie artwork
from [Fanart.tv](https://fanart.tv/) to the TMDB entries in your collection, and
to TheTVDB's series when a plugin provides them, as one of Shoko's image
contributors.

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

The contributor covers these source and kind pairs:

| Source | Kind | Looked up by |
|---|---|---|
| `tmdb` | series | The TheTVDB ID TMDB lists for the show |
| `tmdb` | movie | The TMDB movie ID |
| `tvdb` | series | Its own TheTVDB ID. Only there when a plugin registered the `tvdb` source |

Every pair starts on, and an admin can turn each one off under the server's
image contributor settings (`PUT /api/v3/Metadata/ImageContributor/{id}`),
which also removes the links this plugin added on that pair.

## How it works

- **When it runs.** The plugin has no schedule of its own. Whenever Shoko
  refreshes an entry's images (after the owning source's image job, TMDB's for
  a show or movie, after a refresh that fetched images, or when someone asks
  for the entry's images through an image action), it queues one
  `DownloadContributedImagesJob` for this contributor. That job hands each
  series or movie of an enabled pair to the plugin, which answers with the
  artwork Fanart.tv lists for it. At most two of these jobs run at once, and
  every request also waits on the plugin's own rate limiter.
- **Keying, for shows.** Fanart.tv's TV API is keyed by TheTVDB ID and has no
  other way in. A TMDB show carries a `TvdbShowID`, TMDB is what supplies that
  translation, and Shoko already stores it, so the whole lookup path is
  `TMDB show -> TvdbShowID -> GET /v3.2/tv/{tvdb_id}`. No Trakt, no TheTVDB API
  key, and no community mapping list. A TheTVDB series is looked up by its own
  ID, and any other series by the TheTVDB ID it lists among its cross-source
  IDs.
- **Shows with no TheTVDB ID are skipped.** On a real 6,689 show library, 4,864
  shows carry one, so roughly a quarter of shows have no lookup path here at
  all. They are logged at Debug and left alone rather than being guessed at
  from their titles, because a wrong artwork match is worse than no artwork.
- **Keying, for movies.** The movie API accepts a TMDB or an IMDB ID, and Shoko
  always has the TMDB one for a TMDB movie, so movies need no translation step
  and no extra coverage caveat. `ImdbMovieID` is stored on TMDB movies as well
  and is not needed.
- **What the artwork is attached to.** The TMDB show or movie itself, not the
  shoko series in front of it. A shoko series already reads the images of
  everything it is linked to, so artwork attached to the TMDB show appears on
  the series anyway, survives the series being removed and re-added, and is not
  duplicated when two shoko series link to the same TMDB show. The core keeps
  the links under the `fanart-tv` source, apart from TMDB's own.
- **Registering the source and template URL.** The plugin registers the
  `fanart-tv` source, which every image and link it writes is attributed to.
  Shoko stores one template URL per image source and rebuilds a download URL as
  `string.Format(template, resourceID)`. It keeps defaults for AniDB and TMDB
  only, so the plugin registers `https://assets.fanart.tv/fanart/{0}` as the
  Fanart.tv default on every start. A template you set yourself, say for a
  mirror of your own, takes precedence over it. Each image's resource ID is the
  rest of its asset URL. A URL that is not a full-size Fanart.tv asset URL, or
  whose path would not fit the 128 character column, is skipped rather than
  stored as something that cannot be downloaded.
- **Ordering.** Within one Shoko image type, the asset kind ranks first and the
  number of likes breaks ties inside a kind, with the resource ID as the final
  tie-break so repeated refreshes do not reshuffle unchanged artwork. That is
  why a 4K background outranks a 1080p one with more likes, and an `hdtvlogo`
  outranks a `clearlogo` with four times the likes: the higher resolution
  version of the same thing wins. The server then orders that list by your
  preferred image languages.
- **What is downloaded.** The plugin offers every mapped image, and the server
  picks which to download from its image settings for the Fanart.tv source (or
  the shared defaults): a switch and a maximum count per image type, and a
  language order. Disc art is linked but not downloaded, since no image setting
  covers discs.
- **Withdrawn artwork.** An image Fanart.tv no longer lists is unlinked by the
  server on the next refresh, which is how a deleted or replaced upload stops
  being offered. Only this plugin's links are touched, and the image itself is
  never deleted, only the link. When Fanart.tv answers "not found" for the
  whole show or movie, or the key is missing or rejected, the links already
  there are left as they are.
- **Refreshing by hand.** The server's own image actions, such as "Update TMDB
  Images - Force" on a series, queue this contributor too, so the plugin
  registers no actions of its own.

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

## Installation

### GUI (Recommended)

1. Open the Shoko Web UI and navigate to **Settings → Plugins → Repositories**.
2. Add the manifest URL:
   ```
   https://raw.githubusercontent.com/revam/dotnet-shoko-plugin-fanart/metadata/manifest.json
   ```
3. Go to **Settings → Plugins → Browse** and find **Fanart.tv Artwork**.
4. Click **Install** on the desired version.
5. Restart Shoko.

### Manual

1. Download the latest `Shoko.Plugin.Fanart-<version>-any.zip` from the
   [Releases](../../releases) page.
2. Extract the ZIP and place `Shoko.Plugin.Fanart.dll` into your Shoko
   **Plugins** folder.
3. Restart Shoko.

## Configuration

Fanart.tv requires an API key for all access, and the key belongs to whoever
installed the plugin: a key bundled with a release would be shared by every
install and rate limited accordingly. Get one at
[fanart.tv/get-an-api-key](https://fanart.tv/get-an-api-key/).

| Setting | Default | |
|---|---|---|
| API Key | unset | Your project key, sent as `api_key`. Without it the plugin stays loaded, logs once per start, and does nothing |
| Personal API Key | unset | Optional, sent as `client_key`. It only changes freshness: new uploads reach a project key after seven days and a personal key after two |

Everything else is the server's: which pairs the contributor is on for (movies
included), under its image contributor settings, and how many images of each
type are downloaded, under its image settings for the Fanart.tv source.

No setting is marked `[Required]`: a required setting that is unset fails
validation, and a configuration that fails validation stops the whole plugin
from loading. A missing key is checked for at runtime instead.

## Verified against the live API

The response models were written from Fanart.tv's own published client
([fanart-tv/fanart.tv-api](https://github.com/fanart-tv/fanart.tv-api)) and its
documentation before any key was available, and were confirmed against the live
API on 2026-09-18: a show through `v3.2/tv` and nine linked movies through
`v3.2/movies`, mapping 150 artworks between them with nothing dropped as
unparseable.

The test fixtures are still hand-written rather than captures, and are labelled
as such in `tests/Fixtures/README.md`. What they prove is unchanged: what the
plugin does with a given shape, what is mapped, what is dropped, how artwork is
ordered, and what the contributor answers the server with.

The parsing is built to survive being wrong about the details anyway. Only
`name` and the identifier fields are hard-coded; artwork is read from whatever
top-level keys hold arrays, unknown fields are ignored rather than rejected, and
every numeric field is read from either a JSON string or a JSON number.
Everything that models the wire format lives in `source/Api/FanartModels.cs`, so
correcting it later is one file and one diff.

`tvposter` is worth a note: it is not listed in the vendor client's typed
fields, and was mapped on the guess that the site really serves it. A live
response carried eight of them, so the guess was right and the mapping stays.

## Building

```sh
dotnet build
dotnet test
```

Drop `source/bin/Release/net10.0/Shoko.Plugin.Fanart.dll` into your Shoko data
directory's `plugins/` folder.

To check it against a running server: set an API key, then
`GET /api/v3/Metadata/ImageContributor` should list "Fanart.tv" with the pairs
above. Run the "Update TMDB Images - Force" action on a series linked to a
show with a TheTVDB ID, wait for its `Download Contributed Images` job, and the
series' images (`GET /api/v3/Series/{id}/Images?includeDisabled=true`) should
hold `fanart-tv` artwork.

## Credits

All artwork comes from [Fanart.tv](https://fanart.tv/) and the people who upload
it there. This plugin only fetches it.

## License

MIT. See [LICENSE](LICENSE).
