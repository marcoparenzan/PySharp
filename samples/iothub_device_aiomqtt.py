# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# iothub_device_aiomqtt.py — Azure IoT Hub device on aiomqtt (async/await), run by PySharp.
#
# Async counterpart of samples/iothub_device_mqtt.py: same Azure IoT Hub MQTT protocol
# (SAS token auth, D2C telemetry, C2D messages, device twin), but written against
# aiomqtt's async context-manager client instead of paho-mqtt callbacks + a manual
# network loop. Rationale for the two styles: paho-mqtt for scripts/workers/anything
# blocking, aiomqtt when the rest of the app is already async — see
# https://scadaprotocols.com/python-mqtt/
#
#   async with aiomqtt.Client(...) as client:
#       await client.subscribe("...")
#       async for message in client.messages:
#           ...
#
# STATUS — the core flow (connect, subscribe, concurrent publish + `async for message in
# client.messages`, clean disconnect) runs end-to-end against a real broker
# (test.mosquitto.org). Not yet verified against a real Azure IoT Hub specifically (no
# credentials available in this environment) — see AIOMQTT_PLAN.md Phase 6 at the repo root.
#
# Prerequisite:  pysharp install aiomqtt
# Config in config.json (see config.iothub_device_mqtt.json — shared with the sync sample).
# Usage:         pysharp run iothub_device_aiomqtt.py [config.json]

import asyncio
import base64
import hashlib
import hmac
import json
import ssl
import sys
import time
import urllib.parse

import aiomqtt

API_VERSION = "2021-04-12"


# ----------------------------------------------------------------- helpers
# (identical to samples/iothub_device_mqtt.py — same IoT Hub SAS/connection-string rules)

def parse_connection_string(cs):
    """HostName=...;DeviceId=...;SharedAccessKey=... -> dict"""
    parts = {}
    for section in cs.split(";"):
        if not section:
            continue
        key, _, value = section.partition("=")
        parts[key] = value
    return parts


def generate_sas_token(resource_uri, key, expiry):
    """SAS token for IoT Hub: HMAC-SHA256 signature of '<uri>\n<expiry>' with the base64 key."""
    encoded_uri = urllib.parse.quote(resource_uri, safe="")
    to_sign = encoded_uri + "\n" + str(expiry)
    signature = hmac.new(
        base64.b64decode(key),
        to_sign.encode("utf-8"),
        hashlib.sha256,
    ).digest()
    sig = urllib.parse.quote(base64.b64encode(signature).decode("utf-8"), safe="")
    return "SharedAccessSignature sr=%s&sig=%s&se=%d" % (encoded_uri, sig, expiry)


def load_config():
    path = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1].endswith(".json") else "config.json"
    with open(path) as f:
        return json.load(f)


# ----------------------------------------------------------------- device

async def run_device(config):
    auth = config.get("auth", "sas")
    if auth == "sas":
        cs = parse_connection_string(config["connection_string"])
        hostname = cs["HostName"]
        device_id = cs["DeviceId"]
        shared_key = cs["SharedAccessKey"]
    else:
        hostname = config["hostname"]
        device_id = config["device_id"]
        shared_key = None

    username = "%s/%s/?api-version=%s" % (hostname, device_id, API_VERSION)

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    password = None
    if auth == "x509":
        # client-certificate authentication (self-signed, registered on the device)
        context.load_cert_chain(config["x509_cert"], config["x509_key"])
    else:
        # SAS authentication: token with a 1h expiry as the MQTT password
        expiry = int(time.time()) + 3600
        resource_uri = hostname + "/devices/" + device_id
        password = generate_sas_token(resource_uri, shared_key, expiry)

    twin = {"doc": None}
    rid = {"n": 0}

    def next_rid():
        rid["n"] += 1
        return rid["n"]

    print("[main] connecting to %s as '%s'..." % (hostname, device_id))
    async with aiomqtt.Client(
        hostname,
        port=8883,
        identifier=device_id,
        username=username,
        password=password,
        tls_context=context,
        protocol=aiomqtt.ProtocolVersion.V311,
        keepalive=60,
    ) as client:
        print("[mqtt] connected to %s" % hostname)

        # C2D
        await client.subscribe("devices/%s/messages/devicebound/#" % device_id)
        # twin responses (GET and reported PATCH)
        await client.subscribe("$iothub/twin/res/#")
        # desired properties: push patch from the cloud
        await client.subscribe("$iothub/twin/PATCH/properties/desired/#")

        async def listen():
            async for message in client.messages:
                topic = str(message.topic)
                payload = message.payload.decode("utf-8") if message.payload else ""

                if topic.startswith("devices/"):
                    print("[c2d] message: %s" % payload)
                elif topic.startswith("$iothub/twin/res/"):
                    # $iothub/twin/res/{status}/?$rid={rid}
                    status = topic.split("/")[3]
                    if status == "200" and payload:
                        twin["doc"] = json.loads(payload)
                        print("[twin] document received: %s" % json.dumps(twin["doc"]))
                    else:
                        print("[twin] response status=%s" % status)
                elif topic.startswith("$iothub/twin/PATCH/properties/desired/"):
                    patch = json.loads(payload)
                    print("[twin] desired properties updated: %s" % json.dumps(patch))
                    applied = {k: v for k, v in patch.items() if not k.startswith("$")}
                    if applied:
                        await client.publish(
                            "$iothub/twin/PATCH/properties/reported/?$rid=%d" % next_rid(),
                            json.dumps({"applied": applied, "ts": int(time.time())}),
                        )
                else:
                    print("[mqtt] message on %s: %s" % (topic, payload))

        listener = asyncio.create_task(listen())

        # 1. device twin: GET the document
        await client.publish("$iothub/twin/GET/?$rid=%d" % next_rid(), "")
        print("[twin] GET requested")
        await asyncio.sleep(3)

        # 2. reported properties
        await client.publish(
            "$iothub/twin/PATCH/properties/reported/?$rid=%d" % next_rid(),
            json.dumps({
                "firmware": "pysharp-1.0",
                "interpreter": "PySharp",
                "boot_ts": int(time.time()),
            }),
        )
        await asyncio.sleep(3)

        # 3. D2C telemetry
        for i in range(3):
            payload = json.dumps({"seq": i, "temperature": 20 + i, "humidity": 60 - i})
            await client.publish("devices/%s/messages/events/" % device_id, payload, qos=1)
            print("[d2c] sent: %s" % payload)
            await asyncio.sleep(2)

        # 4. keep listening for C2D and desired properties
        print("[main] listening for 30s (send a C2D or change the desired properties)...")
        await asyncio.sleep(30)

        listener.cancel()

    print("[main] done")


def main():
    config = load_config()
    asyncio.run(run_device(config))
    return 0


if __name__ == "__main__":
    sys.exit(main())
