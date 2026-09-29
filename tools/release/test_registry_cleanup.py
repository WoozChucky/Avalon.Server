import unittest
from datetime import datetime, timedelta, timezone
from unittest.mock import patch

import registry_cleanup
from registry_cleanup import select_deletions


NOW = datetime(2026, 11, 1, tzinfo=timezone.utc)


def version(id, days_old, *tags):
    return {"id": id, "created_at": NOW - timedelta(days=days_old), "tags": list(tags)}


class SelectDeletions(unittest.TestCase):
    def test_deletes_old_prereleases_beyond_the_newest_ten_of_each_kind(self):
        versions = [version(n, 30 + 16 - n, f"0.8.1-dev.{n}") for n in range(1, 16)]
        self.assertEqual(sorted(select_deletions(versions, NOW)), [1, 2, 3, 4, 5])

    def test_keeps_young_prereleases_and_every_release(self):
        versions = [version(n, 3, f"0.8.1-dev.{n}") for n in range(1, 16)]
        versions += [version(100, 90, "0.7.0", "0.7", "latest")]
        self.assertEqual(select_deletions(versions, NOW), [])

    def test_a_promoted_digest_is_kept_while_its_nightly_is_among_the_newest(self):
        versions = [version(n, 40 - n, f"0.8.1-dev.{n}") for n in range(20, 40)]
        versions += [version(5, 30, "0.8.1-dev.5", "0.8.1-nightly.5")]
        self.assertNotIn(5, select_deletions(versions, NOW))

    def test_untagged_versions_are_left_alone(self):
        self.assertEqual(select_deletions([version(1, 90)], NOW), [])

    def test_newest_uses_creation_time_across_release_versions(self):
        old = [version(n, 40, f"0.8.1-dev.{n}") for n in range(1, 11)]
        new = [version(n + 10, 20, f"0.9.1-dev.{n}") for n in range(1, 11)]
        self.assertEqual(sorted(select_deletions(old + new, NOW)), list(range(1, 11)))

    def test_mixed_release_tags_and_age_boundary_are_preserved(self):
        versions = [version(n, 1, f"0.9.1-dev.{n}") for n in range(1, 12)]
        versions += [version(100, 90, "0.8.1-dev.1", "latest"), version(101, 14, "0.8.1-dev.2")]
        self.assertEqual(select_deletions(versions, NOW), [])

    def test_paginated_api_arrays_are_read_without_losing_pages(self):
        raw = ('[{"id":1,"created_at":"2026-10-01T00:00:00Z","metadata":{"container":{"tags":["0.9.1-dev.1"]}}}]'
               '\n[{"id":2,"created_at":"2026-10-02T00:00:00Z","metadata":{"container":{"tags":[]}}}]')
        with patch.object(registry_cleanup, "_gh", return_value=raw):
            self.assertEqual([v["id"] for v in registry_cleanup._versions("unused")], [1, 2])


if __name__ == "__main__":
    unittest.main()
