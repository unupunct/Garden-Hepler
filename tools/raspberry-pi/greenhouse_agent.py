#!/usr/bin/env python3
"""Garden Helper greenhouse agent for a Raspberry Pi.

Every few seconds it reads the sensors, sends the values to Garden Helper and
switches the relays as Garden Helper asks (vent, fan, water valve, pump, light).

Start with --simulate to try the connection without any hardware:

    python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOURTOKEN --greenhouse 1 --simulate

Needs only the Python standard library. Real sensors and relays are optional:
fill in read_sensors() and the PINS table for your wiring (see README.md).
The relay safety rule: if Garden Helper cannot be reached for --failsafe
seconds, the Pi switches everything to its safe state (see SAFE_STATE).
"""
import argparse
import json
import math
import random
import time
import urllib.error
import urllib.request

# BCM pin numbers of the relays. Change to match your wiring. Active-low relay boards: set ACTIVE_LOW = True.
PINS = {"vent": 17, "fan": 27, "water": 22, "pump": 23, "light": 24}
ACTIVE_LOW = True
# What each switch does when the app cannot be reached. Vent open, fan on, water and light off, pump on (fish need circulating water).
SAFE_STATE = {"vent": True, "fan": True, "water": False, "pump": True, "light": False}


def read_sensors(simulate):
    """Return a dict of readings. Keys: air_temp, humidity, soil_moisture, light, water_temp, ph, ec, water_level.

    Replace the real branch with your sensor code, for example:
      - DHT22 / BME280 for air_temp and humidity (adafruit-circuitpython-dht / bme280 libraries)
      - DS18B20 for water_temp (reads /sys/bus/w1/devices/28-*/w1_slave)
      - a capacitive soil sensor on an ADS1115 for soil_moisture (percent)
    Leave out a key when the sensor is not connected.
    """
    if simulate:
        t = time.time()
        return {
            "air_temp": round(22 + 9 * math.sin(t / 120) + random.uniform(-0.3, 0.3), 1),
            "humidity": round(60 + 15 * math.sin(t / 200) + random.uniform(-1, 1), 1),
            "soil_moisture": round(max(0, 45 - (t % 600) / 20 + random.uniform(-1, 1)), 1),
        }
    return {}  # no sensors configured yet


class Relays:
    def __init__(self, simulate):
        self.simulate = simulate
        self.state = {}
        self.gpio = None
        if not simulate:
            import RPi.GPIO as GPIO  # available on Raspberry Pi OS
            GPIO.setmode(GPIO.BCM)
            for pin in PINS.values():
                GPIO.setup(pin, GPIO.OUT)
            self.gpio = GPIO

    def set(self, key, on):
        if key not in PINS or self.state.get(key) == on:
            return
        self.state[key] = on
        if self.gpio:
            self.gpio.output(PINS[key], (not on) if ACTIVE_LOW else on)
        print(f"  switch {key}: {'ON' if on else 'off'}")


def call(url, token, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(url, data=data, method="POST" if body is not None else "GET")
    req.add_header("Authorization", f"Bearer {token}")
    req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=8) as r:
        return json.loads(r.read().decode())


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--server", required=True, help="for example http://192.168.1.20:8765")
    ap.add_argument("--token", required=True)
    ap.add_argument("--greenhouse", type=int, required=True, help="the greenhouse number shown in Garden Helper's command")
    ap.add_argument("--interval", type=int, default=15, help="seconds between readings")
    ap.add_argument("--failsafe", type=int, default=300, help="seconds without contact before switching to the safe state")
    ap.add_argument("--simulate", action="store_true", help="fake sensors and no relays")
    a = ap.parse_args()

    base = f"{a.server.rstrip('/')}/api/v1/greenhouse/{a.greenhouse}"
    relays = Relays(a.simulate)
    last_ok = time.time()
    while True:
        try:
            readings = read_sensors(a.simulate)
            if readings:
                reply = call(f"{base}/readings", a.token, readings)
            else:
                reply = call(f"{base}/commands", a.token)
            for key, on in reply.get("commands", {}).items():
                relays.set(key, bool(on))
            last_ok = time.time()
            print(time.strftime("%H:%M:%S"), readings or "(no sensors)")
        except (urllib.error.URLError, OSError, ValueError) as e:
            print(time.strftime("%H:%M:%S"), "cannot reach Garden Helper:", e)
            if time.time() - last_ok > a.failsafe:
                print("  failsafe: switching to the safe state")
                for key, on in SAFE_STATE.items():
                    relays.set(key, on)
        time.sleep(a.interval)


if __name__ == "__main__":
    main()
