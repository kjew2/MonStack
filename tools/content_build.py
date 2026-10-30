#!/usr/bin/env python3
"""Content converter: content_src/<table>.csv -> Content/<table>.json + manifest.json (specs/05).

Prototype scope: type checks, key uniqueness, enum values and the handful of cross-table rules the sim
depends on. Version-bump rules (specs/05 section 6.3) and the full error-code catalogue are not built yet.

    python3 tools/content_build.py [--src content_src] [--out Content] [--check] [--copy-to DIR]

Exit codes: 0 ok, 1 validation errors (all listed), 2 usage / IO error. Stdlib only.
"""
import argparse
import csv
import hashlib
import json
import os
import re
import shutil
import sys

SCHEMA_VERSION = 1

ENUMS = {
    "Tier": ["C", "R", "E", "L", "M"],
    "BannerKind": ["hero", "pet"],
    "RngStream": ["combat", "gear_drop", "gear_rarity", "enhance", "gacha_hero", "gacha_pet"],
    "CurveId": ["enemy_hp", "gold_per_kill", "stat_cost"],
    "QuestType": ["login", "kill_mobs", "clear_boss", "do_pulls"],
    "GrantTrigger": ["profile_created", "stage_reached", "first_boss_cleared"],
    "GrantType": ["gems", "free_pulls", "gold", "hero"],
    "ConstantType": ["int", "double", "bool"],
    "TreeKind": ["native", "generic", "chase", "book"],
    "NodeKind": ["growth", "stat", "skill", "trigger", "rule"],
    "FightTrigger": ["battle_start", "on_action", "on_basic_attack", "on_crit", "on_hit_taken", "on_evade"],
    "ConditionType": ["tree_maxed", "node_owned", "stat_at_least", "tally_at_least"],
    "TalentEffect": ["drill_cost", "stat_floor", "draft_size", "start_sp"],
    "SkillKind": ["damage", "heal", "shield", "taunt", "buff", "debuff", "passive"],
    "SkillTarget": ["enemy", "all_enemies", "self", "lowest_ally", "all_allies"],
    "SkillStat": ["hp", "atk", "def", "spd", "crit_rate", "crit_dmg"],
}

# key -> type. Mirrors IdleRng.Sim.Content.Constants. specs/05 section 5.1 plus the specs/00 names it
# did not yet carry (STAGE_CAP, OFFLINE_MIN_GAP_S, DAY_SECONDS, GEAR_SELL_KILLS, IDLE_CHEST_*).
REQUIRED_CONSTANTS = {
    "TICK_HZ": "int", "SPD_REF": "double", "ATTACK_INTERVAL_TICKS": "int",
    "KILLS_PER_STAGE": "int", "SPAWN_DELAY_TICKS": "int", "BOSS_EVERY": "int", "STAGE_CAP": "int",
    "STAT_PER_LVL": "double", "GEMS_PER_DAY_F2P": "int", "OFFLINE_RATE": "double",
    "OFFLINE_CAP_H": "double", "OFFLINE_MIN_GAP_S": "int", "DAY_SECONDS": "int",
    "GEAR_DROP_P": "double", "TIER_WIDTH": "int", "GEAR_SLOT_G": "double", "GEAR_SELL_KILLS": "double",
    "ENH_MAX": "int", "ENH_PER_LVL": "double", "ENH_SUCCESS_FLOOR": "double",
    "ENH_SUCCESS_STEP": "double", "ENH_COST_GOLD_MULT": "double",
    "HERO_SLOTS": "int", "HERO_LVL_BONUS": "double",
    "IDLE_CHEST_HOURS": "double", "IDLE_CHEST_GEMS": "int",
    "BATTLE_GAUGE": "double", "DEF_K_MULT": "double", "ELEMENT_STRONG_MULT": "double", "ELEMENT_WEAK_MULT": "double",
    "BASIC_ATTACK_POWER": "double", "AI_HEAL_BELOW": "double", "AI_SHIELD_BELOW": "double",
    # monster battle (specs/16)
    "MON_HP_PER_POINT": "double", "MON_DEF_K": "double", "MON_EVADE_BASE": "double", "MON_EVADE_PER_DEX": "double",
    "MON_EVADE_CAP": "double", "MON_CRIT_BASE": "double", "MON_CRIT_PER_LUK": "double", "MON_CRIT_CAP": "double",
    "MON_CRIT_DMG": "double", "MON_SPD_MIN": "double", "MON_TIME_LIMIT_TICKS": "int", "MON_SIDE_SIZE": "int",
    # training run (specs/14)
    "RUN_CYCLES": "int", "RUN_AP": "int", "RUN_ENERGY_MAX": "double", "RUN_ENERGY_START": "double",
    "DRILL_ENERGY_COST": "double", "REST_ENERGY": "double", "DRILL_FAIL_MIN": "double", "DRILL_FAIL_MAX": "double",
    "DRILL_SUPER_P": "double", "INJURY_BELOW": "double", "INJURY_MAX_P": "double", "STAT_MAX": "double", "FIGHT_EVERY": "int",
    # skill trees (specs/15)
    "SP_PER_CYCLE": "int", "SP_PER_WIN": "int", "TREE_SLOTS": "int", "DRAFT_SIZE": "int", "MON_ECHO_POWER": "double",
    # account and meta-progression (specs/18)
    "SHARDS_PER_CYCLE": "int", "SHARDS_PER_RANK": "int",
}

