#!/usr/bin/env python3
"""Content converter: content_src/<table>.csv -> Content/<table>.json + manifest.json (specs/05).

Prototype scope: type checks, key uniqueness, enum values and the handful of cross-table rules the sim
depends on, plus the previous-build rules E090 and E091 (specs/05 section 6). The full error-code catalogue is not built yet.

    python3 tools/content_build.py [--src content_src] [--out Content] [--check] [--copy-to DIR]
                                   [--baseline DIR | --baseline-ref GITREF]

--baseline compares the fresh build with an earlier Content/ directory; --baseline-ref reads that Content/ from a git ref
(for example the merge base with origin/main).

Exit codes: 0 ok, 1 validation errors (all listed), 2 usage / IO error. Stdlib only.
"""
import argparse
import csv
import hashlib
import json
import math
import os
import re
import shutil
import subprocess
import sys

SCHEMA_VERSION = 1

ENUMS = {
    "ConstantType": ["int", "double", "bool"],
    "TreeKind": ["native", "generic", "chase", "book", "fusion"],
    "NodeKind": ["growth", "stat", "skill", "trigger", "rule", "growth_use"],
    "FightTrigger": ["battle_start", "on_action", "on_basic_attack", "on_crit", "on_hit_taken", "on_evade"],
    "ConditionType": ["tree_maxed", "node_owned", "stat_at_least", "tally_at_least"],
    "TalentEffect": ["drill_cost", "stat_floor", "start_sp", "rest_energy", "fail_chance", "super_chance",
                     "shard_mult", "momentum", "loss_sp", "specialist", "tree_slots", "injury_shield", "last_cycle_ap",
                     "milestone_mult", "draft_rerolls", "iron_constitution", "gamblers_blood", "glass_cannon",
                     "echo_training"],
    "AchievementKind": ["runs_finished", "rank_reached", "stat_reached", "gauntlet_floors", "endless_floor"],
    "SkillKind": ["damage", "heal", "shield", "taunt", "buff", "debuff", "passive"],
    "SkillTarget": ["enemy", "all_enemies", "self", "lowest_ally", "all_allies"],
    "TowerRole": ["regular", "marquee", "add"],
    "PhaseKind": ["shield_check", "split", "channel", "enrage"],
    "EndlessRole": ["minion", "tank", "striker", "caster", "healer", "support"],
    "EndlessCheck": ["aoe", "focus", "open"],
    "MutatorKind": ["heal_mult", "aoe_mult", "single_mult", "element_mult", "speed_mult", "crit_bonus", "corruption_early",
                    "start_shield"],
}

# key -> type. Mirrors IdleRng.Sim.Content.Constants (specs/16 to specs/19 list each group).
REQUIRED_CONSTANTS = {
    "SPD_REF": "double",
    "BATTLE_GAUGE": "double", "ELEMENT_STRONG_MULT": "double", "ELEMENT_WEAK_MULT": "double",
    "BASIC_ATTACK_POWER": "double", "AI_HEAL_BELOW": "double", "AI_SHIELD_BELOW": "double",
    # monster battle (specs/16)
    "MON_HP_PER_POINT": "double", "MON_DEF_K": "double", "MON_EVADE_BASE": "double", "MON_EVADE_PER_DEX": "double",
    "MON_EVADE_CAP": "double", "MON_CRIT_BASE": "double", "MON_CRIT_PER_LUK": "double", "MON_CRIT_CAP": "double",
    "MON_CRIT_DMG": "double", "MON_CRIT_DMG_PER_DEX": "double", "MON_GRAZE_MULT": "double", "MON_FORTUNE_K": "double", "MON_FORTUNE_CAP": "double", "MON_SPD_MIN": "double", "MON_TIME_LIMIT_TICKS": "int", "MON_SIDE_SIZE": "int",
    "STAT_FLOOR_FRAC": "double", "EFFECT_STACK_CAP": "int",
    # training run (specs/14)
    "RUN_CYCLES": "int", "RUN_AP": "int", "RUN_ENERGY_MAX": "double", "RUN_ENERGY_START": "double",
    "DRILL_ENERGY_COST": "double", "REST_ENERGY": "double", "DRILL_FAIL_MIN": "double", "DRILL_FAIL_MAX": "double",
    "DRILL_SUPER_P": "double", "INJURY_BELOW": "double", "INJURY_MAX_P": "double", "INJURY_SKIP_CYCLES": "int",
    "INJURY_RECOVER_ENERGY": "double", "STAT_MAX": "double", "FIGHT_EVERY": "int",
    # skill trees (specs/15)
    "SP_PER_CYCLE": "int", "SP_PER_WIN": "int", "TREE_SLOTS": "int", "DRAFT_SIZE": "int", "TREE_REFUND": "double",
    "ROLL_OWN_CHANCE": "double", "ROLL_GROWTH_OWN_MIN": "double", "ROLL_GROWTH_OWN_MAX": "double", "ROLL_GROWTH_OFF_MIN": "double",
    "ROLL_GROWTH_OFF_MAX": "double", "ROLL_FLAT_OWN_MIN": "double", "ROLL_FLAT_OWN_MAX": "double", "ROLL_FLAT_OFF_MIN": "double",
    "ROLL_FLAT_OFF_MAX": "double", "ROLL_CAP_GROWTH_MIN": "double", "ROLL_CAP_GROWTH_MAX": "double",
    "MON_ECHO_POWER": "double",
    "MON_TIDE_REGEN": "double", "MON_TIDE_SHIELD_CAP": "double", "MON_DRAIN_FRAC": "double", "MON_FIRE_PIERCE": "double",
    "MON_DOUBLE_TURNS": "int", "MON_REBIRTH_HEAL": "double", "MON_WAIL_STAGGER": "double", "MON_WAIL_WITHER": "double", "MON_VEIL_EVADE_CAP": "double",
    "MON_FEAST_STR": "double", "MON_COUNTER_MULT": "double", "MON_FIRE_BONUS": "double", "MON_VEIL_EVADE_BONUS": "double",
    "MON_FEAST_DMG": "double", "MON_FEAST_START": "double", "MON_ECHO_DECOYS": "int", "MON_DRAIN_SHIELD_CAP": "double",
    "MON_TRIGGER_DEPTH": "int", "MON_EXTRA_ACTIONS_CAP": "int",
    # idea-pool lane B (#135): survival rules
    "MON_RESENT_FRAC": "double", "MON_RESENT_DECAY": "double", "MON_VANTAGE_HP": "double", "MON_DOOR_START": "double",
    "MON_DOOR_STEP": "double", "MON_BARRICADE_CAP": "double", "MON_WARD_WIS": "double", "MON_WARD_SHARE": "double", "MON_WARD_CDR": "double",
    # account and meta-progression (specs/18)
    "SHARDS_PER_CYCLE": "int", "SHARDS_PER_RANK": "int", "VAULT_CAP": "int", "VENDOR_OFFERS": "int", "REROLL_BASE": "int", "REROLL_STEP": "int", "LEGENDARY_SLOTS": "int",
    # end-of-run gauntlet (specs/19)
    "GAUNTLET_FLOORS": "int", "GAUNTLET_BASE": "double", "GAUNTLET_GROWTH": "double", "GAUNTLET_BOSS_EVERY": "int",
    "GAUNTLET_BOSS_MULT": "double",
    # Dungeon (specs/21 section 5)
    "TOWER_SEED": "int", "TOWER_FLOORS": "int", "TOWER_ATTEMPTS_PER_DAY": "int", "TOWER_TICKET_CAP": "int", "TOWER_TICKET_PIECES": "int", "TOWER_BASE": "double", "TOWER_GROWTH": "double",
    "TOWER_BOSS_HP_MULT": "double", "TOWER_MILESTONE_EVERY": "int", "TOWER_SHARDS_PER_FLOOR": "int", "TOWER_BOOK_CHANCE": "double",
    "TOWER_BOOK_CHANCE_PER_FLOOR": "double",
    "ENTROPY_CAP": "double", "ENTROPY_PER_TRIGGER": "double", "ENTROPY_PER_ECHO": "double", "ENTROPY_PER_COUNTER": "double",
    "ENTROPY_DECAY": "double", "ENTROPY_RECOIL_FRAC": "double", "ENTROPY_STUN_GAUGE": "double", "ENTROPY_ENRAGE_FRAC": "double",
    "ENTROPY_ENRAGE_TURNS": "int", "ENTROPY_CONVERT_SHIELD": "double", "ENTROPY_CONVERT_TURNS": "int",
    "CORRUPT_START_TICK": "int", "CORRUPT_EVERY_TICKS": "int", "CORRUPT_HEAL_PER_LEVEL": "double", "CORRUPT_HEAL_FLOOR": "double",
    "CORRUPT_DMG_PER_LEVEL": "double",
    # Endless Tower (specs/28 section 7)
    "ENDLESS_SEED": "int", "ENDLESS_BASE": "double", "ENDLESS_GROWTH": "double", "ENDLESS_BAND": "int",
    "ENDLESS_ELITE_HP_MULT": "double", "ENDLESS_MAX_FLOORS": "int",
    "ENDLESS_SHARDS_BASE": "int", "ENDLESS_SHARDS_PER_FLOOR": "double", "ENDLESS_ELITE_SHARD_MULT": "int", "GAMBIT_SLOTS": "int",
    # idea-pool lane A, conversion rules (specs/16 section 6.2, #134)
    "MON_IRON_DEX": "double", "MON_AVATAR_BONUS": "double", "MON_FORTRESS_DEF": "double", "MON_BLOOD_COST": "double",
    "MON_BLOOD_FLOOR": "double",
    # idea-pool lane C, turn-gauge rules (specs/16 section 6.2, #136)
    "MON_BRAVE_MAX": "int", "MON_BRAVE_GUARD": "double", "MON_ONE_MORE": "double", "MON_BREAK_PIPS": "int",
    "MON_BREAK_BONUS": "double", "MON_LATENT_ACTIONS": "int", "MON_LATENT_BONUS": "double",
    # idea-pool lane E, team and mode rules (specs/16 section 6.2, #138)
    "MON_LONE_BONUS": "double", "MON_LONE_MALUS": "double", "MON_ARISE_STATS": "double", "MON_RITE_SHARE": "double",
    # idea-pool lane F, tree and kit run rules (specs/15 section 11, #139)
    "KEEPSAKE_STEP": "double", "KEEPSAKE_DRILLS": "int", "KEEPSAKE_MAX": "double", "MASTERY_WINS": "int",
    "LIMIT_BREAK_MAX": "double",
    # idea-pool lane D, debuff and stacking rules (specs/16 section 6.2, #137)
    "MON_BLEED_PER_HIT": "double", "MON_BLEED_DEX_REF": "double", "MON_BLEED_BURST": "double", "MON_PRIV_BONUS": "double",
    "MON_PRIV_DEBUFFS": "int", "MON_EQ_BONUS": "double",
    # idea-pool lane G, run rules (specs/14 section 4.3, #140)
    "VOW_GAIN": "double", "CURSE_FOE": "double", "DEVOUR_FRAC": "double", "DEVOUR_ENERGY": "double", "BREATH_SAFE": "double",
    "ASCETIC_AP": "int", "HUNGRY_STAT": "double", "HUNGRY_ENERGY": "double",
    # species Keepshell Crab (#152), tide_moat, unbreached and shellcrack (specs/16 section 6.2)
    "MON_MOAT_DEF": "double", "MON_WALL_DEF": "double", "MON_WALL_ERODE": "double", "MON_CRACK_DEF": "double",
}

