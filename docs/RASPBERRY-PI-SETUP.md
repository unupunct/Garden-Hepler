# Raspberry Pi setup: first time, step by step

This guide takes you from a new Raspberry Pi in the box to live greenhouse (or garden bed) readings in Garden Helper. You do not need to know Linux. Allow about 45 minutes, plus extra time for wiring sensors.

**What the Pi does:** every 15 seconds it reads its sensors, sends the values to Garden Helper on your PC, and switches relays (vent, fan, water valve, pump, light) as the app decides. The app decides, the Pi obeys: if the PC is off for 5 minutes, the Pi moves everything to a safe state by itself.

## What you need

| Item | Notes |
|---|---|
| Raspberry Pi 3, 4, 5 or Zero 2 W | Any with Wi-Fi or a network cable |
| microSD card, 16 GB or more | Class 10 / A1 |
| Power supply for your Pi model | Use the official one: weak supplies cause strange crashes |
| Your Windows PC with Garden Helper | On the same home network (Wi-Fi or cable) |
| Optional sensors | DS18B20 waterproof probe (water temperature), BME280 (air temperature and humidity), capacitive soil sensor with an ADS1115 |
| Optional relay board | 4 or 5 channels for vent, fan, water, pump, light |

You can do **steps 1 to 6 with no sensors at all** using the simulator. Do that first to prove the connection works.

---

## Step 1. Put the system on the SD card

1. On your PC install **Raspberry Pi Imager** from raspberrypi.com/software.
2. Choose your Pi model, then the system **Raspberry Pi OS Lite (64-bit)**. (Lite has no desktop. It is lighter and enough.)
3. Choose your SD card, then press **Next** and **Edit settings**:
   - **Hostname:** `greenhouse` (so you can reach it as `greenhouse.local`)
   - **Username and password:** keep `pi` and choose a password you will remember
   - **Wi-Fi:** your network name and password, and your country
   - **Services:** tick **Enable SSH** with password login
4. Write the card. Put it in the Pi and connect the power. The first start takes a couple of minutes.

## Step 2. Connect to the Pi from your PC

1. On the PC open **Windows Terminal** (or PowerShell) and type:

   ```
   ssh pi@greenhouse.local
   ```

2. Answer `yes` to the question about the fingerprint, then type your password. You are now typing commands on the Pi. The prompt looks like `pi@greenhouse:~ $`.
3. If `greenhouse.local` is not found, look up the Pi's address in your router's list of devices (for example `192.168.1.57`) and use `ssh pi@192.168.1.57`.
4. Update the Pi once:

   ```
   sudo apt update && sudo apt full-upgrade -y
   ```

## Step 3. In Garden Helper: allow the Pi to connect

1. Open **Greenhouse** and create a greenhouse (or pick one you traced on the map). For a garden bed outside, see "Outdoor beds" below.
2. Scroll to **Raspberry Pi connection**. Tick **Accept connections from the Raspberry Pi** and press **Apply**.
3. Windows asks about the firewall. Allow Garden Helper on **private networks only**.
4. The page now shows your **PC address** (for example `192.168.1.20`), the **secret token** and a ready-made **command**. Keep this page open.

## Step 4. Copy the agent to the Pi

On your PC (a new Terminal window, not the one connected to the Pi), from the Garden Helper folder:

```
scp tools\raspberry-pi\greenhouse_agent.py pi@greenhouse.local:~
```

(In the installed app the script sits in the `tools\raspberry-pi` folder next to the app.)

## Step 5. Test the connection

Back in the Terminal connected to the Pi, paste the command from the app and add `--check`:

```
python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOUR_TOKEN --greenhouse 8 --check
```

You should see `Connected: {...}`. If you see an error, the message lists what to check: the app is open and accepting connections, the address and port are right, Windows allows the app on private networks, both devices are on the same network.

## Step 6. Try it with fake sensors

```
python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOUR_TOKEN --greenhouse 8 --simulate
```

After a few seconds the **Greenhouse** page in the app shows temperature, humidity and soil moisture tiles that move. Press `Ctrl+C` to stop. The connection works.

---

## Step 7. Connect real sensors

Switch the Pi off (`sudo shutdown now`) and unplug it before wiring.

