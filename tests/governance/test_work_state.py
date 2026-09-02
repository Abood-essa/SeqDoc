import json, shutil, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
import tools.governance.work_state as ws

ROOT = Path(__file__).parents[2]

class WorkStateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.d = Path(self.temp.name)
        (self.d / "docs/project/work-items").mkdir(parents=True)
        (self.d / "docs/project").mkdir(exist_ok=True)
        shutil.copy(ROOT / "docs/project/work-state.schema.json", self.d / "docs/project/work-state.schema.json")
        shutil.copytree(ROOT / "docs/work", self.d / "docs/work")
        self.items = [json.loads(p.read_text()) for p in (ROOT / "docs/project/work-items").glob("*.json")]
        self.normalize_lifecycle_fixture()
        self.write()
        self.assertEqual(ws.execution(self.d, False), 0)
    def tearDown(self): self.temp.cleanup()
    def write(self):
        for x in self.items:
            (self.d / "docs/project/work-items" / (x["id"] + ".json")).write_text(json.dumps(x))
    def find(self, i): return next(x for x in self.items if x["id"] == i)
    def normalize_lifecycle_fixture(self):
        for i in ("GH-12", "GH-13"):
            x=self.find(i); x["lifecycle"]="Blocked"; x["lifecycleLabel"]="blocked"; x["expectedGithubState"]="OPEN"
        capsule=self.d/"docs/work/persistence/I12/checkpoint.md"
        lines=capsule.read_text().splitlines(keepends=True)
        lines[4]="`Blocked`\n"
        capsule.write_text("".join(lines))
    def reset_gws1_transition_fixture(self):
        gws1=self.find("GWS1"); gws1["lifecycle"]="Verifying"; gws1["lifecycleLabel"]="verifying"; gws1["selectedForExecution"]=True
        issue12=self.find("GH-12"); issue12["lifecycle"]="Active"; issue12["lifecycleLabel"]="active"; issue12["expectedGithubState"]="OPEN"; issue12["nextAction"]="continue governance transition test"; issue12["selectedForExecution"]=False
        capsule=self.d/"docs/work/governance/GWS1/checkpoint.md"
        lines=capsule.read_text().splitlines(keepends=True)
        lines[4]="`Verifying`\n"
        capsule.write_text("".join(lines))
        i12=self.d/"docs/work/persistence/I12/checkpoint.md"
        lines=i12.read_text().splitlines(keepends=True)
        lines[4]="`Building`\n"
        i12.write_text("".join(lines))
        self.write(); self.assertEqual(ws.execution(self.d, False),0)
    def test_migrated_registry_is_valid_and_zero_selection_is_idle(self):
        self.assertEqual(ws.validate(self.d), 0)
        for x in self.items: x["selectedForExecution"] = False
        self.write()
        self.assertEqual(ws.validate(self.d), 0)
        self.assertEqual(ws.execution_object(self.items)["mode"], "idle")
        self.assertIsNone(ws.execution_object(self.items)["activeCheckpointId"])
    def test_missing_dependency_and_cycle_are_rejected(self):
        self.find("GH-13")["dependencies"] = ["GH-999"]; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
        self.find("GH-13")["dependencies"] = ["GH-12"]; self.find("GH-12")["dependencies"] = ["GH-13"]; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_active_requires_frozen_contract_and_baseline(self):
        x=self.find("GH-12"); x["lifecycle"]="Active"; x["lifecycleLabel"]="active"; x["baseline"]="bad"; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_closed_dependency_satisfies_active_child_but_active_does_not(self):
        self.assertEqual(ws.validate(self.d), 0)
        self.find("GH-9")["lifecycle"]="Active"; self.find("GH-9")["lifecycleLabel"]="active"; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_parallel_active_items_reject_two_selections(self):
        self.find("GH-3")["selectedForExecution"] = True; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_projection_is_deterministic_and_check_detects_stale_output(self):
        self.assertEqual(ws.execution(self.d, False), 0); first=(self.d/"docs/project/execution.json").read_text(); self.assertEqual(ws.execution(self.d, True), 0); self.assertEqual(first,(self.d/"docs/project/execution.json").read_text()); (self.d/"docs/project/execution.json").write_text("{}\n"); self.assertNotEqual(ws.execution(self.d, True), 0)
    def test_transition_rejects_illegal_state_change(self):
        self.assertNotEqual(ws.transition(self.d, type("A",(),{"id":"GH-13","state":"Closed","reason":None,"select":False,"check":True,"dry_run":False})()), 0)
        x=self.find("GH-16"); x["lifecycle"]="Draft"; x["lifecycleLabel"]=None; self.write()
        self.assertNotEqual(ws.transition(self.d, type("A",(),{"id":"GH-16","state":"Ready","reason":None,"select":True,"check":True,"dry_run":False})()), 0)
    def test_transition_check_is_non_mutating(self):
        self.reset_gws1_transition_fixture()
        before=(self.d/"docs/project/work-items/GWS1.json").read_text(); a=type("A",(),{"id":"GWS1","state":"ReviewRequired","reason":"returning to review after verification","select":True,"check":True,"dry_run":False})()
        self.assertEqual(ws.transition(self.d,a),0); self.assertEqual(before,(self.d/"docs/project/work-items/GWS1.json").read_text())
    def test_github_drift_parsing_is_read_only(self):
        payload=json.dumps([{"number":n,"state":next(x["expectedGithubState"] for x in self.items if x.get("number")==n),"labels":([{"name":ws.LABELS.get(self.find("GH-"+str(n))["lifecycle"])}] if self.find("GH-"+str(n))["lifecycle"] != "Closed" else [])} for n in [x["number"] for x in self.items if "number" in x]])
        with patch("subprocess.run", return_value=type("R",(),{"stdout":payload})()) as run: self.assertEqual(ws.gh(type("A",(),{"command":"check-github","repository":"x"})(),self.d),0); run.assert_called_once()
        for state in ("Draft", "Cancelled"):
            x=self.find("GH-12"); x["lifecycle"]=state; x["lifecycleLabel"]=None; self.write()
            remote=[]
            for y in self.items:
                if "number" in y:
                    label=ws.LABELS.get(y["lifecycle"])
                    remote.append({"number":y["number"],"state":y["expectedGithubState"],"labels":[{"name":label}] if label else []})
            with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()): self.assertEqual(ws.gh(type("A",(),{"command":"check-github","repository":"x"})(),self.d),0)
        a=type("A",(),{"command":"check-github","repository":"x"})()
        for result in ([], "not-json", OSError("offline")):
            with self.subTest(result=result):
                with patch("subprocess.run", side_effect=result if isinstance(result, BaseException) else type("R",(),{"stdout":json.dumps(result) if not isinstance(result,str) else result})()): self.assertNotEqual(ws.gh(a,self.d),0)
    def test_sync_dry_run_forms_only_label_commands(self):
        a=type("A",(),{"command":"sync-github","repository":"x","dry_run":True})()
        remote=[{"number":x["number"],"state":x["expectedGithubState"],"labels":[{"name":"unrelated"}]} for x in self.items if "number" in x]
        with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()) as run:
            with patch("builtins.print") as printed: self.assertEqual(ws.gh(a,self.d),0)
            self.assertEqual(run.call_count,1)
            output=" ".join(str(c) for c in printed.call_args_list)
            self.assertNotIn("unrelated", output)
        remote=[]
        for x in self.items:
            if "number" not in x: continue
            label=ws.LABELS.get(x["lifecycle"])
            labels=[{"name":"active"},{"name":"keep"}] if x["number"]==12 else ([] if x["number"]==13 else ([{"name":label}] if label else []))
            remote.append({"number":x["number"],"state":x["expectedGithubState"],"labels":labels})
        with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()):
            with patch("builtins.print") as printed: self.assertEqual(ws.gh(a,self.d),0)
        text=" ".join(str(c) for c in printed.call_args_list)
        self.assertIn("--remove-label active --add-label blocked", text)
        self.assertNotIn("keep", text); self.assertNotIn("issue edit 14", text)

    def test_schema_rejects_version_extra_missing_wrong_type_and_duplicate_dependency(self):
        for mutation in (lambda x: x.update(schemaVersion=2), lambda x: x.update(extra=1), lambda x: x.pop("owner"), lambda x: x.update(dependencies="GH-9"), lambda x: x.update(dependencies=["GH-9","GH-9"])):
            with self.subTest(mutation=mutation):
                candidate=__import__("copy").deepcopy(self.items); x=next(i for i in candidate if i["id"]=="GH-12"); mutation(x); self.assertTrue(ws.validate_items(candidate,self.d))
        duplicate=__import__("copy").deepcopy(self.items); duplicate.append(__import__("copy").deepcopy(duplicate[0])); duplicate_id=duplicate[0]["id"]; errors=ws.validate_items(duplicate,self.d); self.assertEqual([e for e in errors if e == f"duplicate id {duplicate_id}"], [f"duplicate id {duplicate_id}"])

    def test_capsule_projection_and_execution_identity(self):
        x=self.find("GH-12"); self.assertEqual(x["checkpointId"],"I12"); self.assertEqual(ws.validate_items(self.items,self.d),[])
        self.find("GWS1")["selectedForExecution"]=False; x["selectedForExecution"]=True; self.assertEqual(ws.execution_object(self.items)["activeCheckpointId"],"I12")
        candidate=__import__("copy").deepcopy(self.items); x=next(i for i in candidate if i["id"]=="GWS1"); x["selectedForExecution"]=False; x["lifecycle"]="Ready"; x["lifecycleLabel"]="ready"; selected=next(i for i in candidate if i["id"]=="GH-12"); selected["selectedForExecution"]=True; selected["nextAction"]=None
        self.assertTrue(any("selected item incomplete" in e for e in ws.validate_items(candidate,self.d)))
        broken={"docs/work/governance/GWS1":"# no state\n"}
        self.assertTrue(any("ambiguous capsule state" in e for e in ws.validate_items(self.items,self.d,broken)))

    def test_transition_invalid_is_non_mutating(self):
        before={p:p.read_bytes() for p in (self.d/"docs/project/work-items").glob("*.json")}; a=type("A",(),{"id":"GH-12","state":"Closed","reason":None,"select":False,"check":False,"dry_run":False})()
        self.assertNotEqual(ws.transition(self.d,a),0); self.assertEqual(before,{p:p.read_bytes() for p in before})
        self.reset_gws1_transition_fixture()
        paths=list(before)+[self.d/"docs/work/governance/GWS1/checkpoint.md",self.d/"docs/project/execution.json"]
        before={p:p.read_bytes() for p in paths}
        for recipient_id in ("GH-999", "GH-13"):
            invalid=type("A",(),{"id":"GWS1","state":"Closed","reason":None,"select":False,"select_id":recipient_id,"check":False,"dry_run":False})()
            self.assertNotEqual(ws.transition(self.d,invalid),0)
            self.assertEqual(before,{p:p.read_bytes() for p in paths})
        valid=type("A",(),{"id":"GWS1","state":"Closed","reason":None,"select":False,"select_id":"GH-12","check":False,"dry_run":False})()
        self.assertEqual(ws.transition(self.d,valid),0)
        records={x["id"]:json.loads((self.d/"docs/project/work-items"/(x["id"]+".json")).read_text()) for x in self.items}
        self.assertEqual(records["GWS1"]["lifecycle"],"Closed"); self.assertFalse(records["GWS1"]["selectedForExecution"])
        self.assertEqual((self.d/"docs/work/governance/GWS1/checkpoint.md").read_text().splitlines()[4],"`Closed`")
        self.assertEqual(records["GH-12"]["lifecycle"],"Active"); self.assertTrue(records["GH-12"]["selectedForExecution"])
        execution=json.loads((self.d/"docs/project/execution.json").read_text())
        self.assertEqual((execution["activeCheckpointId"],execution["activeCheckpointPath"]),("I12","docs/work/persistence/I12"))
        item=json.loads((self.d/"docs/project/work-items/GH-12.json").read_text()); item["lifecycle"]="Draft"; item["lifecycleLabel"]=None; item["selectedForExecution"]=False; (self.d/"docs/project/work-items/GH-12.json").write_text(json.dumps(item)); a=type("A",(),{"id":"GH-12","state":"Cancelled","reason":"cancelled governance test","select":False,"dry_run":False,"check":False})(); self.assertEqual(ws.transition(self.d,a),0); cancelled=json.loads((self.d/"docs/project/work-items/GH-12.json").read_text()); self.assertEqual(cancelled["lifecycle"],"Cancelled"); self.assertEqual(ws.validate(self.d),0); self.assertEqual((self.d/"docs/work/persistence/I12/checkpoint.md").read_text().splitlines()[4],"`Cancelled`")

    def test_transition_selected_to_blocked_without_recipient_leaves_idle(self):
        self.reset_gws1_transition_fixture()
        a=type("A",(),{"id":"GWS1","state":"Blocked","reason":"blocked for governance test","select":False,"dry_run":False,"check":False})()
        self.assertEqual(ws.transition(self.d, a), 0)
        records=json.loads((self.d/"docs/project/work-items/GWS1.json").read_text())
        self.assertEqual(records["lifecycle"], "Blocked")
        self.assertFalse(records["selectedForExecution"])
        execution=json.loads((self.d/"docs/project/execution.json").read_text())
        self.assertEqual(execution["mode"], "idle")
        self.assertIsNone(execution["sourceId"])

    def test_replace_failure_rolls_back_all_payloads(self):
        self.reset_gws1_transition_fixture()
        a=type("A",(),{"id":"GWS1","state":"ReviewRequired","reason":"returning to review after verification","select":True,"check":False,"dry_run":False})(); paths=list((self.d/"docs/project/work-items").glob("*.json"))+[self.d/"docs/work/governance/GWS1/checkpoint.md",self.d/"docs/project/execution.json"]; before={p:p.read_bytes() for p in paths}; real=__import__("os").replace; count=[0]
        def fail(src,dst):
            count[0]+=1
            if count[0]==2: raise OSError("simulated")
            return real(src,dst)
        with patch("os.replace",side_effect=fail): self.assertNotEqual(ws.transition(self.d,a),0)
        self.assertGreaterEqual(count[0],2)
        self.assertEqual(before,{p:p.read_bytes() for p in before})


if __name__ == "__main__": unittest.main()
