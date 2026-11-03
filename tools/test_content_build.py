#!/usr/bin/env python3
"""Tests for tools/content_build.py: reading tables (E004) and the previous-build rules (E090, E091). Stdlib only.

    python3 tools/test_content_build.py
"""
import os
import re
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import content_build as cb  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(ROOT, "content_src", "meta.csv"), encoding="utf-8") as _f:
    CURRENT_MAJOR = int(re.search(r"content_version,(\d+)\.", _f.read()).group(1))


class ReadTable(unittest.TestCase):
    """read_table: an unquoted comma must not cut a cell short without a word (E004)."""
    SCHEMA = cb.SCHEMA["strings"]

    def read(self, text):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "strings.csv")
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
            errs = cb.Errors()
            rows = cb.read_table(path, "strings", self.SCHEMA, errs)
            return rows, errs

    def test_quoted_comma_is_kept(self):
        rows, errs = self.read('key,en\na.b,"one, two"\n')
        self.assertEqual([], errs)
        self.assertEqual("one, two", rows[0]["en"])

    def test_unquoted_comma_is_e004(self):
        _, errs = self.read("key,en\na.b,one, two\n")
        self.assertTrue(any("E004" in e for e in errs), errs)

    def test_extra_cells_after_a_notes_column_are_allowed(self):
        _, errs = self.read("key,en,#notes\na.b,one,a note, with a comma\n")
        self.assertEqual([], errs)


class BaselineRules(unittest.TestCase):
    def errors(self, base_version, base_keys, version, tables):
        errs = cb.Errors()
        cb.compare_to_baseline(base_version, base_keys, version, tables, errs)
        return errs

    def test_same_ids_pass(self):
        self.assertEqual([], self.errors("3.1", {"species": {"a", "b"}}, "3.1", {"species": [{"id": "a"}, {"id": "b"}]}))

    def test_added_id_passes_without_a_bump(self):
        self.assertEqual([], self.errors("3.1", {"species": {"a"}}, "3.1", {"species": [{"id": "a"}, {"id": "b"}]}))

    def test_removed_id_needs_a_major_bump(self):
        errs = self.errors("3.1", {"species": {"a", "b"}}, "3.2", {"species": [{"id": "a"}]})
        self.assertEqual(1, len(errs))
        self.assertIn("E090 species", errs[0])
        self.assertIn("b", errs[0])

    def test_renamed_id_counts_as_removed(self):
        errs = self.errors("3.1", {"species": {"a"}}, "3.1", {"species": [{"id": "a2"}]})
        self.assertIn("E090", errs[0])

    def test_removed_id_passes_with_a_major_bump(self):
        self.assertEqual([], self.errors("3.1", {"species": {"a", "b"}}, "4.0", {"species": [{"id": "a"}]}))

    def test_removed_table_needs_a_major_bump(self):
        self.assertIn("E090 talents", self.errors("3.1", {"talents": {"x"}}, "3.1", {})[0])

    def test_removed_string_key_is_allowed(self):
        self.assertEqual([], self.errors("3.1", {"strings": {"a.b", "c.d"}}, "3.1", {"strings": [{"key": "a.b"}]}))

    def test_version_going_backwards_is_e091(self):
        self.assertIn("E091", self.errors("3.2", {}, "3.1", {})[0])
        self.assertIn("E091", self.errors("3.1", {}, "2.9", {})[0])

    def test_malformed_version_is_e091(self):
        self.assertIn("E091", self.errors("3.1", {}, "three", {})[0])

    def test_numeric_keys_compare_as_text(self):
        self.assertEqual([], self.errors("3.1", {"stat_milestones": {"100"}}, "3.1", {"stat_milestones": [{"threshold": 100}]}))


class EndToEnd(unittest.TestCase):
    """The real CLI against the shipped content, with one CSV row removed."""

    def build(self, mutate, extra):
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, "content_src")
            shutil.copytree(os.path.join(ROOT, "content_src"), src)
            mutate(src)
            out = os.path.join(tmp, "Content")
            return cb.main(["--src", src, "--out", out] + extra)

    def drop_last_row(self, name):
        def go(src):
            path = os.path.join(src, name + ".csv")
            with open(path, encoding="utf-8") as f:
                lines = f.read().rstrip("\n").split("\n")
            with open(path, "w", encoding="utf-8") as f:
                f.write("\n".join(lines[:-1]) + "\n")
        return go

    def set_version(self, version):
        def go(src):
            path = os.path.join(src, "meta.csv")
            with open(path, encoding="utf-8") as f:
                text = re.sub(r"content_version,\S+", "content_version," + version, f.read())
            with open(path, "w", encoding="utf-8") as f:
                f.write(text)
        return go

    def test_unchanged_content_passes(self):
        self.assertEqual(0, self.build(lambda src: None, ["--baseline", os.path.join(ROOT, "Content")]))

    def test_dropping_a_talent_fails(self):
        self.assertEqual(1, self.build(self.drop_last_row("talents"), ["--baseline", os.path.join(ROOT, "Content")]))

    def test_dropping_a_talent_with_a_major_bump_passes(self):
        def go(src):
            self.drop_last_row("talents")(src)
            self.set_version(f"{CURRENT_MAJOR + 1}.0")(src)
        self.assertEqual(0, self.build(go, ["--baseline", os.path.join(ROOT, "Content")]))

    def test_lowering_the_version_fails(self):
        self.assertEqual(1, self.build(self.set_version("0.0"), ["--baseline", os.path.join(ROOT, "Content")]))

    def test_missing_baseline_is_a_usage_error(self):
        self.assertEqual(2, self.build(lambda src: None, ["--baseline", os.path.join(ROOT, "nope")]))



