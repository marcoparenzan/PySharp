# mqtt_subscribe.py — subscribe (and publish) on an MQTT broker, run by PySharp.
#
# Scenario 5 of the roadmap. Uses paho-mqtt (downloaded from PyPI) against a public
# plaintext MQTT broker (port 1883). It does a real round-trip: subscribes to a unique
# topic, publishes 3 JSON messages to it, and receives them back from the broker.
#
# Prerequisite:  pysharp install paho-mqtt==2.1.0
# Usage:         pysharp run samples/mqtt_subscribe.py

import json
import time

import paho.mqtt.client as mqtt

BROKER = "test.mosquitto.org"
PORT = 1883
TOPIC = "pysharp/demo/" + str(int(time.time()))

state = {"connected": False}
received = []


def on_connect(client, userdata, flags, reason_code, properties):
    print("[mqtt] connected to %s (rc=%s)" % (BROKER, reason_code))
    state["connected"] = True
    client.subscribe(TOPIC, qos=1)
    print("[mqtt] subscribed to %s" % TOPIC)


def on_message(client, userdata, msg):
    payload = msg.payload.decode("utf-8")
    print("[recv] %s -> %s" % (msg.topic, payload))
    received.append(json.loads(payload))


def pump(client, seconds):
    deadline = time.time() + seconds
    while time.time() < deadline:
        client.loop(timeout=0.5)


def main():
    client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2)
    client.on_connect = on_connect
    client.on_message = on_message

    print("[main] connecting to %s:%d..." % (BROKER, PORT))
    client.connect(BROKER, PORT, keepalive=30)

    # wait for connection + subscribe
    deadline = time.time() + 8
    while time.time() < deadline and not state["connected"]:
        client.loop(timeout=0.5)
    if not state["connected"]:
        print("[main] connection failed")
        return 1

    # publish on the topic we are subscribed to
    for i in range(3):
        payload = json.dumps({"seq": i, "msg": "hello from PySharp"})
        client.publish(TOPIC, payload, qos=1)
        print("[send] %s" % payload)
        client.loop(timeout=0.3)

    # keep listening until all of them arrive (or timeout)
    deadline = time.time() + 8
    while time.time() < deadline and len(received) < 3:
        client.loop(timeout=0.5)

    client.disconnect()
    client.loop(timeout=0.5)
    print("[main] received %d/3 messages" % len(received))
    return 0 if len(received) == 3 else 2


main()
