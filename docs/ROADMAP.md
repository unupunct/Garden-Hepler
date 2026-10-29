# Roadmap

The product specification is large. This file records what is built and what is not, so that nothing is claimed that does not work.

## Built and tested (v0.1.0)

- Core: SQLite + EF Core with migrations (including FTS5 full-text index), settings, backups with verification and restore.
- Library: 10 format readers, import with duplicate detection, subject extraction (English/Romanian stop words), FTS search ignoring accents, in-book search, reader with highlighting.
- Knowledge: plant JSON loader with validation, rotation-rule JSON, planting-window calculator.
- Designer (Phase 1): geometry (`WorldTransform`, polygon area, rotated rectangles, SAT overlap, point-in-polygon), spacing-based plant count, undo/redo, beds with plantings and history, month planner, rotation advisor, alert rules.
- Site: offline solar calculator, opt-in climate and satellite fetch.
- 111 automated tests; installer build, silent install/launch/uninstall verified on one machine.

## Next (in order)

1. **Phase 1 completion** — multi-selection, align/distribute, layers, copy/paste, individual plant markers inside beds (positions from `PlantLayout.Positions` are already computed and tested), PNG/PDF export, print.
2. **Phase 2 — Real-world location** — (property outline from map pins is done; remaining:) scale calibration against a known distance, control points, rotation of the plan against north, GeoJSON import. Address search needs a geocoding provider choice (Nominatim has strict usage rules; a keyed provider is preferable). Projected coordinates need a tested projection library (UTM zones, Stereo 70/EPSG:3844): not started, and nothing in the app currently treats latitude/longitude as metres.
3. **Phase 3 — Planting intelligence** — variety-level data, soil temperature rules, recommendation scoring with hard constraints and modes (beginner, production, permaculture, year-round, food forest).
4. **Phase 4 — Rotation and succession** — multi-year plan generation across beds, succession intervals, cover-crop periods.
5. **Phase 5 — Advanced planning** — obstacle-based shade and sunlight heatmap, automated layout alternatives, seasonal simulator with planned/current/mature views, irrigation and soil overlays.
6. **Phase 6 — Quality** — large-garden performance profiling (viewport rendering, spatial index), accessibility audit with a screen reader, upgrade-path testing across installer versions.

## Known limitations

- Rotation rules cover eight families and are general guidance; crop-specific exceptions are not modelled yet.
- Sun hours use day length and a user shade percentage; there is no obstacle geometry yet, so results are for an unobstructed horizon.
- The canvas uses WPF elements per object, which is fine for hundreds of objects but has not been tested with thousands.
- Plant records are starter content (106 species, Romania-focused; varieties and regional adjustments are not modelled); see `content/SOURCES.md`.