STATS = ["hp", "str", "int", "wis", "dex", "def", "spd", "luk"]
DRILL_TIERS = ["fail", "success", "super"]
EFFECT_STATS = STATS + ["crit_rate", "crit_dmg"]
# Coded rule modifiers (IdleRng.Sim.Combat.RuleModifiers.Known, specs/16 section 6.2).
RULE_IDS = ["counter_mitigated", "echo_cast"]
TALLIES = ["damage_dealt", "evades", "crits", "kills", "wins"]

# Range rules for the monster battle constants (specs/16 section 7): key -> (rule text, predicate).
MON_RANGES = {
    "MON_HP_PER_POINT": ("> 0", lambda v: v > 0), "MON_DEF_K": ("> 0", lambda v: v > 0),
    "MON_EVADE_BASE": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_EVADE_PER_DEX": (">= 0", lambda v: v >= 0),
    "MON_EVADE_CAP": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_CRIT_BASE": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_CRIT_PER_LUK": (">= 0", lambda v: v >= 0), "MON_CRIT_CAP": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_CRIT_DMG": (">= 1", lambda v: v >= 1), "MON_SPD_MIN": ("> 0", lambda v: v > 0),
    "MON_TIME_LIMIT_TICKS": ("> 0", lambda v: v > 0), "MON_SIDE_SIZE": ("> 0", lambda v: v > 0),
    "RUN_CYCLES": ("> 0", lambda v: v > 0), "RUN_AP": ("> 0", lambda v: v > 0), "RUN_ENERGY_MAX": ("> 0", lambda v: v > 0),
    "RUN_ENERGY_START": (">= 0", lambda v: v >= 0), "DRILL_ENERGY_COST": (">= 0", lambda v: v >= 0),
    "REST_ENERGY": (">= 0", lambda v: v >= 0), "DRILL_FAIL_MIN": ("[0, 1]", lambda v: 0 <= v <= 1),
    "DRILL_FAIL_MAX": ("[0, 1]", lambda v: 0 <= v <= 1), "DRILL_SUPER_P": ("[0, 1]", lambda v: 0 <= v <= 1),
    "INJURY_BELOW": ("[0, 1]", lambda v: 0 <= v <= 1), "INJURY_MAX_P": ("[0, 1]", lambda v: 0 <= v <= 1),
    "STAT_MAX": ("> 0", lambda v: v > 0), "FIGHT_EVERY": ("> 0", lambda v: v > 0),
    "SP_PER_CYCLE": (">= 0", lambda v: v >= 0), "SP_PER_WIN": (">= 0", lambda v: v >= 0),
    "TREE_SLOTS": (">= 2", lambda v: v >= 2), "DRAFT_SIZE": ("> 0", lambda v: v > 0),
    "MON_ECHO_POWER": ("[0, 1]", lambda v: 0 <= v <= 1),
    "SHARDS_PER_CYCLE": (">= 0", lambda v: v >= 0), "SHARDS_PER_RANK": (">= 0", lambda v: v >= 0),
}

TIERS = ENUMS["Tier"]


def T(columns, key=None, folds=None, required=True):
    """columns: list of (name, type, nullable)."""
    return {"columns": columns, "key": key, "folds": folds or {}, "required": required}


def c(name, typ, nullable=False):
    return (name, typ, nullable)