class TalentRules(unittest.TestCase):
    """validate_talents (specs/18 section 4): per_rank ranges of the talent effects. Mirrors ContentDb.BuildAccount."""

    def errors(self, effect, per_rank, max_rank=2):
        errs = cb.Errors()
        cb.validate_talents({"constants": [], "talents": [{"id": "x", "effect": effect, "per_rank": per_rank, "max_rank": max_rank,
                                          "cost_base": 1, "cost_step": 1}]}, errs)
        return errs

    def test_in_range_passes(self):
        for eff, p in [("rest_energy", 10), ("fail_chance", -0.02), ("super_chance", 0.03), ("shard_mult", 0.1),
                       ("momentum", 1), ("loss_sp", 1), ("specialist", 0.15), ("tree_slots", 1), ("injury_shield", 1),
                       ("last_cycle_ap", 1), ("milestone_mult", -0.1), ("draft_rerolls", 1)]:
            self.assertEqual([], self.errors(eff, p), eff)

    def test_out_of_range_fails(self):
        for eff, p in [("rest_energy", -1), ("fail_chance", 0.02), ("super_chance", 0.6), ("shard_mult", -0.1),
                       ("momentum", 0.5), ("loss_sp", -1), ("specialist", 1.5), ("tree_slots", 1.5), ("injury_shield", -1),
                       ("last_cycle_ap", 0.5), ("milestone_mult", -0.5), ("draft_rerolls", 2.5)]:
            errs = self.errors(eff, p)
            self.assertEqual(1, len(errs), eff)
            self.assertIn("E043 talents[x]", errs[0])


class RankEnemies(unittest.TestCase):
    """validate_rank_enemies (specs/14 section 5): refs, the rank budget and empty pools."""

    STATS0 = {s: 10.0 for s in cb.STATS}

    def errors(self, enemies, ranks=("e", "d")):
        t = {
            "elements": [{"id": "fire"}],
            "monster_skills": [{"id": "smash"}],
            "fight_ranks": [dict(self.STATS0, id=r) for r in ranks],
            "rank_enemies": enemies,
        }
        errs = cb.Errors()
        cb.validate_rank_enemies(t, 999, errs)
        return errs

    def enemy(self, id_, rank, **kw):
        row = dict(self.STATS0, id=id_, rank=rank, element="fire", skills="smash", weight=1.0)
        row.update(kw)
        return row

    def test_valid_pools_pass(self):
        self.assertEqual([], self.errors([self.enemy("a", "e"), self.enemy("b", "d", hp=20.0, str=0.0, skills=None)]))

    def test_budget_mismatch_is_e043(self):
        errs = self.errors([self.enemy("a", "e", hp=11.0), self.enemy("b", "d")])
        self.assertEqual(1, len(errs))
        self.assertIn("E043 rank_enemies[a]", errs[0])
        self.assertIn("budget", errs[0])

    def test_bad_refs_are_e023(self):
        errs = self.errors([self.enemy("a", "zz"), self.enemy("b", "d", element="mud", skills="smash|nope"), self.enemy("c", "e")])
        self.assertEqual(3, len(errs))
        self.assertTrue(all("E023" in e for e in errs))

    def test_empty_pool_is_e044(self):
        errs = self.errors([self.enemy("a", "e")])
        self.assertEqual(["CONTENT E044 fight_ranks[d]: the rank has no rank_enemies row (empty pool)"], errs)

    def test_weight_must_be_positive(self):
        self.assertIn("E043 rank_enemies[a]", self.errors([self.enemy("a", "e", weight=0.0), self.enemy("b", "d")])[0])