### Water temperature: DS18B20
- Red wire to **3.3 V** (pin 1), black to **GND** (pin 6), yellow data to **GPIO 4** (pin 7), with a **4.7 kΩ resistor** between data and 3.3 V.
- Enable it: `sudo nano /boot/firmware/config.txt`, add the line `dtoverlay=w1-gpio` at the bottom, save (`Ctrl+O`, `Enter`, `Ctrl+X`) and `sudo reboot`.
- Run the agent with `--ds18b20`. The app shows **Water temperature**.

### Air temperature and humidity: BME280
- **VIN** to 3.3 V (pin 1), **GND** to GND (pin 9), **SDA** to GPIO 2 (pin 3), **SCL** to GPIO 3 (pin 5).
- Enable I2C: `sudo raspi-config`, **Interface Options**, **I2C**, Yes.
- Install the library once: `sudo apt install -y python3-smbus2 python3-pip` then `pip3 install --break-system-packages RPi.bme280`.
- Run the agent with `--bme280`. The app shows **Air temperature** and **Humidity**.

### Anything else (soil moisture, pH, light)
Open `greenhouse_agent.py`, find `read_sensors` and add your reading as another key: `soil_moisture` (%), `light` (lux), `ph`, `ec` (mS/cm), `water_level` (%). The app understands those names. Leave out sensors you do not have.

You can combine: `--ds18b20 --bme280`.

## Step 8. Relays: vent, fan, water, pump, light

> **Safety.** Wiring mains-powered pumps, valves or heaters must be done by a qualified person, with a fuse or an RCD (residual-current device). The relay board only switches; never power a heater from the Pi. Keep a plain hardware thermostat as a last line of defence: software can fail.

- Use a relay board made for Raspberry Pi with its own supply for the relays.
- Default pins (BCM numbers): vent 17, fan 27, water 22, pump 23, light 24. Change `PINS` at the top of the script if you wire differently. Most boards switch on when the pin goes **low**; that is `ACTIVE_LOW = True`. If your board is the other way round, set it to `False`.
- The **safe state** when the app cannot be reached for 5 minutes is: vent open, fan on, water off, pump on, light off. Edit `SAFE_STATE` to suit your system (for fish, keep the pump on).
- In the app, each switch can be **Auto** (the app decides from your limits) or **Manual** (you switch it).

## Step 9. Start at boot

So you never have to log in again:

1. Make the service file: run your command with `--print-service` added, for example

   ```
   python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOUR_TOKEN --greenhouse 8 --ds18b20 --bme280 --print-service | sudo tee /etc/systemd/system/greenhouse-agent.service
   ```

2. Turn it on:

   ```
   sudo systemctl daemon-reload
   sudo systemctl enable --now greenhouse-agent
   ```

3. Look at it any time with `systemctl status greenhouse-agent` and `journalctl -u greenhouse-agent -f`.

The PC address can change when your router restarts. In your router, give the PC a fixed ("reserved") address so the Pi always finds it.

## Reaching the app from outside the house

Do **not** open the port on your router: the connection is plain HTTP with a token. Use a VPN instead. The easiest is **Tailscale** (free for personal use): install it on the PC and on the Pi (`curl -fsSL https://tailscale.com/install.sh | sh`, then `sudo tailscale up`), then use the PC's Tailscale address (`100.x.y.z`) as `--server`.

## Outdoor beds and other places

The agent works for **any place**, not only greenhouses. For a garden bed or the whole vegetable garden, put the sensors on a Pi (or a small ESP32), and use the **place number** instead of the greenhouse number. Then in the **Garden designer** select the bed and choose that place under **Sensor source**: the bed gets a coloured dot (green = soil moisture fine, red = dry, blue = wet) and the plant card shows the live values. Outdoor places only store readings; they do not control relays.

## If something does not work

| Symptom | What to try |
|---|---|
| `ssh` says host not found | Use the Pi's IP address from your router instead of `greenhouse.local` |
| `--check` cannot reach the app | App open? **Accept connections** ticked and Applied? Windows firewall on private network? Same Wi-Fi? |
| `--check` says refused (401) | Token typed wrongly. Copy it again from the app, or press **Make a new token** and update the Pi |
| Tiles say "no data" | The agent is not running, or stopped: run it again and watch its output |
| Tiles say data is old | The Pi lost the network. Check Wi-Fi signal near the greenhouse |
| BME280 not available | Check wiring and `sudo i2cdetect -y 1` (install `i2c-tools`); the address is usually 76 or 77 |
| DS18B20 shows nothing | Check the 4.7 kΩ resistor and the `dtoverlay=w1-gpio` line, then reboot |
| Relays click the wrong way | Flip `ACTIVE_LOW` |
