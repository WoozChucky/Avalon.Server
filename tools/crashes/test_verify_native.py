import unittest
from verify_native import GateError, wrong_source_gate


class WrongSymbolsShould(unittest.TestCase):
    def test_reject_unrelated_fault_failure(self):
        with self.assertRaisesRegex(GateError, "wrong fault reason"):
            wrong_source_gate({"crash_info": {"type": "wrong"}}, {"reason": "expected"})

    def test_require_actual_source_resolution_failure(self):
        wrong_source_gate({"crash_info": {"type": "expected", "crashing_thread": 0},
                           "threads": [{"thread_id": 7, "frames": []}]},
                          {"reason": "expected", "threadIndex": 0, "threadId": 7})
