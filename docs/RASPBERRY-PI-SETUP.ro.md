# Configurarea Raspberry Pi: prima dată, pas cu pas

Acest ghid te duce de la un Raspberry Pi nou, scos din cutie, până la citiri în direct din seră (sau de la un strat) în Garden Helper. Nu trebuie să știi Linux. Calculează circa 45 de minute, plus timpul pentru conectarea senzorilor.

**Ce face Pi-ul:** la fiecare 15 secunde citește senzorii, trimite valorile la Garden Helper de pe PC și pornește sau oprește releele (ventilație, ventilator, vană de apă, pompă, lumină) așa cum decide aplicația. Aplicația decide, Pi-ul execută: dacă PC-ul este oprit 5 minute, Pi-ul trece singur totul într-o stare sigură.

## Ce îți trebuie

| Ce | Observații |
|---|---|
| Raspberry Pi 3, 4, 5 sau Zero 2 W | Orice model cu Wi-Fi sau cablu de rețea |
| Card microSD de 16 GB sau mai mare | Clasa 10 / A1 |
| Alimentator potrivit modelului tău | Folosește-l pe cel oficial: alimentatoarele slabe produc blocaje ciudate |
| PC-ul tău Windows cu Garden Helper | În aceeași rețea de acasă (Wi-Fi sau cablu) |
| Senzori (opțional) | Sondă impermeabilă DS18B20 (temperatura apei), BME280 (temperatura și umiditatea aerului), senzor capacitiv de sol cu ADS1115 |
| Placă de relee (opțional) | 4 sau 5 canale pentru ventilație, ventilator, apă, pompă, lumină |

Poți face **pașii 1-6 fără niciun senzor**, folosind simulatorul. Fă asta întâi, ca să vezi că legătura funcționează.

---

## Pasul 1. Pune sistemul pe cardul SD

1. Pe PC instalează **Raspberry Pi Imager** de pe raspberrypi.com/software.
2. Alege modelul tău de Pi, apoi sistemul **Raspberry Pi OS Lite (64-bit)**. (Lite nu are desktop: e mai ușor și îți ajunge.)
3. Alege cardul SD, apoi apasă **Next** și **Edit settings**:
   - **Hostname:** `greenhouse` (ca să-l găsești ca `greenhouse.local`)
   - **Utilizator și parolă:** păstrează `pi` și alege o parolă pe care ți-o amintești
   - **Wi-Fi:** numele și parola rețelei tale, plus țara
   - **Services:** bifează **Enable SSH** cu autentificare prin parolă
4. Scrie cardul. Pune-l în Pi și conectează alimentarea. Prima pornire durează câteva minute.

## Pasul 2. Conectează-te la Pi de pe PC

1. Pe PC deschide **Windows Terminal** (sau PowerShell) și scrie:

   ```
   ssh pi@greenhouse.local
   ```

2. Răspunde `yes` la întrebarea despre amprentă, apoi scrie parola. Acum tastezi comenzi direct pe Pi. Linia de comandă arată ca `pi@greenhouse:~ $`.
3. Dacă `greenhouse.local` nu este găsit, caută adresa Pi-ului în lista de dispozitive din router (de exemplu `192.168.1.57`) și folosește `ssh pi@192.168.1.57`.
4. Actualizează Pi-ul o dată:

   ```
   sudo apt update && sudo apt full-upgrade -y
   ```

## Pasul 3. În Garden Helper: permite conectarea Pi-ului

1. Deschide **Seră** și creează o seră (sau alege una trasată pe hartă). Pentru un strat de afară vezi „Straturi de afară” mai jos.
2. Coboară la **Conexiunea Raspberry Pi**. Bifează **Acceptă conexiuni de la Raspberry Pi** și apasă **Aplică**.
3. Windows întreabă despre firewall. Permite Garden Helper **doar pe rețele private**.
4. Pagina arată acum **adresa PC-ului** (de exemplu `192.168.1.20`), **tokenul secret** și o **comandă** gata făcută. Ține pagina deschisă.

## Pasul 4. Copiază agentul pe Pi

Pe PC (într-o fereastră nouă de Terminal, nu cea conectată la Pi), din folderul Garden Helper:

```
scp tools\raspberry-pi\greenhouse_agent.py pi@greenhouse.local:~
```

(În aplicația instalată, scriptul se află în folderul `tools\raspberry-pi`, lângă aplicație.)

## Pasul 5. Testează conexiunea

Înapoi în Terminalul conectat la Pi, lipește comanda din aplicație și adaugă `--check`:

```
python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOUR_TOKEN --greenhouse 8 --check
```

Trebuie să vezi `Connected: {...}`. Dacă apare o eroare, mesajul îți spune ce să verifici: aplicația este deschisă și acceptă conexiuni, adresa și portul sunt corecte, Windows permite aplicația pe rețele private, ambele dispozitive sunt în aceeași rețea.

## Pasul 6. Încearcă cu senzori falși

```
python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOUR_TOKEN --greenhouse 8 --simulate
```

După câteva secunde, pagina **Seră** din aplicație arată casete cu temperatura, umiditatea și umiditatea solului care se mișcă. Oprește cu `Ctrl+C`. Legătura funcționează.

---

## Pasul 7. Conectează senzori adevărați

Oprește Pi-ul (`sudo shutdown now`) și scoate-l din priză înainte de conectări.

### Temperatura apei: DS18B20
- Firul roșu la **3,3 V** (pinul 1), cel negru la **GND** (pinul 6), cel galben (date) la **GPIO 4** (pinul 7), cu o **rezistență de 4,7 kΩ** între date și 3,3 V.
- Activează-l: `sudo nano /boot/firmware/config.txt`, adaugă la sfârșit linia `dtoverlay=w1-gpio`, salvează (`Ctrl+O`, `Enter`, `Ctrl+X`) și `sudo reboot`.
- Pornește agentul cu `--ds18b20`. Aplicația arată **Temperatura apei**.

