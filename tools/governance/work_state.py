#!/usr/bin/env python3
"""Validation, projection, and deliberately bounded GitHub synchronization."""
import argparse, copy, json, os, re, subprocess, sys, tempfile
from pathlib import Path

STATES = {"Draft", "Blocked", "Ready", "Active", "ReviewRequired", "ResolvingFindings", "Verifying", "Closed", "Cancelled"}
LABELS = {"Blocked": "blocked", "Ready": "ready", "Active": "active", "ReviewRequired": "review-required", "ResolvingFindings": "resolving-findings", "Verifying": "verifying"}
TRANSITIONS = {"Draft": {"Blocked", "Ready", "Cancelled"}, "Blocked": {"Ready", "Cancelled"}, "Ready": {"Active", "Blocked", "Cancelled"}, "Active": {"ReviewRequired", "Blocked", "Verifying", "Cancelled"}, "ReviewRequired": {"ResolvingFindings", "Verifying", "Active", "Cancelled"}, "ResolvingFindings": {"ReviewRequired", "Verifying", "Blocked"}, "Verifying": {"Closed", "ReviewRequired", "Blocked"}, "Closed": set(), "Cancelled": set()}
SHA = re.compile(r"^[0-9a-f]{40}$")
URL = re.compile(r"^https://github\.com/[^/]+/[^/]+/(?:issues|pull)/[0-9]+(?:#.*)?$")
BRANCH = re.compile(r"^[A-Za-z0-9._/-]+$")
CAPSULE_STATES = {"Ready": "NotStarted", "Active": "Building", "ReviewRequired": "ReviewRequired", "ResolvingFindings": "ResolvingFindings", "Verifying": "Verifying", "Closed": "Closed", "Blocked": "Blocked"}

def schema(root):
    return json.loads((root / "docs/project/work-state.schema.json").read_text(encoding="utf-8"))

def load(root):
    p = root / "docs/project/work-items"
    return [json.loads(x.read_text(encoding="utf-8")) for x in sorted(p.glob("*.json"))]

def dump(obj):
    return json.dumps(obj, indent=2, ensure_ascii=False, sort_keys=True) + "\n"

def _json_type(v, typ):
    return ((typ == "null" and v is None) or (typ == "boolean" and isinstance(v, bool)) or
            (typ == "integer" and isinstance(v, int) and not isinstance(v, bool)) or
            (typ == "number" and isinstance(v, (int, float)) and not isinstance(v, bool)) or
            (typ == "string" and isinstance(v, str)) or (typ == "array" and isinstance(v, list)) or
            (typ == "object" and isinstance(v, dict)))

