# config_yaml.py — YAML + JSON (de)serialization, run by PySharp.
#
# Scenario 9 of the roadmap. Loads a YAML configuration, inspects it, and converts
# it back and forth between YAML and JSON. The `yaml` module is a native C# module
# (a subset of PyYAML); `json` was already present.
#
# Usage:  pysharp run samples/config_yaml.py

import json
import yaml

CONFIG = """
# example configuration
app:
  name: PySharp
  debug: false
  version: 2
server:
  host: 0.0.0.0
  port: 8080
  workers: 4
endpoints:
  - path: /health
    method: GET
  - path: /items
    method: POST
tags: [python, csharp, yaml]
"""

config = yaml.safe_load(CONFIG)

print("app.name    :", config["app"]["name"])
print("app.debug   :", config["app"]["debug"], "(%s)" % type(config["app"]["debug"]).__name__)
print("server.port :", config["server"]["port"], "(%s)" % type(config["server"]["port"]).__name__)
print("endpoints   :", len(config["endpoints"]), "->", config["endpoints"][0]["path"])
print("tags        :", config["tags"])

# YAML -> JSON
print("\n--- as JSON ---")
as_json = json.dumps(config, indent=2)
print(as_json)

# JSON -> YAML
print("--- back as YAML ---")
reloaded = json.loads(as_json)
print(yaml.safe_dump(reloaded))

assert reloaded == config, "the YAML<->JSON round-trip must preserve the data"
print("YAML <-> JSON round-trip: ok")
