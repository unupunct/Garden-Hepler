# Roadmap

The product specification is large. This file records what is built and what is not, so that nothing is claimed that does not work.

## Built and tested (v0.1.0)

- Core: SQLite + EF Core with migrations (including FTS5 full-text index), settings, backups with verification and restore.
- Library: 10 format readers, import with duplicate detection, subject extraction (English/Romanian stop words), FTS search ignoring accents, in-book search, reader with highlighting.
- Knowledge: plant JSON loader with validation, rotation-rule JSON, planting-window calculator.
- Designer (Phase 1): geometry (`WorldTransform`, polygon area, rotated rectangles, SAT overlap, point-in-polygon), spacing-based plant count, undo/redo, beds with plantings and history, month planner, rotation advisor, alert rules.
- Site: offline solar calculator, opt-in climate and satellite fetch.
- 111 automated tests; installer build, silent install/launch/uninstall verified on one machine.

## Cameras, motion and wildlife identification (not started)

Requested for the future. Nothing of it exists in the app yet, and there are deliberately no placeholder buttons. Proposed approach, to be validated on real hardware:

- **Cameras**: link each camera to a place; read RTSP/ONVIF streams on the local network (LibVLCSharp or FFmpeg), credentials stored with Windows DPAPI, nothing leaves the computer.
- **Motion tracking**: frame differencing and region masking in GardenHelper.Core (pure and unit-testable on recorded clips), with per-camera sensitivity and quiet zones; clips and snapshots saved into the place's history.
- **Identification**: an on-device model run through ONNX Runtime. Birds by sound (BirdNET is licensed CC BY-NC-SA: acceptable only for non-commercial use, to be confirmed) and by image, animals by camera-trap style models. Results shown as *suggestions with a confidence value*, never as fact, and always correctable by you.
- Needs: test cameras, a choice of models with compatible licenses, and storage and privacy settings. Estimated as its own phase.

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
