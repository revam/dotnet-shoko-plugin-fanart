# Fixtures

**Every file in this folder is hand-written. None of them is a capture of a real
Fanart.tv response.**

Fanart.tv requires an API key for all access, and no key was available while
this plugin was written, so the fixtures reproduce the response shape documented
by Fanart.tv's own API client
([fanart-tv/fanart.tv-api](https://github.com/fanart-tv/fanart.tv-api), `src/index.d.ts`
and its README) rather than anything the live API actually returned.

What that means for the tests that read them:

- They prove the plugin does what it intends with that shape: which asset kinds
  are mapped, which are dropped, how artwork is ordered and capped, what a
  missing or malformed field does, and what is written through the image
  manager.
- They cannot prove the shape itself is right. If the live API differs, these
  tests will keep passing and the plugin will still be wrong.

Each fixture carries a `_fixture_note` field saying the same thing, which
doubles as a check that an unknown scalar field in a response is ignored rather
than breaking parsing.

Replacing these with real captures, once a key is available, is the single most
valuable change anyone can make to this repository. Capture with:

```sh
curl -s "https://webservice.fanart.tv/v3.2/tv/81797?api_key=$FANART_API_KEY" | jq . > tv-81797.json
curl -s "https://webservice.fanart.tv/v3.2/movies/12429?api_key=$FANART_API_KEY" | jq . > movie-12429.json
```

then delete the `_fixture_note` fields, update the filenames in
`FanartFixtures`, and drop this warning.