def validate_items(items, root=None, capsule_overrides=None):
    root = root or Path("."); errors = []; sch = schema(root)
    required = sch["required"]; props = sch["properties"]
    ids = {}; nums = {}
    for x in items:
        if not isinstance(x, dict): errors.append("record is not an object"); continue
        missing = [k for k in required if k not in x]
        errors += [f"{x.get('id', '<unknown>')} missing {k}" for k in missing]
        unknown = set(x) - set(props)
        errors += [f"{x.get('id', '<unknown>')} unknown property {k}" for k in sorted(unknown)]
        i = x.get("id"); ids[i] = x
        for k, rule in props.items():
            if k not in x: continue
            v = x[k]
            if "const" in rule and v != rule["const"]: errors.append(f"{i} invalid {k}")
            if "enum" in rule and v not in rule["enum"]: errors.append(f"{i} invalid {k}")
            types = rule.get("type"); types = types if isinstance(types, list) else [types] if types else []
            if types and not any(_json_type(v, t) for t in types): errors.append(f"{i} invalid type {k}"); continue
            if isinstance(v, str):
                if len(v) < rule.get("minLength", 0): errors.append(f"{i} short {k}")
                if "pattern" in rule and not re.search(rule["pattern"], v): errors.append(f"{i} invalid pattern {k}")
            if isinstance(v, int) and not isinstance(v, bool) and v < rule.get("minimum", v): errors.append(f"{i} below minimum {k}")
            if isinstance(v, list):
                if rule.get("uniqueItems") and len(v) != len({json.dumps(a, sort_keys=True) for a in v}): errors.append(f"{i} duplicate {k}")
                if isinstance(rule.get("items"), dict) and rule["items"].get("type") == "string" and any(not isinstance(a, str) for a in v): errors.append(f"{i} invalid item type {k}")
        if i in ids and ids[i] is not x: errors.append(f"duplicate id {i}")
        if isinstance(i, str) and i.startswith("GH-"):
            n=x.get("number")
            if n in nums: errors.append(f"duplicate issue number {n}")
            nums[n]=i
            if i != f"GH-{n}": errors.append(f"id/number mismatch {i}")
            if not URL.match(x.get("sourceUrl", "")): errors.append(f"invalid source URL {i}")
            if x.get("expectedGithubState") not in {"OPEN", "CLOSED"}: errors.append(f"GitHub state missing {i}")
        s=x.get("lifecycle")
        if s not in STATES: errors.append(f"invalid lifecycle {i}")
        if isinstance(i, str) and i.startswith("GH-") and ((s == "Closed") != (x.get("expectedGithubState") == "CLOSED")): errors.append(f"lifecycle/GitHub state mismatch {i}")
        if x.get("kind") == "synthetic" and x.get("expectedGithubState") != "NONE": errors.append(f"synthetic item has GitHub state {i}")
        if x.get("lifecycleLabel") != LABELS.get(s): errors.append(f"lifecycle label mismatch {i}")
        if x.get("branch") and (not BRANCH.match(x["branch"]) or x["branch"].startswith("/") or x["branch"].endswith("/")): errors.append(f"invalid branch {i}")
        if x.get("pr") and not URL.match(x["pr"]): errors.append(f"invalid PR URL {i}")
        if x.get("expectedGithubState") == "OPEN" and not x.get("contractRevision"): errors.append(f"{i} missing contract revision")
        if s in {"Ready", "Active"} and x.get("kind") not in {"parent", "synthetic"}:
            for k in ("owner", "track", "contractRevision", "baseline"):
                if not x.get(k): errors.append(f"{i} missing {k}")
            if x.get("baseline") and not SHA.match(x["baseline"]): errors.append(f"{i} invalid baseline")
        if x.get("selectedForExecution") and (s not in {"Active", "ReviewRequired", "ResolvingFindings", "Verifying"} or not x.get("checkpointId") or not x.get("checkpointPath") or not x.get("nextAction")): errors.append(f"selected item incomplete {i}")
        if s == "ReviewRequired" and x.get("kind") == "github-issue" and (not x.get("pr") or not ((x.get("checkpointPath")) or (x.get("contractRevision") and SHA.match(x.get("baseline", ""))))): errors.append(f"{i} review requires PR and frozen evidence")
    selected=[x for x in items if x.get("selectedForExecution")]
    if len(selected) != 1: errors.append(f"expected exactly one selected item, got {len(selected)}")
    for x in items:
        deps=x.get("dependencies", [])
        for d in deps:
            if d not in ids: errors.append(f"{x.get('id')} missing dependency {d}")
            if d == x.get("id"): errors.append(f"self dependency {d}")
        if x.get("lifecycle") in {"Ready", "Active"} and x.get("kind") not in {"parent", "synthetic"}:
            for d in deps:
                if ids.get(d, {}).get("lifecycle") != "Closed": errors.append(f"{x['id']} dependency not closed: {d}")
    def visit(i, stack):
        if i in stack: errors.append("dependency cycle: " + " -> ".join(stack+[i])); return
        for d in ids.get(i, {}).get("dependencies", []): visit(d, stack+[i])
    for i in ids: visit(i, [])
    for x in items:
        p=x.get("checkpointPath")
        if p:
            text = capsule_overrides.get(p) if capsule_overrides and p in capsule_overrides else ((root / p / "checkpoint.md").read_text(encoding="utf-8") if (root / p / "checkpoint.md").exists() else None)
            if text is None: errors.append(f"{x['id']} missing checkpoint capsule"); continue
            lines=text.splitlines(); hits=[n for n,line in enumerate(lines) if line.strip() == "## State"]
            following = [line.strip() for line in lines[hits[0]+1:] if line.strip()] if len(hits) == 1 else []
            if len(hits) != 1 or not following or not re.fullmatch(r"`[^`]+`", following[0]): errors.append(f"{x['id']} ambiguous capsule state")
            elif following[0][1:-1] != CAPSULE_STATES.get(x.get("lifecycle")): errors.append(f"{x['id']} capsule state mismatch")
    return sorted(set(errors))

def validate(root):
    errors=validate_items(load(root), root)
    if errors: print("\n".join(errors), file=sys.stderr); return 1
    print(f"valid: {len(load(root))} work items"); return 0

def execution_object(items):
    selected=next((x for x in items if x.get("selectedForExecution")), None)
    return {"schemaVersion":1,"generatedBy":"tools/governance/work_state.py","sourceId":selected.get("id") if selected else None,"mode":"active" if selected else "idle","activePlanPath":None,"activeCheckpointPath":selected.get("checkpointPath") if selected else None,"activeCheckpointId":selected.get("checkpointId") if selected else None,"nextAction":selected.get("nextAction") if selected else "No work item is selected."}
