# Database

SQLite file `gardenhelper.db`, foreign keys on, managed by EF Core migrations (`src/GardenHelper.Core/Data/Migrations`).

| Table | Purpose |
|---|---|
| Settings | key/value (language, theme, units) |
| Gardens | name, region, frost dates (month/day), latitude/longitude, climate JSON, satellite file + cm per pixel |
| Beds | plan objects: kind, X/Y/width/height (cm), rotation, shade %, notes; FK to Gardens by id |
| Plantings | plant id (from content), quantity, planted date, status; FK to Beds, **cascade delete** |
| Tasks, Journal, Seeds | user records |
| Books, BookSections, BookSubjects | library; sections cascade with their book |
| BookSectionFts | FTS5 external-content index over BookSections (`unicode61 remove_diacritics 2`), kept in sync by triggers |

Plant, relationship and rotation content is **not** in the database (it lives in JSON), so app updates never overwrite user records. Plants are referenced by their string id.

Planned tables (not created yet): boundaries/geometry, georeference control points, sunlight observations, rotation plans and assignments, design versions.

Backups use `VACUUM INTO` and are checked with `PRAGMA integrity_check` plus a table check before they are accepted or restored.