STATS = ["hp", "str", "int", "wis", "dex", "def", "spd", "luk"]
DRILL_TIERS = ["fail", "success", "super"]
EFFECT_STATS = STATS + ["crit_rate", "crit_dmg", "dmg_dealt", "entropy_gain"]
# Coded rule modifiers (IdleRng.Sim.Combat.RuleModifiers.Known, specs/16 section 6.2).
RULE_IDS = ["counter_mitigated", "echo_cast", "tidal_overflow", "abyssal_drain", "fire_pierce", "double_turn_start",
            "entropy_convert", "root_rebirth", "wail_stagger", "umbral_veil", "night_feast",
            # idea-pool lane A (#134)
            "iron_reflexes", "elemental_avatar", "fortress_fist", "blood_magic",
            # idea-pool lane B (#135)
            "resentment", "vantage", "deaths_door", "barricade", "mana_ward",
            # idea-pool lane C (#136)
            "brave_heart", "one_more", "guard_break", "latent_power",
            # idea-pool lane E (#138)
            "lone_wolf", "arise", "last_rite",
            # idea-pool lane F (#139): run rules (tree and kit build)
            "mimic_core", "heirloom", "mastery", "limit_break",
            # idea-pool lane D (#137)
            "hemorrhage", "privileged_status", "equilibrium", "corruption", "dead_branch",
            # idea-pool lane G (#140): run rules, coded in TrainingRun (Training/Rules/LaneG.cs)
            "iron_vow", "cursed_mark", "devourer", "second_breath", "ascetic", "hungry_core",
            # species Keepshell Crab (#152)
            "tide_moat", "unbreached", "shellcrack"]
TALLIES = ["damage_dealt", "evades", "crits", "kills", "wins"]

