"""
Turn-action payload generator. Port of PlayerBehavior.cs.

Server canonical zone names are PascalCase (BattleZone.cs:7-14). Field names
in the JSON payload are camelCase: attackZone, blockZonePrimary,
blockZoneSecondary (matches the C# anonymous-object serializer output).
"""

from __future__ import annotations

import json
import random as _random

ZONES = ("Head", "Chest", "Belly", "Waist", "Legs")

# Adjacent block pairs on the 5-zone ring.
# See src/Kombats.Battle/Kombats.Battle.Domain/Rules/BattleZone.cs:24-32.
BLOCK_PAIRS = (
    ("Head", "Chest"),
    ("Chest", "Belly"),
    ("Belly", "Waist"),
    ("Waist", "Legs"),
    ("Legs", "Head"),
)


def pick_action_payload(rng: _random.Random) -> str:
    """Uniform-random pick: 5 zones × 5 adjacent block pairs = 25 options.

    Returns a JSON-encoded string (server expects the action payload as a
    JSON string, not an object — see SubmitTurnAction signature)."""
    attack_zone = rng.choice(ZONES)
    primary, secondary = rng.choice(BLOCK_PAIRS)
    return json.dumps({
        "attackZone": attack_zone,
        "blockZonePrimary": primary,
        "blockZoneSecondary": secondary,
    })
