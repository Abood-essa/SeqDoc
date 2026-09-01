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
        self.write()
        self.assertEqual(ws.execution(self.d, False), 0)
    def tearDown(self): self.temp.cleanup()
    def write(self):
        for x in self.items:
            (self.d / "docs/project/work-items" / (x["id"] + ".json")).write_text(json.dumps(x))
    def find(self, i): return next(x for x in self.items if x["id"] == i)
    def test_migrated_registry_is_valid_and_has_one_selection(self): self.assertEqual(ws.validate(self.d), 0)
    def test_missing_dependency_and_cycle_are_rejected(self):
        self.find("GH-13")["dependencies"] = ["GH-999"]; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
        self.find("GH-13")["dependencies"] = ["GH-12"]; self.find("GH-12")["dependencies"] = ["GH-13"]; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_active_requires_frozen_contract_and_baseline(self):
        x=self.find("GH-12"); x["baseline"]="bad"; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_closed_dependency_satisfies_active_child_but_active_does_not(self):
        self.assertEqual(ws.validate(self.d), 0)
        self.find("GH-9")["lifecycle"]="Active"; self.find("GH-9")["lifecycleLabel"]="active"; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_parallel_active_items_allow_only_one_selection(self):
        self.find("GH-3")["selectedForExecution"] = True; self.write(); self.assertNotEqual(ws.validate(self.d), 0)
    def test_projection_is_deterministic_and_check_detects_stale_output(self):
        self.assertEqual(ws.execution(self.d, False), 0); first=(self.d/"docs/project/execution.json").read_text(); self.assertEqual(ws.execution(self.d, True), 0); self.assertEqual(first,(self.d/"docs/project/execution.json").read_text()); (self.d/"docs/project/execution.json").write_text("{}\n"); self.assertNotEqual(ws.execution(self.d, True), 0)
    def test_transition_rejects_illegal_state_change(self):
        self.assertNotEqual(ws.transition(self.d, type("A",(),{"id":"GH-13","state":"Closed","reason":None,"select":False,"check":True,"dry_run":False})()), 0)
        x=self.find("GH-16"); x["lifecycle"]="Draft"; x["lifecycleLabel"]=None; self.write()
        self.assertNotEqual(ws.transition(self.d, type("A",(),{"id":"GH-16","state":"Ready","reason":None,"select":True,"check":True,"dry_run":False})()), 0)
    def test_transition_check_is_non_mutating(self):
        before=(self.d/"docs/project/work-items/GWS1.json").read_text(); a=type("A",(),{"id":"GWS1","state":"ReviewRequired","reason":"returning to review after verification","select":True,"check":True,"dry_run":False})()
        self.assertEqual(ws.transition(self.d,a),0); self.assertEqual(before,(self.d/"docs/project/work-items/GWS1.json").read_text())
    def test_github_drift_parsing_is_read_only(self):
        payload=json.dumps([{"number":n,"state":("CLOSED" if n in {1,2,5,6,7,8,9,10,11,14,15,19,20,21,22,23,41,44} else "OPEN"),"labels":([{"name":ws.LABELS.get(self.find("GH-"+str(n))["lifecycle"])}] if self.find("GH-"+str(n))["lifecycle"] != "Closed" else [])} for n in [x["number"] for x in self.items if "number" in x]])
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
            labels=[{"name":"blocked"},{"name":"keep"}] if x["number"]==12 else ([] if x["number"]==13 else ([{"name":label}] if label else []))
            remote.append({"number":x["number"],"state":x["expectedGithubState"],"labels":labels})
        with patch("subprocess.run", return_value=type("R",(),{"stdout":json.dumps(remote)})()):
            with patch("builtins.print") as printed: self.assertEqual(ws.gh(a,self.d),0)
        text=" ".join(str(c) for c in printed.call_args_list)
        self.assertIn("--remove-label blocked --add-label active", text)
        self.assertNotIn("keep", text); self.assertNotIn("issue edit 14", text)

    def test_schema_rejects_version_extra_missing_wrong_type_and_duplicate_dependency(self):
        for mutation in (lambda x: x.update(schemaVersion=2), lambda x: x.update(extra=1), lambda x: x.pop("owner"), lambda x: x.update(dependencies="GH-9"), lambda x: x.update(dependencies=["GH-9","GH-9"])):
            with self.subTest(mutation=mutation):
                candidate=__import__("copy").deepcopy(self.items); x=next(i for i in candidate if i["id"]=="GH-12"); mutation(x); self.assertTrue(ws.validate_items(candidate,self.d))

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

    def test_replace_failure_rolls_back_all_payloads(self):
        a=type("A",(),{"id":"GWS1","state":"ReviewRequired","reason":"returning to review after verification","select":True,"check":False,"dry_run":False})(); paths=list((self.d/"docs/project/work-items").glob("*.json"))+[self.d/"docs/work/governance/GWS1/checkpoint.md",self.d/"docs/project/execution.json"]; before={p:p.read_bytes() for p in paths}; real=__import__("os").replace; count=[0]
        def fail(src,dst):
            count[0]+=1
            if count[0]==2: raise OSError("simulated")
            return real(src,dst)
        with patch("os.replace",side_effect=fail): self.assertNotEqual(ws.transition(self.d,a),0)
        self.assertGreaterEqual(count[0],2)
        self.assertEqual(before,{p:p.read_bytes() for p in before})


if __name__ == "__main__": unittest.main()
