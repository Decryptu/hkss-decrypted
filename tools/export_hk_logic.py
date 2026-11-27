#!/usr/bin/env python3
"""
Exports Hollow Knight's Archipelago logic graph (from apworld/hkss/hk) into a
compact JSON file embedded in HKSS.Core for live in-game reachability evaluation.

Source data is MIT-licensed (Archipelago worlds/hk).
"""
from __future__ import annotations

import ast
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HK_DIR = ROOT / "apworld" / "hkss" / "hk"
OUT_PATH = ROOT / "src" / "HKSS.Core" / "Data" / "hk_logic.json"


def convert_expr(node: ast.AST):
    if isinstance(node, ast.Constant) and isinstance(node.value, bool):
        return node.value

    if isinstance(node, ast.BoolOp):
        op = "&" if isinstance(node.op, ast.And) else "|"
        items = []
        for v in node.values:
            sub = convert_expr(v)
            if isinstance(sub, bool):
                if op == "&" and not sub:
                    return False
                if op == "&" and sub:
                    continue
                if op == "|" and sub:
                    return True
                if op == "|" and not sub:
                    continue
            if isinstance(sub, list) and sub and sub[0] == op:
                items.extend(sub[1:])
            else:
                items.append(sub)
        if not items:
            return op == "&"
        if len(items) == 1:
            return items[0]
        return [op, *items]

    if isinstance(node, ast.Compare):
        if len(node.ops) != 1 or len(node.comparators) != 1:
            raise ValueError(f"Unsupported chained compare: {ast.dump(node)}")
        left = node.left
        op = node.ops[0]
        right = node.comparators[0]

        # state.count('NOTCHES', player) > state._hkss_hk_notches(player, c1, c2, ...)
        if (
            isinstance(op, ast.Gt)
            and _is_state_call(left, "count")
            and _const_str(left.args[0]) == "NOTCHES"
            and _is_state_call(right, "_hkss_hk_notches")
        ):
            charms = [a.value for a in right.args[1:] if isinstance(a, ast.Constant) and isinstance(a.value, int)]
            return ["notches", charms]

        if not isinstance(right, ast.Constant) or not isinstance(right.value, int):
            raise ValueError(f"Unsupported compare RHS: {ast.dump(node)}")
        rhs = right.value

        # state.count('TERM', player) > N
        if isinstance(op, ast.Gt) and _is_state_call(left, "count"):
            term = _const_str(left.args[0])
            return ["gt", term, rhs]
        # state._hkss_hk_option(player, 'OPT') == 0
        if isinstance(op, ast.Eq) and rhs == 0 and _is_state_call(left, "_hkss_hk_option"):
            opt = _const_str(left.args[1])
            return ["opt0", opt]
        raise ValueError(f"Unsupported compare: {ast.dump(node)}")

    if isinstance(node, ast.Call):
        if _is_state_call(node, "count") or _is_state_call(node, "has"):
            term = _const_str(node.args[0])
            if len(node.args) >= 3 and isinstance(node.args[2], ast.Constant) and isinstance(node.args[2].value, int):
                count = node.args[2].value
                return term if count <= 1 else ["gt", term, count - 1]
            return term
        if _is_state_call(node, "_hkss_hk_option"):
            opt = _const_str(node.args[1])
            return ["opt", opt]
        raise ValueError(f"Unsupported call: {ast.dump(node)}")

    raise ValueError(f"Unsupported AST node: {ast.dump(node)}")


def _is_state_call(node: ast.AST, method_name: str) -> bool:
    return (
        isinstance(node, ast.Call)
        and isinstance(node.func, ast.Attribute)
        and isinstance(node.func.value, ast.Name)
        and node.func.value.id == "state"
        and node.func.attr == method_name
    )


def _const_str(node: ast.AST) -> str:
    if isinstance(node, ast.Constant) and isinstance(node.value, str):
        return node.value
    raise ValueError(f"Expected string constant, got {ast.dump(node)}")


def parse_generated_rules(path: Path) -> dict[str, object]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    rules: dict[str, object] = {}
    for node in ast.walk(tree):
        if (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id == "fn"
            and len(node.args) == 2
            and isinstance(node.args[0], ast.Constant)
            and isinstance(node.args[0].value, str)
            and isinstance(node.args[1], ast.Lambda)
        ):
            name = node.args[0].value
            rules[name] = convert_expr(node.args[1].body)
    return rules


def parse_godhome_rules(path: Path) -> dict[str, object]:
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    rules: dict[str, object] = {}
    for node in ast.walk(tree):
        if isinstance(node, ast.Dict):
            for k, v in zip(node.keys, node.values):
                if isinstance(k, ast.Constant) and isinstance(k.value, str) and isinstance(v, ast.Lambda):
                    rules[k.value] = convert_expr(v.body)
    return rules


def main() -> int:
    sys.path.insert(0, str(HK_DIR))
    import ExtractedData as extracted  # type: ignore

    gen_rules = parse_generated_rules(HK_DIR / "GeneratedRules.py")
    godhome_rules = parse_godhome_rules(HK_DIR / "GodhomeData.py")

    all_rules = dict(gen_rules)
    all_rules.update(godhome_rules)

    # Filter out null connectors
    connectors = {k: v for k, v in extracted.connectors.items() if v}

    # Distinguish graph nodes (events + transitions) from check locations
    graph_nodes = sorted(set(extracted.event_names) | set(extracted.connectors.keys()) | set(godhome_rules.keys()))
    graph_node_set = set(graph_nodes)

    node_rules = {k: v for k, v in all_rules.items() if k in graph_node_set}
    location_rules = {k: v for k, v in all_rules.items() if k not in graph_node_set}

    extra_checks_path = ROOT / "apworld" / "hkss" / "extra_checks_data.json"
    extra_count = 0
    if extra_checks_path.is_file():
        extra_data = json.loads(extra_checks_path.read_text(encoding="utf-8"))
        slash_expr = ["|", "LEFTSLASH", "RIGHTSLASH", "UPSLASH", "DOWNSLASH"]
        for kind in ("levers", "breakable_walls", "grass"):
            for entry in extra_data.get("hollow_knight", {}).get(kind, ()):
                transitions = entry.get("transitions", [])
                if not transitions:
                    continue
                room_expr = transitions[0] if len(transitions) == 1 else ["|", *transitions]
                parts = [room_expr, slash_expr]
                if entry.get("requiresQuake"):
                    parts.append("QUAKE")
                location_rules[entry["name"]] = ["&", *parts]
                extra_count += 1

    payload = {
        "starts": extracted.starts,
        "connectors": connectors,
        "itemEffects": extracted.item_effects,
        "itemTypes": extracted.items,
        "nodeRules": node_rules,
        "locationRules": location_rules,
    }

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUT_PATH.write_text(
        json.dumps(payload, separators=(",", ":"), sort_keys=True),
        encoding="utf-8",
    )

    size_kb = OUT_PATH.stat().st_size / 1024.0
    print(
        f"Wrote {OUT_PATH.relative_to(ROOT)} ({size_kb:.1f} KiB): "
        f"{len(node_rules)} node rules, {len(location_rules)} location rules, "
        f"{len(connectors)} connectors, {len(extracted.item_effects)} item effects."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