SCHEMA = {
    "banners": T([c("id", "id"), c("kind", "enum:BannerKind"), c("name_key", "key"), c("pool_id", "id"),
                  c("rate_c", "double"), c("rate_r", "double"), c("rate_e", "double"),
                  c("rate_l", "double"), c("rate_m", "double"),
                  c("pity_soft_start", "int"), c("pity_soft_step", "double"), c("pity_hard", "int"),
                  c("mythic_share_of_lplus", "double"), c("guarantee_batch_size", "int"),
                  c("guarantee_min_tier", "enum:Tier"), c("pull_cost_gems", "int"),
                  c("free_pulls_per_day", "int"), c("rng_stream", "enum:RngStream"), c("enabled", "bool")],
                 key="id", folds={"rates": ["rate_c", "rate_r", "rate_e", "rate_l", "rate_m"]}),
    "boss_rules": T([c("id", "id"), c("from_stage", "int"), c("hp_mult", "double"), c("time_ticks", "int"),
                     c("gold_mult", "double"), c("gear_drop_p", "double"), c("gear_min_rarity", "enum:Tier"),
                     c("first_clear_gems", "int"), c("atk_mult", "double"), c("def_mult", "double"), c("spd", "double"),
                     c("aoe_every", "int"), c("aoe_power", "double")], key="id"),
    "constants": T([c("key", "string"), c("type", "enum:ConstantType"), c("value", "constval")], key="key"),
    "curves": T([c("id", "enum:CurveId"), c("base", "double"), c("growth", "double")], key="id"),
    "daily_quests": T([c("id", "id"), c("type", "enum:QuestType"), c("target", "int"), c("reward_gems", "int"),
                       c("sort_order", "int"), c("name_key", "key")], key="id"),
    "gear_rarity": T([c("rarity", "enum:Tier"), c("weight", "double"), c("mult", "double"),
                      c("display_color", "color"), c("name_key", "key")], key="rarity"),
    "gear_slots": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"), c("icon_id", "string")], key="id"),
    "hero_level_thresholds": T([c("level", "int"), c("cum_copies", "int")], key="level"),
    "elements": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"), c("display_color", "color"),
                   c("strong_vs", "id")], key="id"),
    "hero_tiers": T([c("tier", "enum:Tier"), c("display_color", "color"), c("name_key", "key"),
                     c("shard_value", "int"), c("max_promotions", "int")], key="tier"),
    "heroes": T([c("id", "id"), c("tier", "enum:Tier"), c("pool_id", "id"), c("name_key", "key"),
                 c("desc_key", "key"), c("icon_id", "string"), c("role", "id"), c("element", "id"),
                 c("hp", "double"), c("atk", "double"), c("def", "double"), c("spd", "double"),
                 c("crit_rate", "double"), c("crit_dmg", "double"), c("starter", "bool")], key="id"),
    "progress_targets": T([c("day", "int"), c("stage", "int")], key="day"),
    "promotions": T([c("index", "int"), c("unlock_stage", "int"), c("mult", "double"), c("cost_kills", "double"),
                     c("name_key", "key")], key="index"),
    "species": T([c("id", "id"), c("name_key", "key"), c("element", "id"), c("native_tree_1", "id"), c("native_tree_2", "id")]
                  + [c("base_" + s, "double") for s in STATS] + [c("grow_" + s, "double") for s in STATS], key="id"),
    "drill_tiers": T([c("id", "id"), c("gain_min", "int"), c("gain_max", "int"), c("name_key", "key")], key="id"),
    "fight_ranks": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"), c("element", "id")]
                     + [c(s, "double") for s in STATS], key="id"),
    "stat_milestones": T([c("threshold", "int")], key="threshold"),
    "monster_skills": T([c("id", "id"), c("name_key", "key"), c("desc_key", "key"), c("kind", "enum:SkillKind"),
                         c("target", "enum:SkillTarget"), c("stat", "string", True), c("scales", "string", True),
                         c("power", "double"), c("cooldown", "int"), c("duration", "int"), c("element", "id", True)], key="id"),
    "trees": T([c("id", "id"), c("name_key", "key"), c("kind", "enum:TreeKind"), c("offer_stat", "string", True),
                c("offer_threshold", "int", True)], key="id"),
    "tree_nodes": T([c("id", "id"), c("tree_id", "id"), c("index", "int"), c("cost", "int"), c("kind", "enum:NodeKind"),
                     c("stat", "string", True), c("amount", "double", True), c("skill_id", "id", True),
                     c("trigger", "enum:FightTrigger", True), c("rule_id", "id", True)], key="id"),
    "chase_conditions": T([c("id", "id"), c("tree_id", "id"), c("type", "enum:ConditionType"), c("ref", "string"),
                           c("amount", "double", True)], key="id"),
    "skill_books": T([c("id", "id"), c("name_key", "key"), c("tree_id", "id")], key="id"),
    "talents": T([c("id", "id"), c("name_key", "key"), c("desc_key", "key"), c("effect", "enum:TalentEffect"),
                  c("per_rank", "double"), c("max_rank", "int"), c("cost_base", "int"), c("cost_step", "int")], key="id"),
    "skills": T([c("id", "id"), c("hero_id", "id"), c("slot", "int"), c("unlock_promotion", "int"), c("name_key", "key"),
                 c("desc_key", "key"), c("kind", "enum:SkillKind"), c("target", "enum:SkillTarget"),
                 c("stat", "enum:SkillStat", True), c("scales", "enum:SkillStat", True), c("power", "double"),
                 c("cooldown", "int"), c("duration", "int")], key="id"),
    "roles": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"), c("desc_key", "key")], key="id"),
    "strings": T([c("key", "key"), c("en", "string")], key="key", required=False),
    "tutorial_grants": T([c("id", "id"), c("trigger", "enum:GrantTrigger"), c("trigger_value", "int", True),
                          c("grant_type", "enum:GrantType"), c("amount", "int"), c("grant_ref", "string", True),
                          c("sort_order", "int")], key="id"),
}

ID_RE = re.compile(r"^[a-z][a-z0-9_]{0,63}$")
KEY_RE = re.compile(r"^[a-z0-9_]+(\.[a-z0-9_]+)+$")
COLOR_RE = re.compile(r"^#[0-9A-F]{6}$")
INT_RE = re.compile(r"^-?\d+$")


class Errors(list):
    def add(self, code, loc, msg):
        self.append(f"CONTENT {code} {loc}: {msg}")


