# Architecture

```
src/GardenHelper.Core   (net10.0, no UI)
  Domain/       entities (Garden, GardenBed, BedPlanting, Book, BookSection, …)
  Data/         DbContext, migrations, AppPaths, context factory
  Library/      extractors per format, SubjectExtractor, LibraryService (import, FTS search), ReaderPreparer
  Knowledge/    plant/relationship/rotation JSON models, KnowledgeBase, PlantingCalculator
  Design/       WorldTransform, GeometryMath, PlantLayout, UndoStack   (pure, unit-tested)
  Site/         SolarCalculator, ClimateAnalyzer/ClimateService, SatelliteTileService, MapMath
  Services/     Repository, SettingsService, BackupService, GlobalSearchService, ActionAdvisor, RotationAdvisor, CalendarPlanner
src/GardenHelper.App    (net10.0-windows10.0.19041.0, WPF, MVVM with CommunityToolkit.Mvvm)
  ViewModels/   one per page; PageViewModel gives busy/error handling
  Views/        XAML pages; code-behind only for pointer/drag/keyboard glue
  Infrastructure/  localization (Strings.cs en/ro), converters, dialogs, theme, preferences
tests/GardenHelper.Tests (xUnit)
```

Decisions

- **Core has no UI types**, so geometry, rules and calculators are tested without a window.
- **Short-lived DbContexts** from a factory; a small generic `Repository` for simple records.
- **EF migrations** for schema; the FTS5 table and its triggers are created in a hand-written migration. Schema upgrades run at start-up.
- **Content as data**: plants, relationships and rotation rules are JSON, validated, overridable by the user.
- **Advice is explainable**: `GardenAlert.Why`, `RotationCheck.RuleReason` and the planting-window notes carry the data and assumption behind each suggestion.
- **Network code is isolated** (`Site/Climate.cs`) and only runs on an explicit click.
- **Coordinates**: the plan is a flat local metric plane in centimetres. Latitude/longitude are stored separately and only used for sun and imagery. Projected CRS support is not implemented (see ROADMAP).
- Pinned packages: EF Core 10.0.14, CommunityToolkit.Mvvm 8.4.2, PdfPig 1.7.0-custom-5 (no stable release available), HtmlAgilityPack 1.13.1, WebView2 1.0.4314.35, Serilog 4/7.
