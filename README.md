# mqtt-presence-bench

How long does it take before an AI agent knows an MQTT device is dead?

This repo starts with a small MQ02 demo: one sensor leaves a Last Will and one agent reads presence before values. It grows, one episode a week and built in public on the [Dev Skills Unlock](https://www.youtube.com/@DevSkillsUnlock) channel, into a .NET tool that stress-tests MQTT presence. That means hundreds of clients, brutal disconnects, and time-to-offline for every Last Will.

| Tag | Episode | What it adds |
|---|---|---|
| `mq02` | MQTT Retained Messages and Last Will Explained | `src/LastWillDemo`: one sensor, one agent, a cable you can cut |

## Run the MQ02 demo

Requires the .NET 10 SDK and Docker or Podman.

```powershell
docker compose up -d            # or: podman run -d --name mq02-broker -p 1883:1883 -v ${PWD}/mosquitto:/mosquitto/config eclipse-mosquitto:2

cd src/LastWillDemo
dotnet run -- agent             # terminal 1: the AI agent, subscribed to line3/#
dotnet run -- sensor            # terminal 2: K = kill the process, C = cut the cable, Q = clean goodbye
```

The sensor connects to the broker through `Cable`, a small TCP relay on `127.0.0.1:1884`. `Cut()` stops forwarding bytes but keeps both sockets open. To the broker it looks like a power cut: no FIN, no RST, just silence.

### Scenarios

One command per scenario, agent and sensor in the same console (this is what the video shows):

```powershell
./demo.ps1 trap    # or: kill | quit | cut
```

Running by hand? Clear retained state between runs with `dotnet run -- reset` (it publishes an empty retained payload to both topics).

| Scenario | Command | What the agent sees (Mosquitto 2.1.2) |
|---|---|---|
| Process killed | `sensor --then kill --after 6` | `offline` right away: the OS closed the socket |
| Power cut | `sensor --then cut --after 6 --for 40` | `offline` about 15 s after the last reading: 1.5 × the 10 s keep-alive, counted from the last packet |
| Clean goodbye | `sensor --then quit --after 5` | the sensor's own `offline`; the broker discards the Will |
| The trap | `sensor --no-will --then kill --after 5`, then start the agent | retained `online` + `71.0`, and the agent says "All good" about a dead sensor |
| Will Delay | `sensor --will-delay 30 --session-expiry 60 --then kill --after 5` | `offline` about 30 s after the kill |

> **Will Delay and Session Expiry.** MQTT 5 (§3.1.2.5, MQTT-3.1.2-8) publishes the Will when the Will Delay ends *or the session ends, whichever comes first*. With Session Expiry 0 the session ends at disconnect, so the spec says the Will goes out at once. Mosquitto 2.1.2 still waited the full Will Delay in our test (`--will-delay 30`, with or without an explicit Session Expiry 0). Set Session Expiry ≥ Will Delay, and measure on your own broker.

Anonymous access on port 1883 is for the local demo only.

## Sources

- [MQTT Version 5.0, OASIS Standard](https://docs.oasis-open.org/mqtt/mqtt/v5.0/mqtt-v5.0.html): §3.1.2.5 Will Flag, §3.1.2.10 Keep Alive, §3.1.3.2.2 Will Delay Interval, §3.3.1.3 RETAIN
- [MQTTnet](https://github.com/dotnet/MQTTnet) 5.2