def parse_cell(typ, text, loc, errs):
    if typ == "int":
        if not INT_RE.match(text):
            errs.add("E010", loc, f"expected int, got '{text}'")
            return None
        return int(text)
    if typ == "double":
        try:
            return float(text)
        except ValueError:
            errs.add("E010", loc, f"expected double, got '{text}'")
            return None
    if typ == "bool":
        if text not in ("true", "false"):
            errs.add("E010", loc, f"expected true/false, got '{text}'")
            return None
        return text == "true"
    if typ.startswith("enum:"):
        values = ENUMS[typ[5:]]
        if text not in values:
            errs.add("E021", loc, f"'{text}' is not one of {', '.join(values)}")
            return None
        return text
    if typ == "id" and not ID_RE.match(text):
        errs.add("E010", loc, f"bad id '{text}'")
    if typ == "key" and not KEY_RE.match(text):
        errs.add("E010", loc, f"bad localisation key '{text}'")
    if typ == "color" and not COLOR_RE.match(text):
        errs.add("E010", loc, f"bad color '{text}' (want #RRGGBB uppercase)")
    return text


def read_table(path, name, schema, errs):
    with open(path, newline="", encoding="utf-8") as f:
        rows = list(csv.reader(f))
    if not rows:
        errs.add("E001", name, "empty file")
        return []
    header = [h.strip() for h in rows[0]]
    cols = {cn: (ct, nul) for cn, ct, nul in schema["columns"]}
    for h in header:
        if h and not h.startswith("#") and h not in cols:
            errs.add("E002", name, f"unknown column '{h}'")
    for cn in cols:
        if cn not in header:
            errs.add("E003", name, f"missing column '{cn}'")
    out, seen = [], set()
    for i, raw in enumerate(rows[1:], start=2):
        cells = [x.strip() for x in raw]
        if not any(cells):
            continue
        rec = dict(zip(header, cells + [""] * (len(header) - len(cells))))
        row = {}
        for cn, ct, nul in schema["columns"]:
            text = rec.get(cn, "")
            loc = f"{name}[row {i}].{cn}"
            if text == "":
                if not nul:
                    errs.add("E011", loc, "empty cell in non-nullable column")
                row[cn] = None
                continue
            if ct == "constval":
                row[cn] = text  # typed after the whole row is read
            else:
                row[cn] = parse_cell(ct, text, loc, errs)
        if name == "constants" and row.get("type") and row.get("value") is not None:
            row["value"] = parse_cell(row["type"], row["value"], f"constants[{row['key']}].value", errs)
        k = row.get(schema["key"])
        if k in seen:
            errs.add("E020", f"{name}[{k}]", "duplicate key")
        seen.add(k)
        out.append(row)
    return out


def validate_trees(t, errs):
    """Skill trees (specs/15 section 9). Mirrors ContentDb.BuildTrees."""
    elements = {r["id"] for r in t["elements"]}
    skills = {}
    for s in t["monster_skills"]:
        loc = f"monster_skills[{s['id']}]"
        skills[s["id"]] = s
        if s["kind"] in ("buff", "debuff", "passive") and s["stat"] not in EFFECT_STATS:
            errs.add("E023", loc, f"stat '{s['stat']}' is not an effect stat")
        if s["kind"] in ("damage", "heal", "shield") and s["scales"] not in STATS:
            errs.add("E023", loc, f"scales '{s['scales']}' is not a trained stat")
        if s["power"] < 0 or s["cooldown"] < 0 or s["duration"] < 0:
            errs.add("E043", loc, "power, cooldown and duration must be >= 0")
        if s["element"] is not None and s["element"] not in elements:
            errs.add("E023", loc, f"element '{s['element']}' is not an element id")
    trees = {r["id"]: r for r in t["trees"]}
    for r in t["trees"]:
        loc = f"trees[{r['id']}]"
        if r["kind"] == "generic":
            if r["offer_threshold"] is None or r["offer_threshold"] <= 0:
                errs.add("E043", loc, "a generic tree needs offer_threshold > 0")
            if r["offer_stat"] is not None and r["offer_stat"] not in STATS:
                errs.add("E023", loc, f"offer_stat '{r['offer_stat']}' is not a trained stat")
        elif r["offer_stat"] is not None or r["offer_threshold"] is not None:
            errs.add("E043", loc, "only generic trees have offer_stat and offer_threshold")
    by_tree = {}
    nodes = {}
    for n in t["tree_nodes"]:
        loc = f"tree_nodes[{n['id']}]"
        nodes[n["id"]] = n
        if n["tree_id"] not in trees:
            errs.add("E023", loc, f"tree_id '{n['tree_id']}' is not a tree id")
            continue
        by_tree.setdefault(n["tree_id"], []).append(n)
        if n["cost"] < 1:
            errs.add("E043", loc, "cost must be >= 1")
        k = n["kind"]
        if k in ("growth", "stat"):
            if n["stat"] not in STATS:
                errs.add("E023", loc, f"stat '{n['stat']}' is not a trained stat")
            if n["amount"] is None or not n["amount"] > 0:
                errs.add("E043", loc, "amount must be > 0")
        if k in ("skill", "trigger") and n["skill_id"] not in skills:
            errs.add("E023", loc, f"skill_id '{n['skill_id']}' is not a monster skill id")
        if k == "trigger":
            if n["trigger"] is None:
                errs.add("E043", loc, "a trigger node needs a trigger")
            elif n["skill_id"] in skills and skills[n["skill_id"]]["kind"] == "passive":
                errs.add("E043", loc, "a trigger node cannot fire a passive")
        if k == "rule" and n["rule_id"] not in RULE_IDS:
            errs.add("E023", loc, f"rule_id '{n['rule_id']}' is not a known rule modifier")
    for tid in trees:
        ns = sorted(by_tree.get(tid, []), key=lambda n: n["index"])
        if len(ns) < 2:
            errs.add("E044", f"trees[{tid}]", "a tree needs at least 2 nodes")
            continue
        if [n["index"] for n in ns] != list(range(len(ns))):
            errs.add("E060", f"trees[{tid}]", "node index must be 0..N-1 contiguous")
        if ns[-1]["kind"] not in ("skill", "trigger", "rule"):
            errs.add("E064", f"trees[{tid}]", "the last node is the chase trait: it must be a skill, trigger or rule node")
    for sp in t["species"]:
        loc = f"species[{sp['id']}]"
        for col in ("native_tree_1", "native_tree_2"):
            if sp[col] not in trees or trees[sp[col]]["kind"] != "native":
                errs.add("E023", loc, f"{col} '{sp[col]}' is not a native tree id")
        if sp["native_tree_1"] == sp["native_tree_2"]:
            errs.add("E043", loc, "native trees must differ")
    with_conds = set()
    for cd in t["chase_conditions"]:
        loc = f"chase_conditions[{cd['id']}]"
        tid = cd["tree_id"]
        if tid not in trees or trees[tid]["kind"] != "chase":
            errs.add("E023", loc, f"tree_id '{tid}' is not a chase tree id")
            continue
        with_conds.add(tid)
        typ, ref = cd["type"], cd["ref"]
        if typ == "tree_maxed" and (ref not in trees or ref == tid):
            errs.add("E023", loc, f"ref '{ref}' is not another tree id")
        if typ == "node_owned" and (ref not in nodes or nodes[ref]["tree_id"] == tid):
            errs.add("E023", loc, f"ref '{ref}' is not a node of another tree")
        if typ == "stat_at_least" and ref not in STATS:
            errs.add("E023", loc, f"ref '{ref}' is not a trained stat")
        if typ == "tally_at_least" and ref not in TALLIES and not (ref.startswith("damage_") and ref[7:] in elements):
            errs.add("E023", loc, f"ref '{ref}' is not a tally ({', '.join(TALLIES)}, damage_<element>)")
        if typ in ("stat_at_least", "tally_at_least") and (cd["amount"] is None or cd["amount"] < 0):
            errs.add("E043", loc, "amount must be >= 0")
    for tid, r in trees.items():
        if r["kind"] == "chase" and tid not in with_conds:
            errs.add("E065", f"trees[{tid}]", "a chase tree needs at least one condition")
    for b in t["skill_books"]:
        if b["tree_id"] not in trees or trees[b["tree_id"]]["kind"] != "book":
            errs.add("E023", f"skill_books[{b['id']}]", f"tree_id '{b['tree_id']}' is not a book tree id")


