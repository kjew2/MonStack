#!/usr/bin/env python3
"""Tests for the previous-build rules of tools/content_build.py (E090, E091). Stdlib only.

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


if __name__ == "__main__":
    unittest.main()