class Endless(unittest.TestCase):
    """validate_endless and parse_gambits (specs/28 section 7). Mirrors EndlessTests in the C# tests."""

    STATS1 = {s: 1.0 for s in cb.STATS}
    KEYS = {k: {"value": v} for k, v in {"MON_SIDE_SIZE": 4, "ENDLESS_BASE": 60.0, "ENDLESS_GROWTH": 1.05, "ENDLESS_BAND": 10,
                                         "ENDLESS_ELITE_HP_MULT": 2.0, "ENDLESS_MAX_FLOORS": 500}.items()}

    def tables(self):
        return {
            "elements": [{"id": "fire"}],
            "monster_skills": [{"id": "jab", "kind": "damage"}, {"id": "calm", "kind": "passive"}],
            "endless_enemies": [dict(self.STATS1, id="imp", element="fire", role="minion", skills="jab|calm",
                                     gambits="enemy_hp_below:0.4>jab@lowest_hp|always>basic"),
                                dict(self.STATS1, id="golem", element="fire", role="tank", skills="jab", gambits=None)],
            "endless_shapes": [{"id": "swarm", "check": "aoe", "slots": "minion|minion|minion|minion", "anchor_hp": 1.0},
                               {"id": "wall", "check": "focus", "slots": "tank|minion|minion|minion", "anchor_hp": 3.0}],
            "mutators": [{"id": "haste", "kind": "speed_mult", "value": 1.3, "element": None},
                         {"id": "blaze", "kind": "element_mult", "value": 1.5, "element": "fire"}],
        }

    def errors(self, t, keys=None):
        errs = cb.Errors()
        cb.validate_endless(t, keys or self.KEYS, errs)
        return errs

    def test_valid_passes(self):
        self.assertEqual([], self.errors(self.tables()))

    def test_gambits_parse(self):
        rules, err = cb.parse_gambits("enemy_hp_below:0.4>jab@lowest_hp|always>basic")
        self.assertIsNone(err)
        self.assertEqual([("enemy_hp_below", 0.4, "jab", "lowest_hp"), ("always", 0.0, "basic", "auto")], rules)
        for bad in ["always", "always>a>b", "never>x", "always>x@random", "always>", "self_hp_below>x", "self_hp_below:1.5>x",
                    "first_actions:0>x", "enemies_at_least:2.5>x", "always:3>x", "ally_hp_below:nan>x", "first_actions:inf>x"]:
            self.assertIsNotNone(cb.parse_gambits(bad)[1], bad)

    def test_bad_gambit_action_is_e023(self):
        t = self.tables()
        t["endless_enemies"][0]["gambits"] = "always>calm"
        errs = self.errors(t)
        self.assertEqual(1, len(errs))
        self.assertIn("E023 endless_enemies[imp]", errs[0])

    def test_shapes_need_full_slots_and_both_checks(self):
        t = self.tables()
        t["endless_shapes"][1]["slots"] = "tank|minion|minion"
        t["endless_shapes"][0]["check"] = "open"
        errs = self.errors(t)
        self.assertTrue(any("E043 endless_shapes[wall]" in e for e in errs))
        self.assertTrue(any("E044 endless_shapes" in e and "aoe" in e for e in errs))

    def test_mutator_values_and_elements(self):
        t = self.tables()
        t["mutators"][0]["value"] = 0.0
        t["mutators"][1]["element"] = None
        errs = self.errors(t)
        self.assertEqual(2, len(errs))
        self.assertTrue(all("E043 mutators" in e for e in errs))

    def test_constants(self):
        keys = dict(self.KEYS, ENDLESS_BAND={"value": 2})
        self.assertIn("E043 constants", self.errors(self.tables(), keys)[0])


class LaneFRules(unittest.TestCase):
    """Idea-pool lane F (#139): the growth_use node kind and the run rule constants (specs/15 section 11)."""

    build = EndToEnd.build

    def replace(self, name, old, new):
        def go(src):
            path = os.path.join(src, name + ".csv")
            with open(path, encoding="utf-8") as f:
                text = f.read()
            self.assertIn(old, text)
            with open(path, "w", encoding="utf-8") as f:
                f.write(text.replace(old, new))
        return go

    def test_shipped_content_passes(self):
        self.assertEqual(0, self.build(lambda src: None, []))

    def test_limit_break_max_below_stat_max_is_e043(self):
        self.assertEqual(1, self.build(self.replace("constants", "LIMIT_BREAK_MAX,double,1299", "LIMIT_BREAK_MAX,double,500"), []))

    def test_keepsake_drills_below_one_is_e043(self):
        self.assertEqual(1, self.build(self.replace("constants", "KEEPSAKE_DRILLS,int,3", "KEEPSAKE_DRILLS,int,0"), []))

    def test_growth_use_needs_a_trained_stat(self):
        self.assertEqual(1, self.build(self.replace("tree_nodes", "heirloom_n0,heirloom,0,1,growth_use,hp,", "heirloom_n0,heirloom,0,1,growth_use,xyz,"), []))


if __name__ == "__main__":
    unittest.main()