### Temperatura și umiditatea aerului: BME280
- **VIN** la 3,3 V (pinul 1), **GND** la GND (pinul 9), **SDA** la GPIO 2 (pinul 3), **SCL** la GPIO 3 (pinul 5).
- Activează I2C: `sudo raspi-config`, **Interface Options**, **I2C**, Yes.
- Instalează biblioteca o singură dată: `sudo apt install -y python3-smbus2 python3-pip`, apoi `pip3 install --break-system-packages RPi.bme280`.
- Pornește agentul cu `--bme280`. Aplicația arată **Temperatura aerului** și **Umiditatea**.

### Orice altceva (umiditatea solului, pH, lumină)
Deschide `greenhouse_agent.py`, găsește `read_sensors` și adaugă citirea ta ca o cheie nouă: `soil_moisture` (%), `light` (lux), `ph`, `ec` (mS/cm), `water_level` (%). Aplicația înțelege aceste nume. Lasă la o parte senzorii pe care nu-i ai.

Poți combina: `--ds18b20 --bme280`.

## Pasul 8. Relee: ventilație, ventilator, apă, pompă, lumină

> **Siguranță.** Conectarea pompelor, a vanelor sau a încălzitoarelor alimentate de la rețea trebuie făcută de o persoană calificată, cu siguranță fuzibilă sau diferențial (RCD). Placa de relee doar comută; nu alimenta niciodată un încălzitor de la Pi. Păstrează un termostat hardware simplu ca ultimă apărare: software-ul poate da greș.

- Folosește o placă de relee făcută pentru Raspberry Pi, cu alimentare proprie pentru relee.
- Pinii impliciți (numerotare BCM): ventilație 17, ventilator 27, apă 22, pompă 23, lumină 24. Schimbă `PINS` la începutul scriptului dacă conectezi altfel. Majoritatea plăcilor pornesc când pinul trece pe **low**: asta înseamnă `ACTIVE_LOW = True`. Dacă placa ta e invers, pune `False`.
- **Starea sigură** când aplicația nu poate fi contactată 5 minute este: ventilația deschisă, ventilatorul pornit, apa oprită, pompa pornită, lumina oprită. Modifică `SAFE_STATE` după sistemul tău (pentru pești, lasă pompa pornită).
- În aplicație, fiecare comutator poate fi **Auto** (aplicația decide după limitele tale) sau **Manual** (îl comuți tu).

## Pasul 9. Pornire la boot

Ca să nu mai trebuiască să te conectezi niciodată:

1. Fă fișierul de serviciu: rulează comanda ta cu `--print-service` adăugat, de exemplu

   ```
   python3 greenhouse_agent.py --server http://192.168.1.20:8765 --token YOUR_TOKEN --greenhouse 8 --ds18b20 --bme280 --print-service | sudo tee /etc/systemd/system/greenhouse-agent.service
   ```

2. Pornește-l:

   ```
   sudo systemctl daemon-reload
   sudo systemctl enable --now greenhouse-agent
   ```

3. Îl poți urmări oricând cu `systemctl status greenhouse-agent` și `journalctl -u greenhouse-agent -f`.

Adresa PC-ului se poate schimba când routerul repornește. În router, dă PC-ului o adresă fixă („rezervată”), ca Pi-ul să-l găsească mereu.

## Acces din afara casei

**Nu** deschide portul în router: conexiunea este HTTP simplu, protejat doar de token. Folosește în schimb un VPN. Cel mai simplu este **Tailscale** (gratuit pentru uz personal): instalează-l pe PC și pe Pi (`curl -fsSL https://tailscale.com/install.sh | sh`, apoi `sudo tailscale up`), apoi folosește adresa Tailscale a PC-ului (`100.x.y.z`) ca `--server`.

## Straturi de afară și alte locuri

Agentul funcționează pentru **orice loc**, nu doar pentru sere. Pentru un strat sau pentru toată grădina de legume, pune senzorii pe un Pi (sau pe un ESP32 mic) și folosește **numărul locului** în loc de numărul serei. Apoi, în **Designerul de grădină**, selectează stratul și alege acel loc la **Sursa de senzori**: stratul primește un punct colorat (verde = umiditatea solului e bună, roșu = uscat, albastru = umed), iar fișa plantei arată valorile în direct. Locurile de afară doar stochează citiri; nu comandă relee.

## Dacă ceva nu merge

| Simptom | Ce poți încerca |
|---|---|
| `ssh` spune că gazda nu e găsită | Folosește adresa IP a Pi-ului din router în loc de `greenhouse.local` |
| `--check` nu ajunge la aplicație | Aplicația e deschisă? **Acceptă conexiuni** e bifat și Aplicat? Firewall-ul Windows permite rețeaua privată? Aceeași rețea Wi-Fi? |
| `--check` spune refuzat (401) | Tokenul a fost scris greșit. Copiază-l din nou din aplicație sau apasă **Fă un token nou** și actualizează Pi-ul |
| Casetele spun „fără date” | Agentul nu rulează sau s-a oprit: pornește-l din nou și urmărește ce scrie |
| Casetele spun că datele sunt vechi | Pi-ul a pierdut rețeaua. Verifică semnalul Wi-Fi lângă seră |
| BME280 not available | Verifică conexiunile și `sudo i2cdetect -y 1` (instalează `i2c-tools`); adresa este de obicei 76 sau 77 |
| DS18B20 nu arată nimic | Verifică rezistența de 4,7 kΩ și linia `dtoverlay=w1-gpio`, apoi repornește |
| Releele se comută invers | Schimbă `ACTIVE_LOW` |