# Range rules for the monster battle constants (specs/16 section 7): key -> (rule text, predicate).
MON_RANGES = {
    "MON_HP_PER_POINT": ("> 0", lambda v: v > 0), "MON_DEF_K": ("> 0", lambda v: v > 0),
    "MON_EVADE_BASE": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_EVADE_PER_DEX": (">= 0", lambda v: v >= 0),
    "MON_EVADE_CAP": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_CRIT_BASE": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_CRIT_PER_LUK": (">= 0", lambda v: v >= 0), "MON_CRIT_CAP": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_CRIT_DMG": (">= 1", lambda v: v >= 1), "MON_CRIT_DMG_PER_DEX": (">= 0", lambda v: v >= 0), "MON_GRAZE_MULT": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_FORTUNE_K": ("> 0", lambda v: v > 0), "MON_FORTUNE_CAP": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_SPD_MIN": ("> 0", lambda v: v > 0),
    "MON_TIME_LIMIT_TICKS": ("> 0", lambda v: v > 0), "MON_SIDE_SIZE": ("> 0", lambda v: v > 0),
    "STAT_FLOOR_FRAC": ("(0, 1]", lambda v: 0 < v <= 1), "EFFECT_STACK_CAP": (">= 1", lambda v: v >= 1),
    "RUN_CYCLES": ("> 0", lambda v: v > 0), "RUN_AP": ("> 0", lambda v: v > 0), "RUN_ENERGY_MAX": ("> 0", lambda v: v > 0),
    "RUN_ENERGY_START": (">= 0", lambda v: v >= 0), "DRILL_ENERGY_COST": (">= 0", lambda v: v >= 0),
    "REST_ENERGY": (">= 0", lambda v: v >= 0), "DRILL_FAIL_MIN": ("[0, 1]", lambda v: 0 <= v <= 1),
    "DRILL_FAIL_MAX": ("[0, 1]", lambda v: 0 <= v <= 1), "DRILL_SUPER_P": ("[0, 1]", lambda v: 0 <= v <= 1),
    "INJURY_BELOW": ("[0, 1]", lambda v: 0 <= v <= 1), "INJURY_MAX_P": ("[0, 1]", lambda v: 0 <= v <= 1),
    "INJURY_SKIP_CYCLES": (">= 0", lambda v: v >= 0), "INJURY_RECOVER_ENERGY": (">= 0", lambda v: v >= 0),
    "STAT_MAX": ("> 0", lambda v: v > 0), "FIGHT_EVERY": ("> 0", lambda v: v > 0),
    "SP_PER_CYCLE": (">= 0", lambda v: v >= 0), "SP_PER_WIN": (">= 0", lambda v: v >= 0),
    "TREE_SLOTS": (">= 2", lambda v: v >= 2), "DRAFT_SIZE": ("> 0", lambda v: v > 0),
    "TREE_REFUND": ("[0, 1]", lambda v: 0 <= v <= 1),
    "ROLL_OWN_CHANCE": ("[0, 1]", lambda v: 0 <= v <= 1),
    **{k: (">= 0", lambda v: v >= 0) for k in ["ROLL_GROWTH_OWN_MIN", "ROLL_GROWTH_OWN_MAX", "ROLL_GROWTH_OFF_MIN", "ROLL_GROWTH_OFF_MAX",
        "ROLL_FLAT_OWN_MIN", "ROLL_FLAT_OWN_MAX", "ROLL_FLAT_OFF_MIN", "ROLL_FLAT_OFF_MAX", "ROLL_CAP_GROWTH_MIN", "ROLL_CAP_GROWTH_MAX"]},
    "MON_ECHO_POWER": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_TIDE_REGEN": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_TIDE_SHIELD_CAP": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_DRAIN_FRAC": (">= 0", lambda v: v >= 0), "MON_FIRE_PIERCE": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_DOUBLE_TURNS": (">= 1", lambda v: v >= 1),
    "MON_REBIRTH_HEAL": ("(0, 1]", lambda v: 0 < v <= 1), "MON_WAIL_STAGGER": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_WAIL_WITHER": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_VEIL_EVADE_CAP": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_FEAST_STR": (">= 0", lambda v: v >= 0),
    "MON_COUNTER_MULT": (">= 0", lambda v: v >= 0), "MON_FIRE_BONUS": (">= 0", lambda v: v >= 0),
    "MON_VEIL_EVADE_BONUS": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_FEAST_DMG": (">= 0", lambda v: v >= 0), "MON_FEAST_START": (">= 0", lambda v: v >= 0), "MON_ECHO_DECOYS": (">= 0", lambda v: v >= 0),
    "MON_DRAIN_SHIELD_CAP": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_TRIGGER_DEPTH": (">= 1", lambda v: v >= 1), "MON_EXTRA_ACTIONS_CAP": (">= 0", lambda v: v >= 0),
    # idea-pool lane B (#135)
    "MON_RESENT_FRAC": (">= 0", lambda v: v >= 0), "MON_RESENT_DECAY": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_VANTAGE_HP": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_DOOR_START": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_DOOR_STEP": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_BARRICADE_CAP": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_WARD_WIS": (">= 0", lambda v: v >= 0), "MON_WARD_SHARE": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_WARD_CDR": (">= 0", lambda v: v >= 0),
    "SHARDS_PER_CYCLE": (">= 0", lambda v: v >= 0), "SHARDS_PER_RANK": (">= 0", lambda v: v >= 0),
    "VAULT_CAP": ("> 0", lambda v: v > 0), "VENDOR_OFFERS": ("> 0", lambda v: v > 0),
    "REROLL_BASE": (">= 0", lambda v: v >= 0), "REROLL_STEP": (">= 0", lambda v: v >= 0), "LEGENDARY_SLOTS": (">= 0", lambda v: v >= 0),
    "GAUNTLET_FLOORS": ("> 0", lambda v: v > 0), "GAUNTLET_BASE": ("> 0", lambda v: v > 0),
    "GAUNTLET_GROWTH": (">= 1", lambda v: v >= 1), "GAUNTLET_BOSS_EVERY": ("> 0", lambda v: v > 0),
    "GAUNTLET_BOSS_MULT": ("> 0", lambda v: v > 0),
    "TOWER_FLOORS": ("> 0", lambda v: v > 0), "TOWER_ATTEMPTS_PER_DAY": ("> 0", lambda v: v > 0),
    "TOWER_TICKET_CAP": ("> 0", lambda v: v > 0), "TOWER_TICKET_PIECES": ("> 0", lambda v: v > 0),
    "TOWER_BASE": ("> 0", lambda v: v > 0), "TOWER_GROWTH": (">= 1", lambda v: v >= 1),
    "TOWER_BOSS_HP_MULT": ("> 0", lambda v: v > 0), "TOWER_MILESTONE_EVERY": ("> 0", lambda v: v > 0),
    "TOWER_SHARDS_PER_FLOOR": (">= 0", lambda v: v >= 0), "TOWER_BOOK_CHANCE": ("[0, 1]", lambda v: 0 <= v <= 1),
    "TOWER_BOOK_CHANCE_PER_FLOOR": (">= 0", lambda v: v >= 0),
    "ENTROPY_CAP": ("> 0", lambda v: v > 0), "ENTROPY_PER_TRIGGER": (">= 0", lambda v: v >= 0),
    "ENTROPY_PER_ECHO": (">= 0", lambda v: v >= 0), "ENTROPY_PER_COUNTER": (">= 0", lambda v: v >= 0),
    "ENTROPY_DECAY": (">= 0", lambda v: v >= 0), "ENTROPY_RECOIL_FRAC": ("[0, 1]", lambda v: 0 <= v <= 1),
    "ENTROPY_STUN_GAUGE": (">= 0", lambda v: v >= 0), "ENTROPY_ENRAGE_FRAC": (">= 0", lambda v: v >= 0),
    "ENTROPY_ENRAGE_TURNS": ("> 0", lambda v: v > 0), "ENTROPY_CONVERT_SHIELD": (">= 0", lambda v: v >= 0),
    "ENTROPY_CONVERT_TURNS": ("> 0", lambda v: v > 0),
    "CORRUPT_START_TICK": (">= 0", lambda v: v >= 0), "CORRUPT_EVERY_TICKS": ("> 0", lambda v: v > 0),
    "CORRUPT_HEAL_PER_LEVEL": (">= 0", lambda v: v >= 0), "CORRUPT_HEAL_FLOOR": ("[0, 1]", lambda v: 0 <= v <= 1),
    "CORRUPT_DMG_PER_LEVEL": (">= 0", lambda v: v >= 0),
    "ENDLESS_SHARDS_BASE": (">= 0", lambda v: v >= 0), "ENDLESS_SHARDS_PER_FLOOR": (">= 0", lambda v: v >= 0),
    "ENDLESS_ELITE_SHARD_MULT": (">= 1", lambda v: v >= 1), "GAMBIT_SLOTS": ("> 0", lambda v: v > 0),
    # idea-pool lane A (#134)
    "MON_IRON_DEX": (">= 0", lambda v: v >= 0), "MON_AVATAR_BONUS": (">= 0", lambda v: v >= 0),
    "MON_FORTRESS_DEF": (">= 0", lambda v: v >= 0), "MON_BLOOD_COST": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_BLOOD_FLOOR": ("[0, 1]", lambda v: 0 <= v <= 1),
    # idea-pool lane C (#136)
    "MON_BRAVE_MAX": (">= 1", lambda v: v >= 1), "MON_BRAVE_GUARD": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_ONE_MORE": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_BREAK_PIPS": (">= 1", lambda v: v >= 1),
    "MON_BREAK_BONUS": (">= 0", lambda v: v >= 0), "MON_LATENT_ACTIONS": (">= 1", lambda v: v >= 1),
    "MON_LATENT_BONUS": (">= 0", lambda v: v >= 0),
    # idea-pool lane E (#138)
    "MON_LONE_BONUS": (">= 0", lambda v: v >= 0), "MON_LONE_MALUS": ("[0, 1]", lambda v: 0 <= v <= 1),
    "MON_ARISE_STATS": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_RITE_SHARE": (">= 0", lambda v: v >= 0),
    # idea-pool lane F (#139)
    "KEEPSAKE_STEP": (">= 0", lambda v: v >= 0), "KEEPSAKE_DRILLS": (">= 1", lambda v: v >= 1),
    "KEEPSAKE_MAX": (">= 0", lambda v: v >= 0), "MASTERY_WINS": (">= 1", lambda v: v >= 1),
    "LIMIT_BREAK_MAX": (">= 0", lambda v: v >= 0),   # and >= STAT_MAX, checked in validate
    # idea-pool lane D (#137)
    "MON_BLEED_PER_HIT": (">= 0", lambda v: v >= 0), "MON_BLEED_DEX_REF": ("> 0", lambda v: v > 0),
    "MON_BLEED_BURST": ("[0, 1]", lambda v: 0 <= v <= 1), "MON_PRIV_BONUS": (">= 0", lambda v: v >= 0),
    "MON_PRIV_DEBUFFS": (">= 1", lambda v: v >= 1), "MON_EQ_BONUS": (">= 0", lambda v: v >= 0),
    # idea-pool lane G (#140)
    "VOW_GAIN": (">= 0", lambda v: v >= 0), "CURSE_FOE": ("[0, 1]", lambda v: 0 <= v <= 1),
    "DEVOUR_FRAC": ("[0, 1]", lambda v: 0 <= v <= 1), "DEVOUR_ENERGY": (">= 0", lambda v: v >= 0),
    "BREATH_SAFE": (">= 0", lambda v: v >= 0), "ASCETIC_AP": (">= 0", lambda v: v >= 0),
    "HUNGRY_STAT": (">= 0", lambda v: v >= 0), "HUNGRY_ENERGY": (">= 0", lambda v: v >= 0),
    # species Keepshell Crab (#152): the walls must always fall, so the erosion is never 0
    "MON_MOAT_DEF": (">= 0", lambda v: v >= 0), "MON_WALL_DEF": (">= 0", lambda v: v >= 0),
    "MON_WALL_ERODE": ("(0, 1]", lambda v: 0 < v <= 1), "MON_CRACK_DEF": (">= 0", lambda v: v >= 0),
}