def execution_payload(items): return dump(execution_object(items))
def execution(root, check=False):
    items=load(root); errors=validate_items(items, root)
    if errors: print("\n".join(errors), file=sys.stderr); return 1
    path=root/"docs/project/execution.json"; expected=execution_payload(items); actual=path.read_text(encoding="utf-8") if path.exists() else ""
    if check and actual != expected: print("execution.json is stale", file=sys.stderr); return 1
    if not check and actual != expected: path.write_text(expected, encoding="utf-8")
    print("execution projection: " + ("current" if check else "written")); return 0

def read_remote(repo):
    try:
        r=subprocess.run(["gh","issue","list","--repo",repo,"--state","all","--limit","1000","--json","number,state,labels"],capture_output=True,text=True,check=True)
        data=json.loads(r.stdout)
        if (not isinstance(data,list) or any(not isinstance(x,dict) or not isinstance(x.get("number"),int) or not isinstance(x.get("labels"),list) or any(not isinstance(z,dict) or not isinstance(z.get("name"),str) for z in x["labels"]) for x in data)): raise ValueError("malformed response")
        return {x["number"]:x for x in data}
    except (OSError, subprocess.SubprocessError, ValueError, TypeError, json.JSONDecodeError) as e:
        print(f"GitHub read failed: {e}", file=sys.stderr); return None
def gh(args, root):
    items=load(root); remote=read_remote(args.repository)
    if remote is None: return 1
    drift=[]; commands=[]; lifecycle=set(LABELS.values())
    for x in items:
        if "number" not in x: continue
        r=remote.get(x["number"]); expected=LABELS.get(x["lifecycle"]); attached={z.get("name") for z in r.get("labels",[]) if isinstance(z,dict)} if r else set()
        if not r:
            drift.append(f"GH-{x['number']}: missing")
            continue
        if r.get("state") != x.get("expectedGithubState"): drift.append(f"GH-{x['number']}: state")
        labels=attached & lifecycle
        if len(labels) != (1 if expected else 0) or (expected and labels != {expected}): drift.append(f"GH-{x['number']}: lifecycle label")
        if args.command == "sync-github":
            wrong_lifecycle_labels = labels - ({expected} if expected else set())
            changes=[("--remove-label", l) for l in sorted(wrong_lifecycle_labels)] + ([ ("--add-label", expected) ] if expected and expected not in attached else [])
            if changes: commands.append(["gh","issue","edit",str(x["number"]),"--repo",args.repository] + [a for pair in changes for a in pair])
    if args.command == "check-github":
        if drift: print("\n".join(drift)); return 1
        print("GitHub projection: current"); return 0
    if any(d.endswith(": missing") for d in drift):
        print("\n".join(drift), file=sys.stderr); return 1
    for label in sorted(lifecycle): commands.insert(0,["gh","label","create",label,"--repo",args.repository,"--color","ededed","--force"])
    for c in commands:
        print(("DRY-RUN " if args.dry_run else "") + " ".join(c))
        if not args.dry_run:
            try: subprocess.run(c, check=True)
            except (OSError, subprocess.SubprocessError) as e: print(f"GitHub write failed: {e}", file=sys.stderr); return 1
    return 0

