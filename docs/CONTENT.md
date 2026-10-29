# Content guide

Plant knowledge is plain JSON in `content/` (shipped) and `%LOCALAPPDATA%\GardenHelper\content\` (yours; same names override).

```
content/
  plants/*.json          list of plant records
  relationships.json     companion relationships
  rotation-rules.json    rest period per crop family
  SOURCES.md             provenance and status
```

## Plant record (abridged)

```json
{
  "id": "tomato",                       // lower-case, no spaces, unique
  "names": { "en": ["Tomato"], "ro": ["Roșie", "Tomată"] },
  "scientific": "Solanum lycopersicum",
  "family": "Solanaceae", "rotationFamily": "Solanaceae",
  "category": "vegetable", "lifecycle": "annual",
  "sun": "full", "water": "moderate", "frostTolerance": "tender",
  "summary": { "en": "…", "ro": "…" },
  "tips": { "en": ["…"], "ro": ["…"] },
  "sowing": { "anchor": "lastFrost",    // or firstFrost
              "indoors": { "min": -8, "max": -6 },   // weeks relative to the anchor frost date
              "transplant": { "min": 1, "max": 2 } },
  "daysToMaturity": { "min": 60, "max": 100 },
  "spacingCm": { "min": 45, "max": 60 }, "rowSpacingCm": { "min": 75, "max": 100 },
  "roles": ["food"], "nitrogenFixing": false,
  "confidence": "moderate", "reviewed": "2026-10-29",
  "sources": [ { "title": "…", "organization": "…", "url": "https://…" } ]
}
```

Leave a field out when you do not know it; never fill it with a guess. Unknown values are shown as missing, not invented.

Allowed values: `sun` full | full-partial | partial | shade; `water` low | moderate | high; `frostTolerance` tender | half-hardy | hardy.

## Companion relationship

`effect` positive | negative | mixed | uncertain. `evidence` **research** | **recommendation** | **traditional** | **anecdotal**. Always explain the reason and add `conditions` when the effect depends on something. Do not present folklore as science.

## Rotation rule

`{ "family": "Solanaceae", "minYears": 3, "reason": { "en": "…", "ro": "…" } }` — the bed should rest that many years from the family. Rules advise; they never block.

## Validation

`KnowledgeBase.Validate()` (run by the test suite on shipped content) reports: bad ids, missing scientific name or English text, no source, unknown companion plants, invalid evidence levels, reversed ranges, broken JSON. Fix everything it reports before sharing a file.