def T(columns, key=None, folds=None, required=True):
    """columns: list of (name, type, nullable)."""
    return {"columns": columns, "key": key, "folds": folds or {}, "required": required}


def c(name, typ, nullable=False):
    return (name, typ, nullable)


SCHEMA = {
    "constants": T([c("key", "string"), c("type", "enum:ConstantType"), c("value", "constval")], key="key"),
    "elements": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"), c("display_color", "color"),
                   c("strong_vs", "id")], key="id"),
    "species": T([c("id", "id"), c("name_key", "key"), c("element", "id"), c("native_tree_1", "id"), c("native_tree_2", "id"),
                  c("chase_tree_run", "id"), c("chase_tree_book", "id")]
                  + [c("base_" + s, "double") for s in STATS] + [c("grow_" + s, "double") for s in STATS], key="id"),
    "drill_tiers": T([c("id", "id"), c("gain_min", "int"), c("gain_max", "int"), c("name_key", "key")], key="id"),
    "fight_ranks": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"), c("element", "id")]
                     + [c(s, "double") for s in STATS] + [c("dismantle_shards", "int"), c("ticket_pieces", "int")], key="id"),
    "rank_enemies": T([c("id", "id"), c("rank", "id"), c("name_key", "key"), c("element", "id")]
                      + [c(s, "double") for s in STATS] + [c("skills", "string", True), c("weight", "double")], key="id"),
    "stat_milestones": T([c("threshold", "int")], key="threshold"),
    "monster_skills": T([c("id", "id"), c("name_key", "key"), c("kind", "enum:SkillKind"),
                         c("target", "enum:SkillTarget"), c("stat", "string", True), c("scales", "string", True),
                         c("power", "double"), c("cooldown", "int"), c("duration", "int"), c("element", "id", True)], key="id"),
    "trees": T([c("id", "id"), c("name_key", "key"), c("kind", "enum:TreeKind"), c("offer_stat", "string", True),
                c("offer_threshold", "int", True), c("apex_name_key", "key", True),
                c("fuse_a", "id", True), c("fuse_b", "id", True)], key="id"),
    "tree_nodes": T([c("id", "id"), c("tree_id", "id"), c("index", "int"), c("cost", "int"), c("kind", "enum:NodeKind"),
                     c("stat", "string", True), c("amount", "double", True), c("skill_id", "id", True),
                     c("trigger", "enum:FightTrigger", True), c("rule_id", "id", True)], key="id"),
    "chase_conditions": T([c("id", "id"), c("tree_id", "id"), c("type", "enum:ConditionType"), c("ref", "string"),
                           c("amount", "double", True)], key="id"),
    "skill_books": T([c("id", "id"), c("name_key", "key"), c("tree_id", "id")], key="id"),
    "gauntlet_enemies": T([c("id", "id"), c("name_key", "key"), c("element", "id"), c("boss", "bool")]
                           + [c(s, "double") for s in STATS], key="id"),
    "legacy_titles": T([c("min_floor", "int"), c("title_key", "key")], key="min_floor"),
    "tower_bosses": T([c("id", "id"), c("name_key", "key"), c("element", "id"), c("role", "enum:TowerRole")]
                       + [c(s, "double") for s in STATS] + [c("skills", "string", True)], key="id"),
    "boss_phases": T([c("id", "id"), c("boss_id", "id"), c("order", "int"), c("enter_hp", "double"), c("kind", "enum:PhaseKind"),
                      c("power", "double"), c("duration", "int"), c("count", "int"), c("guard", "double"), c("heal_frac", "double"),
                      c("strike", "double"), c("buff", "double"), c("adds", "id", True), c("add_skills", "string", True)], key="id"),
    "endless_enemies": T([c("id", "id"), c("name_key", "key"), c("element", "id"), c("role", "enum:EndlessRole")]
                          + [c(s, "double") for s in STATS] + [c("skills", "string", True), c("gambits", "string", True)], key="id"),
    "endless_shapes": T([c("id", "id"), c("name_key", "key"), c("check", "enum:EndlessCheck"), c("slots", "string"),
                         c("anchor_hp", "double")], key="id"),
    "mutators": T([c("id", "id"), c("name_key", "key"), c("kind", "enum:MutatorKind"), c("value", "double"),
                   c("element", "id", True)], key="id"),
    "book_drops": T([c("book_id", "id"), c("min_floor", "int"), c("weight", "double")], key="book_id"),
    "talents": T([c("id", "id"), c("name_key", "key"), c("desc_key", "key"), c("rarity", "id"), c("effect", "enum:TalentEffect"),
                  c("per_rank", "double"), c("max_rank", "int"), c("cost_base", "int"), c("cost_step", "int")], key="id"),
    "achievements": T([c("id", "id"), c("name_key", "key"), c("kind", "enum:AchievementKind"), c("ref", "string", True),
                       c("amount", "double", True), c("unlocks_species", "id", True)], key="id"),
    "vendor_rarities": T([c("id", "id"), c("sort_order", "int"), c("name_key", "key"),
                          c("weight_base", "double"), c("weight_per_rank", "double"), c("legendary_slot", "bool")], key="id"),
    "strings": T([c("key", "key"), c("en", "string")], key="key", required=False),
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
        if len(cells) > len(header) and not (header and header[-1].startswith("#")):
            # An unquoted comma splits a cell and the rest would be dropped without a word (a string cut short).
            errs.add("E004", f"{name}[row {i}]", f"{len(cells)} cells but {len(header)} columns: quote a cell that holds a comma")
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
        if (r["kind"] in ("chase", "fusion")) != (r["apex_name_key"] is not None):
            errs.add("E043", loc, "chase and fusion trees, and only they, have an apex_name_key")
        if (r["kind"] == "fusion") != (r["fuse_a"] is not None and r["fuse_b"] is not None) or \
                (r["kind"] != "fusion" and (r["fuse_a"] is not None or r["fuse_b"] is not None)):
            errs.add("E043", loc, "fusion trees, and only they, name the two trees they fuse (fuse_a, fuse_b)")
    fused = {}
    for r in t["trees"]:
        if r["kind"] != "fusion" or r["fuse_a"] is None or r["fuse_b"] is None:
            continue
        loc = f"trees[{r['id']}]"
        if r["fuse_a"] == r["fuse_b"]:
            errs.add("E043", loc, "a fusion tree fuses two different trees")
        for p in (r["fuse_a"], r["fuse_b"]):
            if p not in trees:
                errs.add("E023", loc, f"fuses '{p}', which is not a tree id")
            elif trees[p]["kind"] not in ("native", "generic", "book"):
                errs.add("E043", loc, f"fuses '{p}': only native, generic and book trees fuse")
            elif p in fused:
                errs.add("E043", loc, f"'{p}' already fuses into {fused[p]}")
            else:
                fused[p] = r["id"]
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
        if k in ("growth", "stat", "growth_use"):
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
        if k == "rule" and n["rule_id"] == "fire_pierce" and "fire" not in elements:
            errs.add("E023", loc, "fire_pierce works on the 'fire' element, which is not an element id")
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
    # Each species owns two chase trees: the in-run one and a second one, book-gated or (no book node) a harder in-run chase
    # (kj, 2026-11-03; specs/15 section 5.1).
    def book_refs(tid):
        out = 0
        for cd in t["chase_conditions"]:
            if cd["tree_id"] != tid:
                continue
            ref = cd["ref"]
            if cd["type"] == "node_owned" and ref in nodes:
                ref = nodes[ref]["tree_id"]
            elif cd["type"] != "tree_maxed":
                continue
            if ref in trees and trees[ref]["kind"] == "book":
                out += 1
        return out
    owner = {}
    for sp in t["species"]:
        loc = f"species[{sp['id']}]"
        for col in ("chase_tree_run", "chase_tree_book"):
            tid = sp[col]
            if tid not in trees or trees[tid]["kind"] != "chase":
                errs.add("E023", loc, f"{col} '{tid}' is not a chase tree id")
                continue
            if tid in owner:
                errs.add("E043", loc, f"chase tree '{tid}' already belongs to species '{owner[tid]}'")
            owner.setdefault(tid, sp["id"])
            if col == "chase_tree_run" and book_refs(tid) > 0:
                errs.add("E043", loc, f"in-run chase tree '{tid}' must not need a book tree")
        if sp["chase_tree_run"] == sp["chase_tree_book"]:
            errs.add("E043", loc, "chase trees must differ")
    for tid, r in trees.items():
        if r["kind"] == "chase" and tid not in owner:
            errs.add("E043", f"trees[{tid}]", "a chase tree must belong to a species (chase_tree_run or chase_tree_book)")
    for b in t["skill_books"]:
        if b["tree_id"] not in trees or trees[b["tree_id"]]["kind"] != "book":
            errs.add("E023", f"skill_books[{b['id']}]", f"tree_id '{b['tree_id']}' is not a book tree id")


