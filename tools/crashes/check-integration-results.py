"""Fail closed on zero, skipped, failed or attached integration results; emit only counts."""
import sys
import re
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
counters = root.find("t:ResultSummary/t:Counters", ns)
results = root.findall("t:Results/t:UnitTestResult", ns)
if counters is None:
    raise SystemExit("Integration results missing counters")
total, executed, passed = (int(counters.get(k, "0")) for k in ("total", "executed", "passed"))
print(f"{sys.argv[2]}: selected={total} executed={executed} passed={passed}")
for result in results:
    if result.get("outcome") != "Passed":
        print("Failed test:", result.get("testName"))
if not (total > 0 and total == executed == passed == len(results)):
    raise SystemExit("Integration suite must execute and pass every selected test (no skips)")
if any(r.get("outcome") != "Passed" for r in results):
    raise SystemExit("Integration suite contains a non-passing result")
if root.findall(".//t:ResultFile", ns) or root.findall(".//t:CollectorDataEntries", ns):
    raise SystemExit("Integration result attachments are forbidden")
# Emit only this finite evidence shape, never arbitrary test output or private diagnostics.
for output in root.findall(".//t:StdOut", ns):
    for match in re.finditer(r"Collector accepted (\d+) report spans, (\d+) finite report events, (\d+) worker metric names; four report IDs covered; private-value scan passed\.", output.text or ""):
        print(f"collector: spans={match[1]} events={match[2]} metric_names={match[3]} covered_reports=4 privacy=passed")