def validate_talents(t, errs):
    """Account talent tree (specs/18 section 4). Mirrors ContentDb.BuildAccount."""
    for r in t["talents"]:
        loc = f"talents[{r['id']}]"
        if r["max_rank"] < 1 or r["cost_base"] < 0 or r["cost_step"] < 0:
            errs.add("E043", loc, "need max_rank >= 1, cost_base >= 0, cost_step >= 0")
        p, eff = r["per_rank"], r["effect"]
        if eff == "drill_cost" and not (p <= 0 and 1 + p * r["max_rank"] >= 0):
            errs.add("E043", loc, "drill_cost per_rank must be <= 0 and keep the multiplier >= 0 at max rank")
        if eff == "stat_floor" and not p >= 0:
            errs.add("E043", loc, "stat_floor per_rank must be >= 0")
        if eff in ("draft_size", "start_sp") and not (p >= 0 and p == int(p)):
            errs.add("E043", loc, f"{eff} per_rank must be a whole number >= 0")


def validate_training(t, keys, errs):
    """Training run tables and constants (specs/14 section 8)."""
    kv = {k: keys[k]["value"] for k in keys}
    elements = {r["id"] for r in t["elements"]}
    stat_max = kv.get("STAT_MAX", 999)
    if "RUN_ENERGY_START" in kv and "RUN_ENERGY_MAX" in kv and kv["RUN_ENERGY_START"] > kv["RUN_ENERGY_MAX"]:
        errs.add("E043", "constants[RUN_ENERGY_START]", "value out of range (<= RUN_ENERGY_MAX)")
    if kv.get("DRILL_FAIL_MIN", 0) > kv.get("DRILL_FAIL_MAX", 1):
        errs.add("E043", "constants[DRILL_FAIL_MIN]", "value out of range (<= DRILL_FAIL_MAX)")
    if kv.get("DRILL_FAIL_MAX", 0) + kv.get("DRILL_SUPER_P", 0) > 1:
        errs.add("E043", "constants[DRILL_SUPER_P]", "value out of range (DRILL_FAIL_MAX + DRILL_SUPER_P <= 1)")
    if not t["species"]:
        errs.add("E044", "species", "at least one species is required")
    for r in t["species"]:
        loc = f"species[{r['id']}]"
        if r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        for s in STATS:
            if not 0 <= r["base_" + s] <= stat_max:
                errs.add("E043", loc, f"base_{s} must be in [0, STAT_MAX]")
            if not r["grow_" + s] > 0:
                errs.add("E043", loc, f"grow_{s} must be > 0")
    if sorted(r["id"] for r in t["drill_tiers"]) != sorted(DRILL_TIERS):
        errs.add("E044", "drill_tiers", "must list fail, success, super exactly once")
    for r in t["drill_tiers"]:
        if not 0 <= r["gain_min"] <= r["gain_max"]:
            errs.add("E043", f"drill_tiers[{r['id']}]", "need 0 <= gain_min <= gain_max")
    ranks = t["fight_ranks"]
    if not ranks:
        errs.add("E044", "fight_ranks", "at least one rank is required")
    if sorted(r["sort_order"] for r in ranks) != list(range(len(ranks))):
        errs.add("E047", "fight_ranks", "sort_order must be exactly 0..N-1")
    for r in ranks:
        loc = f"fight_ranks[{r['id']}]"
        if r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        for s in STATS:
            if not 0 <= r[s] <= stat_max:
                errs.add("E043", loc, f"{s} must be in [0, STAT_MAX]")
    validate_trees(t, errs)
    validate_talents(t, errs)
    ms = [r["threshold"] for r in t["stat_milestones"]]
    for i, v in enumerate(ms):
        if v <= 0 or (i > 0 and v <= ms[i - 1]):
            errs.add("E032", f"stat_milestones[{v}]", "thresholds must be > 0 and strictly increasing")