def validate_gauntlet(t, errs):
    """End-of-run gauntlet (specs/19 section 4). Mirrors ContentDb.BuildGauntlet."""
    elements = {r["id"] for r in t["elements"]}
    en = t["gauntlet_enemies"]
    if not any(not r["boss"] for r in en) or not any(r["boss"] for r in en):
        errs.add("E044", "gauntlet_enemies", "need at least one normal enemy and one boss")
    for r in en:
        loc = f"gauntlet_enemies[{r['id']}]"
        if r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        if any(r[s] < 0 for s in STATS):
            errs.add("E043", loc, "stat weights must be >= 0")
    ti = [r["min_floor"] for r in t["legacy_titles"]]
    if not ti or ti[0] != 1:
        errs.add("E045", "legacy_titles", "first row must have min_floor = 1")
    for i, v in enumerate(ti):
        if i > 0 and v <= ti[i - 1]:
            errs.add("E032", f"legacy_titles[{v}]", "min_floor must strictly increase")


def split_ids(text):
    return [x for x in (text or "").split("|") if x]


GAMBIT_WHEN = ["always", "self_hp_below", "ally_hp_below", "enemy_hp_below", "enemies_at_least", "first_actions"]
GAMBIT_TARGETS = ["auto", "lowest_hp", "highest_hp"]


