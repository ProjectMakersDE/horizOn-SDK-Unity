#!/usr/bin/env python3
"""Actual Unity Editor processes are killed at owned journal boundaries, then reopened.
Usage: run-unity-probes.py UNITY_EXECUTABLE PROJECT_PATH EVIDENCE_DIRECTORY
The isolated project must already contain GroupsSampleStore.cs and GroupsCrashProbe.cs.
"""
import hashlib,json,os,signal,subprocess,sys,time
from pathlib import Path
unity,project,evidence=map(Path,sys.argv[1:]);evidence.mkdir(parents=True,exist_ok=True)
results=[]
for phase in ('locked-intent','unpublished-result','unpublished-null-group','before-ack','after-ack'):
    root=evidence/phase;root.mkdir(exist_ok=False)
    args=[str(unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','GroupsCrashProbe.Run','-groupsProbeRoot',str(root),'-groupsProbePhase',phase,'-logFile',str(root/'crash.log')]
    child=subprocess.Popen(args,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
    try:
        deadline=time.monotonic()+180
        while not (root/'ready').exists() and child.poll() is None and time.monotonic()<deadline:time.sleep(.1)
        if child.poll() is not None or not (root/'ready').exists():raise RuntimeError(f'{phase}: no exact crash-boundary readback; see crash.log')
        original_hash=(root/'ready').read_text();assert len(original_hash)==64
        # Only this Popen-owned PID; never search/kill unrelated Editors or helpers.
        os.kill(child.pid,signal.SIGKILL);code=child.wait(timeout=15);assert code==-signal.SIGKILL,code
        # Unity's Library lock is OS-owned; process death releases it before reopening.
        recover=args.copy();recover[recover.index(phase)]='recover-'+phase;recover[-1]=str(root/'recovery.log');recover.append('-quit')
        with (root/'recovery-stdout.log').open('wb')as stream:
            recovery=subprocess.run(recover,stdout=stream,stderr=subprocess.STDOUT,timeout=180)
        if recovery.returncode!=0 or (root/'verified').read_text()!='recover-'+phase:raise RuntimeError(f'{phase}: recovery failed; see recovery.log')
        files={str(f.relative_to(root)):hashlib.sha256(f.read_bytes()).hexdigest()for f in root.rglob('*')if f.is_file()}
        results.append({'phase':phase,'actualUnityProcessKilled':True,'signal':9,'originalHash':original_hash,'recoveryExit':0,'files':files})
        (evidence/'summary.json').write_text(json.dumps(results,indent=2)+'\n')
        print(f'{phase}: actual Unity process SIGKILL and recovery passed',flush=True)
    finally:
        if child.poll() is None:child.kill();child.wait(timeout=15)
print('All five actual Unity Editor process-death recovery probes passed.',flush=True)