def validate(t, errs):
    # constants
    keys = {r["key"]: r for r in t["constants"]}
    for k, typ in REQUIRED_CONSTANTS.items():
        if k not in keys:
            errs.add("E040", f"constants[{k}]", "required constant missing")
        elif keys[k]["type"] != typ:
            errs.add("E041", f"constants[{k}]", f"type must be {typ}")
    for k in keys:
        if k not in REQUIRED_CONSTANTS:
            errs.add("E042", f"constants[{k}]", "unknown constant")
    # curves
    ids = {r["id"] for r in t["curves"]}
    for cid in ENUMS["CurveId"]:
        if cid not in ids:
            errs.add("E044", f"curves[{cid}]", "curve missing")
    for r in t["curves"]:
        if r["growth"] is not None and r["growth"] <= 1:
            errs.add("E043", f"curves[{r['id']}]", "growth must be > 1")
    # rarity tables
    for tab, col in (("gear_rarity", "rarity"), ("hero_tiers", "tier")):
        have = [r[col] for r in t[tab]]
        if have != TIERS:
            errs.add("E044", tab, f"must list {', '.join(TIERS)} in order")
    s = sum(r["weight"] or 0 for r in t["gear_rarity"])
    if abs(s - 1) > 1e-9:
        errs.add("E030", "gear_rarity", f"weights sum to {s!r}, abs error {abs(s - 1):.3g}")
    # thresholds
    lv = t["hero_level_thresholds"]
    for i, r in enumerate(lv):
        if r["level"] != i + 1:
            errs.add("E060", f"hero_level_thresholds[{r['level']}]", "levels must be 1..N contiguous")
        if i > 0 and r["cum_copies"] <= lv[i - 1]["cum_copies"]:
            errs.add("E031", f"hero_level_thresholds[{r['level']}]", "cum_copies must strictly increase")
    if lv and lv[0]["cum_copies"] != 1:
        errs.add("E061", "hero_level_thresholds[1]", "level 1 must need exactly 1 copy")
    # gear slots
    orders = sorted(r["sort_order"] for r in t["gear_slots"])
    if orders != list(range(len(orders))):
        errs.add("E047", "gear_slots", "sort_order must be exactly 0..N-1")
    # boss rules
    br = t["boss_rules"]
    if not br or br[0]["from_stage"] != 1:
        errs.add("E045", "boss_rules", "first row must have from_stage = 1")
    for r in br:
        if r["atk_mult"] < 0 or r["def_mult"] < 0 or r["spd"] <= 0 or r["aoe_every"] < 0 or r["aoe_power"] < 0:
            errs.add("E043", f"boss_rules[{r['id']}]", "atk_mult, def_mult, aoe_power >= 0; spd > 0; aoe_every >= 0")
    for k in ("BATTLE_GAUGE", "DEF_K_MULT", "ELEMENT_STRONG_MULT", "ELEMENT_WEAK_MULT", "BASIC_ATTACK_POWER"):
        if k in keys and not keys[k]["value"] > 0:
            errs.add("E043", f"constants[{k}]", "value out of range (> 0)")
    for k, (rule, ok) in MON_RANGES.items():
        if k in keys and not ok(keys[k]["value"]):
            errs.add("E043", f"constants[{k}]", f"value out of range ({rule})")
    validate_training(t, keys, errs)
    # promotions (specs/11 section 10)
    pr = t["promotions"]
    for i, r in enumerate(pr):
        loc = f"promotions[{r['index']}]"
        if r["index"] != i + 1:
            errs.add("E060", loc, "index must be 1..N contiguous")
        if i > 0 and r["unlock_stage"] <= pr[i - 1]["unlock_stage"]:
            errs.add("E032", loc, "unlock_stage must strictly increase")
        if r["mult"] <= 0:
            errs.add("E043", loc, "mult must be > 0")
        if r["cost_kills"] < 0:
            errs.add("E043", loc, "cost_kills must be >= 0")
    for r in t["hero_tiers"]:
        if not 0 <= r["max_promotions"] <= len(pr):
            errs.add("E043", f"hero_tiers[{r['tier']}]", f"max_promotions must be in 0..{len(pr)}")
    # roles and elements
    for tab in ("roles", "elements"):
        orders = sorted(r["sort_order"] for r in t[tab])
        if orders != list(range(len(orders))):
            errs.add("E047", tab, "sort_order must be exactly 0..N-1")
    role_ids = {r["id"] for r in t["roles"]}
    element_ids = {r["id"] for r in t["elements"]}
    for e in t["elements"]:
        if e["strong_vs"] not in element_ids:
            errs.add("E023", f"elements[{e['id']}]", f"strong_vs '{e['strong_vs']}' is not an element id")
    # companion stat sheet
    starters = 0
    for h in t["heroes"]:
        loc = f"heroes[{h['id']}]"
        if h["role"] not in role_ids:
            errs.add("E023", loc, f"role '{h['role']}' is not a role id")
        if h["element"] not in element_ids:
            errs.add("E023", loc, f"element '{h['element']}' is not an element id")
        for col in ("hp", "atk", "def", "spd"):
            if h[col] <= 0:
                errs.add("E043", loc, f"{col} must be > 0")
        if not 0 <= h["crit_rate"] <= 1:
            errs.add("E043", loc, "crit_rate must be in [0, 1]")
        if h["crit_dmg"] < 1:
            errs.add("E043", loc, "crit_dmg must be >= 1")
        starters += 1 if h["starter"] else 0
    if starters != 1:
        errs.add("E062", "heroes", f"exactly one hero must have starter = true (found {starters})")
    # skeleton kits (specs/11 section 4.4)
    heroes_by_id = {h["id"]: h for h in t["heroes"]}
    max_promo = {r["tier"]: r["max_promotions"] for r in t["hero_tiers"]}
    kits = {}
    for sk in t["skills"]:
        loc = f"skills[{sk['id']}]"
        if sk["hero_id"] not in heroes_by_id:
            errs.add("E023", loc, f"hero_id '{sk['hero_id']}' is not a hero id")
            continue
        kits.setdefault(sk["hero_id"], []).append(sk)
        kind = sk["kind"]
        if kind in ("damage", "heal", "shield") and sk["scales"] is None:
            errs.add("E054", loc, f"scales required for kind {kind}")
        if kind in ("buff", "debuff", "passive") and sk["stat"] is None:
            errs.add("E054", loc, f"stat required for kind {kind}")
        if sk["stat"] == "hp" and kind != "passive":
            errs.add("E054", loc, "stat hp is only allowed on passives (a timed HP change has no effect)")
        if (kind == "passive") != (sk["cooldown"] == 0):
            errs.add("E043", loc, "cooldown must be 0 for passives and >= 1 otherwise")
        if sk["power"] < 0 or sk["duration"] < 0:
            errs.add("E043", loc, "power and duration must be >= 0")
    for hid, h in heroes_by_id.items():
        kit = sorted(kits.get(hid, []), key=lambda r: r["slot"])
        loc = f"skills[{hid}]"
        if [r["slot"] for r in kit] != list(range(len(kit))):
            errs.add("E060", loc, "slots must be 0..N-1 contiguous per hero")
        if not any(r["unlock_promotion"] == 0 for r in kit):
            errs.add("E063", loc, "a companion needs at least one base skill (unlock_promotion 0)")
        promos = sorted(r["unlock_promotion"] for r in kit if r["unlock_promotion"] > 0)
        cap = max_promo.get(h["tier"], 0)
        if promos != list(range(1, cap + 1)):
            errs.add("E063", loc, f"needs exactly one skill per promotion 1..{cap} (specs/11 section 4.4)")
    # banners
    pools = {}
    for h in t["heroes"]:
        pools.setdefault(h["pool_id"], {}).setdefault(h["tier"], []).append(h["id"])
    streams = set()
    for b in t["banners"]:
        loc = f"banners[{b['id']}]"
        rates = [b[f"rate_{x.lower()}"] or 0 for x in TIERS]
        if abs(sum(rates) - 1) > 1e-9:
            errs.add("E030", loc, f"rates sum to {sum(rates)!r}, abs error {abs(sum(rates) - 1):.3g}")
        if b["pool_id"] not in pools:
            errs.add("E022", loc, f"pool_id '{b['pool_id']}' has no heroes")
        else:
            for tier, rate in zip(TIERS, rates):
                if rate > 0 and not pools[b["pool_id"]].get(tier):
                    errs.add("E033", loc, f"tier {tier} has a rate but no heroes in the pool")
        if b["pity_hard"] is not None and b["pity_soft_start"] is not None and b["pity_hard"] <= b["pity_soft_start"]:
            errs.add("E051", loc, "pity_hard must be > pity_soft_start")
        if b["enabled"]:
            if b["rng_stream"] in streams:
                errs.add("E053", loc, "rng_stream shared with another enabled banner")
            streams.add(b["rng_stream"])
    # tutorial grants
    hero_ids = {h["id"] for h in t["heroes"]}
    for g in t["tutorial_grants"]:
        loc = f"tutorial_grants[{g['id']}]"
        if (g["trigger"] == "stage_reached") != (g["trigger_value"] is not None):
            errs.add("E054", loc, "trigger_value required iff trigger = stage_reached")
        if (g["grant_type"] == "hero") != (g["grant_ref"] is not None):
            errs.add("E054", loc, "grant_ref required iff grant_type = hero")
        if g["grant_ref"] is not None and g["grant_ref"] not in hero_ids:
            errs.add("E023", loc, f"grant_ref '{g['grant_ref']}' is not a hero id")