def parse_gambits(text):
    """A rulebook in content (specs/28 section 7): rules split by |, each when[:value]>action[@target].
    Returns (rules, error); each rule is (when, value, action, target). Mirrors GambitText.Parse."""
    rules = []
    for part in split_ids(text):
        if part.count(">") != 1:
            return None, f"rule '{part}' needs exactly one '>'"
        cond, act = part.split(">")
        when, _, val = cond.partition(":")
        action, _, target = act.partition("@")
        target = target or "auto"
        if when not in GAMBIT_WHEN:
            return None, f"rule '{part}': unknown condition '{when}'"
        if target not in GAMBIT_TARGETS:
            return None, f"rule '{part}': unknown target '{target}'"
        if not action:
            return None, f"rule '{part}' names no action"
        value = 0.0
        if when != "always":
            try:
                value = float(val)
            except ValueError:
                return None, f"rule '{part}': '{when}' needs a number"
            if not math.isfinite(value):
                return None, f"rule '{part}': '{when}' needs a number"
            if when in ("self_hp_below", "ally_hp_below", "enemy_hp_below") and not 0 < value <= 1:
                return None, f"rule '{part}': an HP fraction must be in (0, 1]"
            if when in ("enemies_at_least", "first_actions") and not (value >= 1 and value == int(value)):
                return None, f"rule '{part}': a count must be a whole number >= 1"
        elif val:
            return None, f"rule '{part}': 'always' takes no value"
        rules.append((when, value, action, target))
    return rules, None


def validate_endless(t, keys, errs):
    """Endless Tower (specs/28 section 7). Mirrors ContentDb.BuildEndless."""
    def k(name, default=0):
        return keys.get(name, {}).get("value", default)
    side = k("MON_SIDE_SIZE", 4)
    if not (k("ENDLESS_BASE") > 0 and k("ENDLESS_GROWTH") >= 1 and k("ENDLESS_BAND") >= 3 and k("ENDLESS_ELITE_HP_MULT") > 0
            and k("ENDLESS_MAX_FLOORS") > 0):
        errs.add("E043", "constants", "Endless Tower constants out of range (specs/28 section 7)")
    elements = {r["id"] for r in t["elements"]}
    skills = {r["id"]: r for r in t["monster_skills"]}
    roles = set()
    for r in t["endless_enemies"]:
        loc = f"endless_enemies[{r['id']}]"
        roles.add(r["role"])
        if r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        if any(r[s] < 0 for s in STATS):
            errs.add("E043", loc, "stat weights must be >= 0")
        own = split_ids(r["skills"])
        for sk in own:
            if sk not in skills:
                errs.add("E023", loc, f"skill '{sk}' is not a monster_skills id")
        rules, err = parse_gambits(r["gambits"])
        if err:
            errs.add("E043", loc, err)
            continue
        active = {sk for sk in own if sk in skills and skills[sk]["kind"] != "passive"}
        for (_, _, action, _) in rules:
            if action != "basic" and action not in active:
                errs.add("E023", loc, f"gambit action '{action}' is not one of the enemy's active skills")
    checks = set()
    for r in t["endless_shapes"]:
        loc = f"endless_shapes[{r['id']}]"
        checks.add(r["check"])
        slots = split_ids(r["slots"])
        if len(slots) != side:
            errs.add("E043", loc, f"slots must list exactly MON_SIDE_SIZE ({side}) roles")
        for role in slots:
            if role not in ENUMS["EndlessRole"]:
                errs.add("E021", loc, f"slot role '{role}' is not an endless role")
            elif role not in roles:
                errs.add("E044", loc, f"no endless_enemies row has role '{role}'")
        if not r["anchor_hp"] > 0:
            errs.add("E043", loc, "anchor_hp must be > 0")
    if "aoe" not in checks or "focus" not in checks:
        errs.add("E044", "endless_shapes", "need at least one aoe and one focus shape")
    if not t["mutators"]:
        errs.add("E044", "mutators", "at least one row is required")
    for r in t["mutators"]:
        loc = f"mutators[{r['id']}]"
        kind, v = r["kind"], r["value"]
        if (r["element"] is not None) != (kind == "element_mult"):
            errs.add("E043", loc, "element_mult, and only it, names an element")
        elif r["element"] is not None and r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        ok = {"speed_mult": v > 0, "crit_bonus": -1 <= v <= 1, "corruption_early": v >= 0 and v == int(v),
              "start_shield": 0 <= v <= 1}.get(kind, v >= 0)
        if not ok:
            errs.add("E043", loc, f"value out of range for kind '{kind}' (specs/28 section 3.2)")


def validate_tower(t, keys, errs):
    """Dungeon (specs/21 section 5). Mirrors ContentDb.BuildTower."""
    elements = {r["id"] for r in t["elements"]}
    skills = {r["id"] for r in t["monster_skills"]}
    bosses = {r["id"]: r for r in t["tower_bosses"]}
    roles = [r["role"] for r in t["tower_bosses"]]
    if "regular" not in roles or "marquee" not in roles:
        errs.add("E044", "tower_bosses", "need at least one regular and one marquee boss")
    for r in t["tower_bosses"]:
        loc = f"tower_bosses[{r['id']}]"
        if r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        if any(r[s] < 0 for s in STATS):
            errs.add("E043", loc, "stat weights must be >= 0")
        for sk in split_ids(r["skills"]):
            if sk not in skills:
                errs.add("E023", loc, f"skill '{sk}' is not a monster_skills id")
    by_boss = {}
    for p in t["boss_phases"]:
        loc = f"boss_phases[{p['id']}]"
        by_boss.setdefault(p["boss_id"], []).append(p)
        if p["boss_id"] not in bosses:
            errs.add("E023", loc, f"boss_id '{p['boss_id']}' is not a tower_bosses id")
        elif bosses[p["boss_id"]]["role"] == "add":
            errs.add("E044", loc, "an add cannot have phases")
        for sk in split_ids(p["add_skills"]):
            if sk not in skills:
                errs.add("E023", loc, f"add_skills '{sk}' is not a monster_skills id")
        k = p["kind"]
        ok = (p["power"] > 0 and p["duration"] > 0 and p["heal_frac"] >= 0 and p["buff"] >= 0) if k == "shield_check" else \
             (p["count"] >= 1 and 0 <= p["guard"] <= 1 and p["adds"] is not None) if k == "split" else \
             (p["power"] > 0 and p["duration"] > 0 and p["strike"] >= 0) if k == "channel" else p["buff"] >= 0
        if not ok:
            errs.add("E043", loc, f"values out of range for kind '{k}' (specs/21 section 3.2)")
        if p["adds"] is not None and (p["adds"] not in bosses or bosses[p["adds"]]["role"] != "add"):
            errs.add("E023", loc, f"adds '{p['adds']}' is not a tower_bosses id with role add")
    for boss, ps in by_boss.items():
        ps = sorted(ps, key=lambda x: x["order"])
        prev = 1.0
        for p in ps:
            if not 0 < p["enter_hp"] < prev:
                errs.add("E032", f"boss_phases[{p['id']}]", "enter_hp must be in (0, 1) and strictly decrease with order")
            prev = p["enter_hp"]
    books = {r["id"] for r in t["skill_books"]}
    floors = keys.get("TOWER_FLOORS", {}).get("value", 1)
    if not t["book_drops"]:
        errs.add("E044", "book_drops", "at least one row is required")
    for r in t["book_drops"]:
        loc = f"book_drops[{r['book_id']}]"
        if r["book_id"] not in books:
            errs.add("E023", loc, f"book_id '{r['book_id']}' is not a skill_books id")
        if not 1 <= r["min_floor"] <= floors:
            errs.add("E043", loc, "min_floor must be in [1, TOWER_FLOORS]")
        if not r["weight"] > 0:
            errs.add("E043", loc, "weight must be > 0")
    if t["book_drops"] and not any(r["min_floor"] == 1 for r in t["book_drops"]):
        errs.add("E044", "book_drops", "at least one row needs min_floor 1")


