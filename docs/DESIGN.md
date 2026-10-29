# Garden Helper: rethink and plan

Garden Helper started as a garden planner. It is turning into a **command center for the whole property**: land, plants, trees, animals, greenhouse, cameras, money. This page records an honest review and the order of work.

## Information architecture

| Area | Holds | Status |
|---|---|---|
| **Property** (home) | Outline on satellite map, structures, spaces, "what needs me now" | built |
| **Plants & trees** | Designer, plant encyclopedia, seeds, plant inventory (indoor too), trees | designer, encyclopedia, seeds built; inventory page, trees pending |
| **Animals** | Groups (30 chickens, ducks, rabbits, goats, 2 pigs), housing, production (eggs/day), treatments, rations and prices | planned |
| **Greenhouse** | Own layout, Raspberry Pi sensors, ventilation/water control, aquaponics/hydroponics | planned |
| **Cameras** | Network cameras, live picture, position, view angle, icon on the plan | **built (snapshot/MJPEG)** |
| **Money & notes** | Budget by category, feed/seed/treatment costs, free notes | planned |
| **Coming up** | One list: days / weeks / months, from tasks, seasons, treatments, weather | partly (alerts) |
| **Library** | Own ebooks, full-text and subject search | built |

Rule: every record belongs to a **place** (a structure or space on the property). Animals live in a coop, a camera watches a bed, a sensor sits in the greenhouse. That one link powers the overview, the plan and the "coming up" list.

## What works and what does not (honest)

Works: offline-first data, real geometry (area, overlap, zoom), outline → plan, reversible geo↔plan mapping, bilingual UI, tests on all rules, library search.

Does not work well yet:
- **Too many entry points.** Plants, inventory, seeds, designer and trees overlap. Fix: group the menu into *Property / Plants & trees / Animals / Greenhouse / Cameras / Money / Library / System*.
- **Several data models for "a thing on the land"** (bed, feature, tree, coop). Fix: keep `PropertyFeature` as the single place and hang everything off it.
- **No single "what do I do today" screen.** Fix: Coming up, fed by every module.
- **Satellite imagery stops at zoom 18** (no higher-resolution free tiles). Accept; trace by eye and measure on site.
- **RTSP cameras can be saved but not previewed** (WPF cannot play RTSP). Most cheap cameras also offer a snapshot or MJPEG address, which works now. Real RTSP needs a player library (LibVLC), planned.
- **Cameras cannot be found automatically** (ONVIF discovery is fragile on home networks). Adding by address is reliable; discovery comes later.
- Bird/animal identification by image and sound needs on-device models; only after cameras and the API are stable.

## Phone, Home Assistant and remote access (private)

Goal: reach the app from the phone at home or away, without exposing the PC to the internet.

1. **Companion API in the desktop app** (off by default). A small local HTTPS server, bearer-token login with a pairing code. Endpoints: status, alerts, tasks (list, tick off), journal (add), animals (log eggs, treatments), camera snapshots, sensor ingest from the Raspberry Pi.
2. **Private access from anywhere: Tailscale (or WireGuard)** on the PC and the phone. No router ports are opened; traffic is encrypted end to end; the same address works at home and away.
3. **Home Assistant**, two routes, simplest first:
   - **REST sensors**: Home Assistant reads the companion API (`rest` / `rest_command`). No code on the Home Assistant side; a ready YAML snippet goes in the docs.
   - **MQTT discovery**: the app publishes its own sensors (overdue tasks, eggs today, greenhouse temperature, next action). Later.
   Home Assistant entities (soil moisture, temperature) can also feed the greenhouse and plant records through the same API.
4. **Phone app.** A real `.apk` needs the Android SDK and a signing key, which this machine does not have set up. Plan: first an **installable web app (PWA)** served by the companion API (Chrome → "Add to Home screen" gives an app icon, full screen, camera snapshots, task list). It works on any phone with no build chain. Then, if wanted, wrap the same pages into an `.apk` (Capacitor) once the API is stable, and attach it to the GitHub Release.

Security rules: off by default; token per device, revocable; HTTPS only; never log tokens; camera passwords stay encrypted (Windows DPAPI) and are never sent to the phone, only snapshots.

## Execution order (smallest useful steps)

1. ✅ Cameras tab, add by address, test, live view, position, view angle, icon on the plan with hover preview and click-to-open. (This commit.)
2. Plant inventory page (data exists) + trees (health, seasonal actions, shade/roots on beds).
3. Animals module on top of places, with production, treatments (withdrawal periods) and rations/prices.
4. Money & notes; Coming up list.
5. Companion API + pairing + REST sensors for Home Assistant (docs snippet).
6. PWA for the phone; Tailscale setup guide.
7. Greenhouse layout and Raspberry Pi control panel (uses the API for sensor ingest).
8. APK wrapper, MQTT discovery, RTSP preview, wildlife detection.

Publishing to GitHub only when the owner says the app is done.