def transition(root, args):
    items=load(root); candidate=copy.deepcopy(items); target=next((x for x in candidate if x["id"]==args.id),None)
    select_id=getattr(args, "select_id", None)
    if getattr(args, "select", False) and select_id:
        print("--select and --select-id are mutually exclusive", file=sys.stderr); return 1
    if not target or not args.state: print("--id and --state are required", file=sys.stderr); return 1
    if args.state not in TRANSITIONS.get(target["lifecycle"], set()): print(f"illegal transition {target['lifecycle']} -> {args.state}", file=sys.stderr); return 1
    recipient=None
    if select_id:
        recipient=next((x for x in candidate if x["id"]==select_id),None)
        if (recipient is None or recipient is target or recipient.get("lifecycle") not in {"Active","ReviewRequired","ResolvingFindings","Verifying"}
                or not recipient.get("checkpointId") or not recipient.get("checkpointPath") or not recipient.get("nextAction")):
            print("selection recipient is missing or not selectable", file=sys.stderr); return 1
    elif target.get("selectedForExecution") and args.state not in {"Active","ReviewRequired","ResolvingFindings","Verifying"}:
        others=[x for x in candidate if x is not target and x.get("selectedForExecution")]
        if len(others) != 1 or others[0].get("lifecycle") not in {"Active","ReviewRequired","ResolvingFindings","Verifying"} or not others[0].get("checkpointId") or not others[0].get("checkpointPath") or not others[0].get("nextAction"):
            print("selected item would become unselectable",file=sys.stderr); return 1
    target["lifecycle"]=args.state; target["lifecycleLabel"]=LABELS.get(args.state)
    for key in ("reason", "checkpoint_id", "checkpoint_path", "next_action", "pr", "branch", "baseline", "contract_revision"):
        value=getattr(args,key,None)
        if value is not None: target[{"reason":"statusReason","checkpoint_id":"checkpointId","checkpoint_path":"checkpointPath","next_action":"nextAction","contract_revision":"contractRevision"}.get(key,key)] = value
    if select_id:
        for x in candidate: x["selectedForExecution"] = x is recipient
    elif args.select:
        if args.state not in {"Active","ReviewRequired","ResolvingFindings","Verifying"}: print("selected lifecycle is not selectable",file=sys.stderr); return 1
        for x in candidate: x["selectedForExecution"] = x is target
    elif target.get("selectedForExecution") and args.state not in {"Active","ReviewRequired","ResolvingFindings","Verifying"}:
        target["selectedForExecution"] = False
    overrides={}
    if target.get("checkpointPath"):
        p=root/target["checkpointPath"]/"checkpoint.md"; text=p.read_text(encoding="utf-8") if p.exists() else None
        if text is not None:
            lines=text.splitlines(); n=next((i for i,l in enumerate(lines) if l.strip()=="## State"),None)
            if n is not None:
                state_line=next((i for i in range(n+1,len(lines)) if lines[i].strip()),None)
                if state_line is not None: lines[state_line]=f"`{CAPSULE_STATES.get(target['lifecycle'])}`"; overrides[target["checkpointPath"]]='\n'.join(lines)+'\n'
    errors=validate_items(candidate, root, overrides)
    if errors: print("\n".join(errors),file=sys.stderr); return 1
    payloads={root/"docs/project/work-items"/(x["id"]+".json"):dump(x) for x in candidate if dump(x)!= (root/"docs/project/work-items"/(x["id"]+".json")).read_text(encoding="utf-8")}
    if overrides and target.get("checkpointPath"): payloads[root/target["checkpointPath"] / "checkpoint.md"] = overrides[target["checkpointPath"]]
    ep=root/"docs/project/execution.json"; newexec=execution_payload(candidate)
    if newexec != ep.read_text(encoding="utf-8"): payloads[ep]=newexec
    print(dump(target), end="")
    if args.check or args.dry_run: return 0
    originals={p:(p.read_bytes() if p.exists() else None) for p in payloads}; staged=[]; replaced=[]
    try:
        for p,data in payloads.items():
            fd,tmp=tempfile.mkstemp(dir=p.parent, prefix=".work-state-"); os.write(fd, data.encode("utf-8")); os.fsync(fd); os.close(fd); staged.append((p,Path(tmp)))
        for p,tmp in staged: os.replace(tmp,p); replaced.append(p)
    except (OSError, IOError) as e:
        for p in reversed(replaced):
            if originals[p] is None: p.unlink(missing_ok=True)
            else:
                fd,tmp=tempfile.mkstemp(dir=p.parent); os.write(fd,originals[p]); os.fsync(fd); os.close(fd); os.replace(tmp,p)
        for _,tmp in staged: tmp.unlink(missing_ok=True)
        print(f"transition write rolled back: {e}",file=sys.stderr); return 1
    return 0

def main():
    p=argparse.ArgumentParser(); p.add_argument("command",choices=["validate","project-execution","check-github","sync-github","transition"]); p.add_argument("--root",type=Path,default=Path(".")); p.add_argument("--check",action="store_true"); p.add_argument("--dry-run",action="store_true"); p.add_argument("--repository",default="Bilaltariq41/SeqDoc"); p.add_argument("--id"); p.add_argument("--state"); p.add_argument("--reason"); selection=p.add_mutually_exclusive_group(); selection.add_argument("--select",action="store_true"); selection.add_argument("--select-id"); p.add_argument("--checkpoint-id"); p.add_argument("--checkpoint-path"); p.add_argument("--next-action"); p.add_argument("--pr"); p.add_argument("--branch"); p.add_argument("--baseline"); p.add_argument("--contract-revision"); a=p.parse_args()
    if a.command=="validate": return validate(a.root)
    if a.command=="project-execution": return execution(a.root,a.check)
    if a.command in {"check-github","sync-github"}: return gh(a,a.root)
    return transition(a.root,a)
if __name__ == "__main__": sys.exit(main())
