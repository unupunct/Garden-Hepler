# Phone app and Home Assistant

Both talk to the **local API** built into the desktop app (menu: *Phone and Home Assistant*). The PC must be on and Garden Helper open.

## Phone

**Android app (`GardenHelper-0.1.0.apk`)**
1. Copy the `.apk` to the phone (USB, cloud drive, or `adb install GardenHelper-0.1.0.apk`).
2. Open it. If Android asks, allow *Install unknown apps* for the app you opened it from. The app is signed with a debug key (fine for your own phone; it is not a Play Store build).
3. First start: type the PC's address (for example `192.168.1.20:8765`) or paste the *phone link* from the Connect page. The link also carries the token, so you sign in once.
4. Menu (⋮) → *Change address* to point it somewhere else.

**Any phone, no install:** open the same link in Chrome or Safari and use *Add to Home screen*.

The app shows today's counts and coming-up list, tasks (tick off), animals (add eggs), greenhouse values and switches, and camera pictures. Camera passwords never leave the PC; only the picture does.

### Away from home (private)
Install **Tailscale** on the PC and the phone, sign in with the same account, and use the PC's `100.x.y.z` address. Nothing is opened on the router and the traffic is encrypted. Do not forward port 8765 to the internet: the API speaks plain HTTP.

## Home Assistant
The Connect page generates a ready-to-paste `configuration.yaml` block for your PC and token: REST sensors (tasks, eggs, plants to water, greenhouse temperature, treatment withdrawal), commands (mark a task done, open the greenhouse vent) and your cameras (HTTP Basic: any user name, the token as password).

Home Assistant can also *send* readings, for example soil moisture from a Zigbee sensor, to `POST /api/v1/greenhouse/{id}/readings` with `{"soil_moisture": 31}`.

## API reference
Header `Authorization: Bearer <token>` (or HTTP Basic with the token as password). The page files (`/`, `/app.js`, …) are public and contain no data.

| Method and path | Purpose |
|---|---|
| `GET /api/v1/summary` | counts, animals, greenhouses, coming up, month totals |
| `GET /api/v1/tasks`, `POST /api/v1/tasks/{id}/done` | open tasks, mark one done |
| `POST /api/v1/animals/{id}/production` `{"quantity": 12}` | add today's eggs or milk |
| `GET /api/v1/cameras`, `GET /api/v1/cameras/{id}/snapshot` | camera list, one picture |
| `POST /api/v1/greenhouse/{id}/switch/{vent\|fan\|water\|pump\|light}` `{"on": true}` or `{"auto": true}` | hold a switch by hand, or return it to auto |
| `POST /api/v1/greenhouse/{id}/readings`, `GET .../commands` | what the Raspberry Pi uses |

## Rebuilding the APK
Needs JDK 17 and the Android SDK (platform 34, build-tools 34.0.0). In the `android` folder run `gradlew assembleDebug`; the file appears under `app/build/outputs/apk/debug/`. For a Play Store release you would create your own signing key and use `assembleRelease`.