WHOLE_TALENT_EFFECTS = ("start_sp", "momentum", "loss_sp", "tree_slots", "injury_shield", "last_cycle_ap",
                        "draft_rerolls")
LEGENDARY_TALENT_EFFECTS = ("iron_constitution", "gamblers_blood", "glass_cannon", "echo_training")


def validate_talents(t, errs):
    """Account talent tree (specs/18 section 4). Mirrors ContentDb.BuildAccount."""
    for r in t["talents"]:
        loc = f"talents[{r['id']}]"
        if r["max_rank"] < 1 or r["cost_base"] < 0 or r["cost_step"] < 0:
            errs.add("E043", loc, "need max_rank >= 1, cost_base >= 0, cost_step >= 0")
        p, eff = r["per_rank"], r["effect"]
        if eff == "drill_cost" and not (p <= 0 and 1 + p * r["max_rank"] >= 0):
            errs.add("E043", loc, "drill_cost per_rank must be <= 0 and keep the multiplier >= 0 at max rank")
        if eff in ("stat_floor", "rest_energy", "shard_mult") and not p >= 0:
            errs.add("E043", loc, f"{eff} per_rank must be >= 0")
        if eff == "fail_chance" and not p <= 0:
            errs.add("E043", loc, "fail_chance per_rank must be <= 0")
        if eff == "super_chance" and not (p >= 0 and p * r["max_rank"] <= 1):
            errs.add("E043", loc, "super_chance per_rank must be >= 0 and at most 1 at max rank")
        if eff == "specialist" and not (p >= 0 and p * r["max_rank"] <= 2):
            errs.add("E043", loc, "specialist per_rank must be >= 0 and at most 2 at max rank")
        if eff == "milestone_mult" and not (p <= 0 and 1 + p * r["max_rank"] > 0):
            errs.add("E043", loc, "milestone_mult per_rank must be <= 0 and keep the multiplier > 0 at max rank")
        if eff in WHOLE_TALENT_EFFECTS and not (p >= 0 and p == int(p)):
            errs.add("E043", loc, f"{eff} per_rank must be a whole number >= 0")
        # Legendary rule changes (issue #103): one rank each; per_rank is the rule's one number.
        if eff in LEGENDARY_TALENT_EFFECTS and r["max_rank"] != 1:
            errs.add("E043", loc, f"{eff} is a rule change: max_rank must be 1")
        if eff in ("iron_constitution", "echo_training") and not 0 <= p <= 1:
            errs.add("E043", loc, f"{eff} per_rank must be in [0, 1]")
        if eff in ("gamblers_blood", "glass_cannon") and not p >= 0:
            errs.add("E043", loc, f"{eff} per_rank must be >= 0")


def validate_unlocks(t, errs):
    """Achievements and the post-run vendor (specs/18 sections 4 and 8). Mirrors ContentDb.BuildUnlocks."""
    species = [r["id"] for r in t["species"]]
    ranks = {r["id"] for r in t["fight_ranks"]}
    unlocked = set()
    for r in t["achievements"]:
        loc = f"achievements[{r['id']}]"
        kind, ref, amount = r["kind"], r["ref"], r["amount"]
        if kind == "rank_reached":
            if ref not in ranks:
                errs.add("E023", loc, f"ref '{ref}' is not a fight rank id")
            if amount is not None:
                errs.add("E043", loc, "rank_reached takes no amount")
        else:
            if kind == "stat_reached":
                if ref is not None and ref not in STATS:
                    errs.add("E023", loc, f"ref '{ref}' is not a trained stat (or empty for any stat)")
            elif ref is not None:
                errs.add("E043", loc, f"{kind} takes no ref")
            if amount is None or not amount > 0 or (kind != "stat_reached" and amount != int(amount)):
                errs.add("E043", loc, f"{kind} needs an amount > 0" + ("" if kind == "stat_reached" else " (a whole number)"))
        sp = r["unlocks_species"]
        if sp is not None:
            if sp not in species:
                errs.add("E023", loc, f"unlocks_species '{sp}' is not a species id")
            elif species and sp == species[0]:
                errs.add("E043", loc, f"'{sp}' is the starter species (the first in species.csv): every account owns it")
            unlocked.add(sp)
    for sp in species[1:]:
        if sp not in unlocked:
            errs.add("E044", f"species[{sp}]", "only the first species is owned from the start: every other species needs an achievement that unlocks it")
    rar = t["vendor_rarities"]
    if not rar:
        errs.add("E044", "vendor_rarities", "at least one rarity is required")
    if sorted(r["sort_order"] for r in rar) != list(range(len(rar))):
        errs.add("E047", "vendor_rarities", "sort_order must be exactly 0..N-1")
    tiers = {r["id"] for r in rar}
    for r in t["talents"]:
        if r["rarity"] not in tiers:
            errs.add("E023", f"talents[{r['id']}]", f"rarity '{r['rarity']}' is not a vendor_rarities id")
    for g in range(len(t["fight_ranks"])):
        if rar and not sum(max(0.0, r["weight_base"] + r["weight_per_rank"] * g) for r in rar) > 0:
            errs.add("E043", "vendor_rarities", f"the weights must not all be 0 at grade index {g}")


def validate_rank_enemies(t, stat_max, errs):
    """Rank fight pools (specs/14 section 5). Mirrors the rank_enemies part of ContentDb.Build."""
    elements = {r["id"] for r in t["elements"]}
    skills = {r["id"] for r in t["monster_skills"]}
    budget = {r["id"]: sum(r[s] for s in STATS) for r in t["fight_ranks"]}
    pooled = set()
    for r in t["rank_enemies"]:
        loc = f"rank_enemies[{r['id']}]"
        if r["rank"] not in budget:
            errs.add("E023", loc, f"rank '{r['rank']}' is not a fight_ranks id")
        else:
            pooled.add(r["rank"])
            total = sum(r[s] for s in STATS)
            if abs(total - budget[r["rank"]]) > 1e-6:
                errs.add("E043", loc, f"stats sum to {total:g}, but the budget of rank '{r['rank']}' is {budget[r['rank']]:g}")
        if r["element"] not in elements:
            errs.add("E023", loc, f"element '{r['element']}' is not an element id")
        if any(not 0 <= r[s] <= stat_max for s in STATS):
            errs.add("E043", loc, "stats must be in [0, STAT_MAX]")
        for sk in split_ids(r["skills"]):
            if sk not in skills:
                errs.add("E023", loc, f"skill '{sk}' is not a monster_skills id")
        if not r["weight"] > 0:
            errs.add("E043", loc, "weight must be > 0")
    for r in t["fight_ranks"]:
        if r["id"] not in pooled:
            errs.add("E044", f"fight_ranks[{r['id']}]", "the rank has no rank_enemies row (empty pool)")