def shape(name, rows, schema):
    """Apply folds and the per-table JSON shape (specs/05 section 6.1)."""
    out = []
    for r in rows:
        o = {}
        folded = {}
        for fold_name, cols in schema["folds"].items():
            for col in cols:
                folded[col] = fold_name
        emitted_folds = set()
        for cn, _, _ in schema["columns"]:
            if cn in folded:
                fn = folded[cn]
                if fn not in emitted_folds:
                    emitted_folds.add(fn)
                    o[fn] = {col.split("_")[-1].upper(): r[col] for col in schema["folds"][fn]}
                continue
            o[cn] = r[cn]
        if name == "strings":
            o = {"key": r["key"], "text": r["en"]}
        out.append(o)
    doc = {"table": name}
    if name == "strings":
        doc["locale"] = "en"
    doc["rows"] = out
    return doc


def dumps(doc):
    return json.dumps(doc, indent=2, ensure_ascii=False) + "\n"


def build(src):
    errs = Errors()
    tables = {}
    for name, schema in SCHEMA.items():
        path = os.path.join(src, name + ".csv")
        if not os.path.exists(path):
            if schema["required"]:
                errs.add("E001", name, f"missing {path}")
            continue
        tables[name] = read_table(path, name, schema, errs)
    meta = {}
    meta_path = os.path.join(src, "meta.csv")
    if os.path.exists(meta_path):
        with open(meta_path, newline="", encoding="utf-8") as f:
            for row in list(csv.reader(f))[1:]:
                if len(row) >= 2 and row[0].strip():
                    meta[row[0].strip()] = row[1].strip()
    for k in ("content_version", "source_revision"):
        if k not in meta:
            errs.add("E070", "meta", f"missing {k}")
    if not errs:
        validate(tables, errs)
    if errs:
        return None, errs
    files = {}
    for name in sorted(tables):
        files[name + ".json"] = dumps(shape(name, tables[name], SCHEMA[name])).encode("utf-8")
    entries = {}
    for fname in sorted(files):
        entries[fname[:-5]] = {"file": fname, "sha256": hashlib.sha256(files[fname]).hexdigest()}
    content_hash = hashlib.sha256("".join(e["sha256"] for e in entries.values()).encode()).hexdigest()
    manifest = {
        "schema_version": SCHEMA_VERSION,
        "content_version": meta["content_version"],
        "source_revision": meta["source_revision"],
        "tables": entries,
        "content_hash": content_hash,
    }
    files["manifest.json"] = dumps(manifest).encode("utf-8")
    summary = {"tables": tables, "manifest": manifest}
    return (files, summary), errs


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--src", default="content_src")
    ap.add_argument("--out", default="Content")
    ap.add_argument("--check", action="store_true", help="exit 1 if --out differs from a fresh build")
    ap.add_argument("--copy-to", default=None)
    args = ap.parse_args(argv)

    if not os.path.isdir(args.src):
        print(f"source directory not found: {args.src}", file=sys.stderr)
        return 2
    result, errs = build(args.src)
    if errs:
        for e in errs:
            print(e, file=sys.stderr)
        return 1
    files, summary = result

    if args.check:
        stale = []
        for fname, data in files.items():
            p = os.path.join(args.out, fname)
            if not os.path.exists(p) or open(p, "rb").read() != data:
                stale.append(fname)
        if stale:
            print("Content/ is stale: " + ", ".join(sorted(stale)), file=sys.stderr)
            return 1
        print("Content/ is up to date")
        return 0

    os.makedirs(args.out, exist_ok=True)
    for fname, data in files.items():
        with open(os.path.join(args.out, fname), "wb") as f:
            f.write(data)
    if args.copy_to:
        os.makedirs(args.copy_to, exist_ok=True)
        for fname in files:
            shutil.copyfile(os.path.join(args.out, fname), os.path.join(args.copy_to, fname))

    m = summary["manifest"]
    print(f"content_version {m['content_version']}  content_hash {m['content_hash']}")
    for name, rows in sorted(summary["tables"].items()):
        print(f"  {name:24s} {len(rows):4d} rows")
    pools = {}
    for h in summary["tables"]["heroes"]:
        pools.setdefault(h["pool_id"], {}).setdefault(h["tier"], 0)
        pools[h["pool_id"]][h["tier"]] += 1
    for pid, counts in pools.items():
        print(f"  pool {pid}: " + " / ".join(f"{t} {counts.get(t, 0)}" for t in TIERS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
