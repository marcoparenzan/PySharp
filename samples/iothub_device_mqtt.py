# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# iothub_device_mqtt.py — Azure IoT Hub device on paho-mqtt, run by PySharp.
#
# Demonstrates, directly on the IoT Hub MQTT protocol (port 8883, MQTT 3.1.1):
#   - SAS token authentication (connection string) or X.509 certificate
#   - device-to-cloud telemetry (D2C)
#   - cloud-to-device messages (C2D)
#   - device twin: GET, reported properties, desired properties (live patch)
#
# Config in config.json (see config.iothub_device_mqtt.json).
# Usage:  pysharp run iothub_device_mqtt.py [config.json]

import base64
import hashlib
import hmac
import json
import ssl
import sys
import time
import urllib.parse

import paho.mqtt.client as mqtt

API_VERSION = "2021-04-12"


# ----------------------------------------------------------------- helpers

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

class IoTHubDevice:
    """MQTT device for Azure IoT Hub with send/receive/twin."""

    def __init__(self, config):
        self._rid = 0
        self.twin = None
        self.connected = False

        auth = config.get("auth", "sas")
        if auth == "sas":
            cs = parse_connection_string(config["connection_string"])
            self.hostname = cs["HostName"]
            self.device_id = cs["DeviceId"]
            self._shared_key = cs["SharedAccessKey"]
        else:
            self.hostname = config["hostname"]
            self.device_id = config["device_id"]
            self._shared_key = None

        self.client = mqtt.Client(
            mqtt.CallbackAPIVersion.VERSION2,
            client_id=self.device_id,
            protocol=mqtt.MQTTv311,
        )

        username = "%s/%s/?api-version=%s" % (self.hostname, self.device_id, API_VERSION)

        context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        if auth == "x509":
            # client-certificate authentication (self-signed, registered on the device)
            context.load_cert_chain(config["x509_cert"], config["x509_key"])
            self.client.username_pw_set(username)
        else:
            # SAS authentication: token with a 1h expiry as the MQTT password
            expiry = int(time.time()) + 3600
            resource_uri = self.hostname + "/devices/" + self.device_id
            token = generate_sas_token(resource_uri, self._shared_key, expiry)
            self.client.username_pw_set(username, token)

        self.client.tls_set_context(context)

        self.client.on_connect = self._on_connect
        self.client.on_message = self._on_message
        self.client.on_disconnect = self._on_disconnect

    # ------------------------------------------------------------- callbacks

    def _on_connect(self, client, userdata, flags, reason_code, properties):
        print("[mqtt] connected to %s (rc=%s)" % (self.hostname, reason_code))
        self.connected = True
        # C2D
        client.subscribe("devices/%s/messages/devicebound/#" % self.device_id)
        # twin responses (GET and reported PATCH)
        client.subscribe("$iothub/twin/res/#")
        # desired properties: push patch from the cloud
        client.subscribe("$iothub/twin/PATCH/properties/desired/#")

    def _on_disconnect(self, client, userdata, flags, reason_code, properties):
        print("[mqtt] disconnected (rc=%s)" % reason_code)
        self.connected = False

    def _on_message(self, client, userdata, msg):
        topic = msg.topic
        payload = msg.payload.decode("utf-8") if len(msg.payload) > 0 else ""

        if topic.startswith("devices/"):
            print("[c2d] message: %s" % payload)
        elif topic.startswith("$iothub/twin/res/"):
            # $iothub/twin/res/{status}/?$rid={rid}
            status = topic.split("/")[3]
            if status == "200" and payload:
                self.twin = json.loads(payload)
                print("[twin] document received: %s" % json.dumps(self.twin))
            else:
                print("[twin] response status=%s" % status)
        elif topic.startswith("$iothub/twin/PATCH/properties/desired/"):
            patch = json.loads(payload)
            print("[twin] desired properties updated: %s" % json.dumps(patch))
            self.on_desired(patch)
        else:
            print("[mqtt] message on %s: %s" % (topic, payload))

    def on_desired(self, patch):
        """Reacts to a desired-properties patch by reporting the applied state."""
        applied = {}
        for key in patch:
            if key.startswith("$"):
                continue
            applied[key] = patch[key]
        if applied:
            self.report_properties({"applied": applied, "ts": int(time.time())})

    # ------------------------------------------------------------- operations

    def connect(self):
        self.client.connect(self.hostname, 8883, keepalive=60)

    def send_telemetry(self, data):
        topic = "devices/%s/messages/events/" % self.device_id
        payload = json.dumps(data)
        info = self.client.publish(topic, payload, qos=1)
        print("[d2c] sent: %s (mid=%d)" % (payload, info.mid))

    def request_twin(self):
        self._rid += 1
        self.client.publish("$iothub/twin/GET/?$rid=%d" % self._rid, "")
        print("[twin] GET requested (rid=%d)" % self._rid)

    def report_properties(self, properties):
        self._rid += 1
        topic = "$iothub/twin/PATCH/properties/reported/?$rid=%d" % self._rid
        self.client.publish(topic, json.dumps(properties))
        print("[twin] reported sent: %s" % json.dumps(properties))

    def loop(self, seconds):
        deadline = time.time() + seconds
        while time.time() < deadline:
            self.client.loop(timeout=1.0)


# ----------------------------------------------------------------- main

def main():
    config = load_config()
    device = IoTHubDevice(config)

    print("[main] connecting to %s as '%s'..." % (device.hostname, device.device_id))
    device.connect()
    device.loop(3)

    if not device.connected:
        print("[main] connection failed")
        return 1

    # 1. device twin: GET the document
    device.request_twin()
    device.loop(3)

    # 2. reported properties
    device.report_properties({
        "firmware": "pysharp-1.0",
        "interpreter": "PySharp",
        "boot_ts": int(time.time()),
    })
    device.loop(3)

    # 3. D2C telemetry
    for i in range(3):
        device.send_telemetry({
            "seq": i,
            "temperature": 20 + i,
            "humidity": 60 - i,
        })
        device.loop(2)

    # 4. keep listening for C2D and desired properties
    print("[main] listening for 30s (send a C2D or change the desired properties)...")
    device.loop(30)

    device.client.disconnect()
    device.loop(1)
    print("[main] done")
    return 0


if __name__ == "__main__":
    sys.exit(main())