def validate_training(t, keys, errs):
    """Training run tables and constants (specs/14 section 8)."""
    kv = {k: keys[k]["value"] for k in keys}
    elements = {r["id"] for r in t["elements"]}
    stat_max = kv.get("STAT_MAX", 999)
    if "RUN_ENERGY_START" in kv and "RUN_ENERGY_MAX" in kv and kv["RUN_ENERGY_START"] > kv["RUN_ENERGY_MAX"]:
        errs.add("E043", "constants[RUN_ENERGY_START]", "value out of range (<= RUN_ENERGY_MAX)")
    if "INJURY_RECOVER_ENERGY" in kv and "RUN_ENERGY_MAX" in kv and kv["INJURY_RECOVER_ENERGY"] > kv["RUN_ENERGY_MAX"]:
        errs.add("E043", "constants[INJURY_RECOVER_ENERGY]", "value out of range (<= RUN_ENERGY_MAX)")
    if kv.get("DRILL_FAIL_MIN", 0) > kv.get("DRILL_FAIL_MAX", 1):
        errs.add("E043", "constants[DRILL_FAIL_MIN]", "value out of range (<= DRILL_FAIL_MAX)")
    if kv.get("DRILL_FAIL_MAX", 0) + kv.get("DRILL_SUPER_P", 0) > 1:
        errs.add("E043", "constants[DRILL_SUPER_P]", "value out of range (DRILL_FAIL_MAX + DRILL_SUPER_P <= 1)")
    for part in ("GROWTH_OWN", "GROWTH_OFF", "FLAT_OWN", "FLAT_OFF", "CAP_GROWTH"):   # rolled Awakening trees (specs/15 section 4.1)
        if kv.get(f"ROLL_{part}_MIN", 0) > kv.get(f"ROLL_{part}_MAX", 0):
            errs.add("E043", f"constants[ROLL_{part}_MIN]", f"value out of range (<= ROLL_{part}_MAX)")
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
        if r["dismantle_shards"] < 0:
            errs.add("E043", loc, "dismantle_shards must be >= 0")
        if r["ticket_pieces"] < 0:
            errs.add("E043", loc, "ticket_pieces must be >= 0")
    validate_rank_enemies(t, stat_max, errs)
    validate_trees(t, errs)
    validate_talents(t, errs)
    validate_unlocks(t, errs)
    validate_gauntlet(t, errs)
    validate_tower(t, keys, errs)
    validate_endless(t, keys, errs)
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
    for k in ("BATTLE_GAUGE", "ELEMENT_STRONG_MULT", "ELEMENT_WEAK_MULT", "BASIC_ATTACK_POWER"):
        if k in keys and not keys[k]["value"] > 0:
            errs.add("E043", f"constants[{k}]", "value out of range (> 0)")
    for k, (rule, ok) in MON_RANGES.items():
        if k in keys and not ok(keys[k]["value"]):
            errs.add("E043", f"constants[{k}]", f"value out of range ({rule})")
    if "LIMIT_BREAK_MAX" in keys and "STAT_MAX" in keys and not keys["LIMIT_BREAK_MAX"]["value"] >= keys["STAT_MAX"]["value"]:
        errs.add("E043", "constants[LIMIT_BREAK_MAX]", "value out of range (>= STAT_MAX)")
    validate_training(t, keys, errs)
    # elements
    orders = sorted(r["sort_order"] for r in t["elements"])
    if orders != list(range(len(orders))):
        errs.add("E047", "elements", "sort_order must be exactly 0..N-1")
    element_ids = {r["id"] for r in t["elements"]}
    for e in t["elements"]:
        if e["strong_vs"] not in element_ids:
            errs.add("E023", f"elements[{e['id']}]", f"strong_vs '{e['strong_vs']}' is not an element id")


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


def parse_version(text):
    """'MAJOR.MINOR' -> (major, minor), or None when malformed."""
    m = re.match(r"^(\d+)\.(\d+)$", text or "")
    return (int(m.group(1)), int(m.group(2))) if m else None


def read_baseline_dir(path):
    """(content_version, {table: set of primary keys}) of an earlier Content/ directory."""
    with open(os.path.join(path, "manifest.json"), encoding="utf-8") as f:
        manifest = json.load(f)
    keys = {}
    for name, entry in manifest["tables"].items():
        with open(os.path.join(path, entry["file"]), encoding="utf-8") as f:
            rows = json.load(f)["rows"]
        keys[name] = {str(r[SCHEMA[name]["key"]]) for r in rows} if name in SCHEMA else set()
    return manifest["content_version"], keys


def read_baseline_ref(ref):
    """The same, read from the Content/ directory of a git ref (no checkout needed)."""
    def show(rel):
        out = subprocess.run(["git", "show", f"{ref}:Content/{rel}"], capture_output=True)
        if out.returncode != 0:
            raise OSError(f"git show {ref}:Content/{rel} failed: {out.stderr.decode().strip()}")
        return json.loads(out.stdout.decode("utf-8"))
    manifest = show("manifest.json")
    keys = {}
    for name, entry in manifest["tables"].items():
        rows = show(entry["file"])["rows"]
        keys[name] = {str(r[SCHEMA[name]["key"]]) for r in rows} if name in SCHEMA else set()
    return manifest["content_version"], keys


def compare_to_baseline(base_version, base_keys, version, tables, errs):
    """E090 / E091 (specs/05 section 6): a removed or renamed id needs a MAJOR bump, the version never goes backwards.

    Saves refer to ids (species, trees, nodes, skills, books, talents, enemies), so a removed id breaks them."""
    old, new = parse_version(base_version), parse_version(version)
    if new is None or old is None:
        errs.add("E091", "meta", f"content_version must be MAJOR.MINOR (now {version!r}, before {base_version!r})")
        return
    if new < old:
        errs.add("E091", "meta", f"content_version went backwards: {version} after {base_version}")
    if new[0] > old[0]:
        return
    for name in sorted(base_keys):
        if name == "strings":
            continue  # a string key is looked up, never saved; a missing one shows as the key (ContentDb.Text)
        if name not in tables:
            errs.add("E090", name, f"table removed since {base_version} without a MAJOR bump (content_version {version})")
            continue
        key = SCHEMA[name]["key"]
        now = {str(r[key]) for r in tables[name]}
        gone = sorted(base_keys[name] - now)
        if gone:
            errs.add("E090", name, f"removed or renamed since {base_version} without a MAJOR bump (content_version {version}): "
                                    + ", ".join(gone))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--src", default="content_src")
    ap.add_argument("--out", default="Content")
    ap.add_argument("--check", action="store_true", help="exit 1 if --out differs from a fresh build")
    ap.add_argument("--copy-to", default=None)
    base = ap.add_mutually_exclusive_group()
    base.add_argument("--baseline", default=None, help="an earlier Content/ directory to compare the build with (E090, E091)")
    base.add_argument("--baseline-ref", default=None, help="a git ref whose Content/ to compare the build with (E090, E091)")
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

    if args.baseline or args.baseline_ref:
        try:
            base_version, base_keys = (read_baseline_dir(args.baseline) if args.baseline
                                       else read_baseline_ref(args.baseline_ref))
        except (OSError, KeyError, ValueError) as e:
            print(f"cannot read the baseline: {e}", file=sys.stderr)
            return 2
        errs = Errors()
        compare_to_baseline(base_version, base_keys, summary["manifest"]["content_version"], summary["tables"], errs)
        if errs:
            for e in errs:
                print(e, file=sys.stderr)
            return 1
        print(f"Content ids are compatible with the baseline (content_version {base_version} -> "
              f"{summary['manifest']['content_version']})")

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
    return 0


if __name__ == "__main__":
    sys.exit(main())
