#!/usr/bin/env python3
"""Tests for the previous-build rules of tools/content_build.py (E090, E091). Stdlib only.

    python3 tools/test_content_build.py
"""
import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import content_build as cb  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


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
            lines = open(path, encoding="utf-8").read().rstrip("\n").split("\n")
            open(path, "w", encoding="utf-8").write("\n".join(lines[:-1]) + "\n")
        return go

    def set_version(self, version):
        def go(src):
            path = os.path.join(src, "meta.csv")
            text = open(path, encoding="utf-8").read().replace("content_version,3.1", "content_version," + version)
            open(path, "w", encoding="utf-8").write(text)
        return go

    def test_unchanged_content_passes(self):
        self.assertEqual(0, self.build(lambda src: None, ["--baseline", os.path.join(ROOT, "Content")]))

    def test_dropping_a_talent_fails(self):
        self.assertEqual(1, self.build(self.drop_last_row("talents"), ["--baseline", os.path.join(ROOT, "Content")]))

    def test_dropping_a_talent_with_a_major_bump_passes(self):
        def go(src):
            self.drop_last_row("talents")(src)
            self.set_version("4.0")(src)
        self.assertEqual(0, self.build(go, ["--baseline", os.path.join(ROOT, "Content")]))

    def test_lowering_the_version_fails(self):
        self.assertEqual(1, self.build(self.set_version("3.0"), ["--baseline", os.path.join(ROOT, "Content")]))

    def test_missing_baseline_is_a_usage_error(self):
        self.assertEqual(2, self.build(lambda src: None, ["--baseline", os.path.join(ROOT, "nope")]))


if __name__ == "__main__":
    unittest.main()
