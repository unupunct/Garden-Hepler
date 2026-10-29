# Raspberry Pi greenhouse agent

`greenhouse_agent.py` connects a Raspberry Pi to Garden Helper's **Greenhouse** page. It needs only Python 3 (no packages) to talk to the app.

## 1. In Garden Helper
1. Open **Greenhouse**, create a greenhouse (or pick one traced on the map).
2. In *Raspberry Pi connection* tick **Accept connections from the Raspberry Pi** and press Apply. Allow the app through the Windows firewall on **private** networks.
3. Copy the command shown there. It contains the secret token and the greenhouse number.

## 2. On the Pi
```bash
python3 greenhouse_agent.py --server http://<PC address>:8765 --token <TOKEN> --greenhouse <N> --simulate
```
`--simulate` sends fake values and switches no relays, so you can check the connection first. Remove it once your sensors are wired.

## 3. Wiring (your hardware)
Fill in two places in the script:
- `read_sensors()` returns a dictionary with any of: `air_temp` (°C), `humidity` (%), `soil_moisture` (%), `light` (lux), `water_temp` (°C), `ph`, `ec` (mS/cm), `water_level` (%). Leave out sensors you do not have. Typical parts: BME280 or DHT22 (air), DS18B20 (water temperature), capacitive soil sensor via ADS1115, a pH probe board for hydroponics/aquaponics.
- `PINS` maps vent, fan, water valve, pump and light to GPIO pins of a relay board. Set `ACTIVE_LOW` to match the board.

**Safety:** mains-powered pumps, valves and heaters must be wired by someone qualified, with a fuse or RCD. The agent's failsafe (default 5 minutes without contact) moves every switch to `SAFE_STATE`: vent open, fan on, water and light off, pump on. Change it to suit your system, and keep a hardware thermostat as a last line of defence, because software can fail.

## 4. Run at boot
Create `/etc/systemd/system/greenhouse-agent.service` with `ExecStart=/usr/bin/python3 /home/pi/greenhouse_agent.py --server ... --token ... --greenhouse N` and `Restart=always`, then `sudo systemctl enable --now greenhouse-agent`.

## Security
The connection is plain HTTP with a secret token. Keep it on your home network, or reach it from outside only through a VPN such as Tailscale or WireGuard. Do not forward the port on your router. Use *Make a new token* in the app if the token leaks.

## API (for other tools)
All requests need the header `Authorization: Bearer <token>`.
- `POST /api/v1/greenhouse/{id}/readings` with JSON like `{"air_temp": 21.5, "humidity": 60}` stores readings and returns `{"stored": n, "commands": {"vent": true, ...}}`.
- `GET /api/v1/greenhouse/{id}/commands` returns the switch states the Pi should apply.
- `GET /api/v1/status` checks that the app is reachable.
