from pathlib import Path
import hashlib,json,re,subprocess,struct,datetime,sys
sys.stdout.reconfigure(encoding="utf-8")
repo=Path('E:/SqlXmlAnalyzer')
run=repo/'DOCS/implementation/IMP-02-04-hardening/20260908T094301770Z_c5b1e0766cfc_b1da315a'
prior=repo/'DOCS/implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38'
def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def save(name,value):
    p=run/name.replace(".json","-v2.json")
    if p.exists(): raise RuntimeError('Refusing to overwrite evidence: '+str(p))
    p.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
def git(*args): return subprocess.check_output(['git','-c','core.quotepath=false',*args],cwd=repo,text=True,encoding='utf-8').strip()
owned='''SqlXmlAnalyzer.Core/Diagnostics/ExceptionPolicy.cs
SqlXmlAnalyzer.Core/Diagnostics/UnexpectedErrorReporter.cs
SqlXmlAnalyzer.Core/Diagnostics/WindowsMiniDumpWriter.cs
SqlXmlAnalyzer.Core/Diagnostics/MinidumpValidator.cs
SqlXmlAnalyzer.Core/Logger.cs
src/SqlXmlAnalyzer.Refactoring/SqlRefactoringEngine.cs
src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs
src/SqlXmlAnalyzer.Application/Services/SqlWritebackService.cs
src/SqlXmlAnalyzer.Application/Services/ApplicationLoggerProvider.cs
src/SqlXmlAnalyzer.Application/Models/SqlWritebackResult.cs
SqlXmlAnalyzer.CLI/Program.cs
SqlXmlAnalyzer.Tests/Application/SqlWritebackServiceTests.cs
SqlXmlAnalyzer.Tests/Refactoring/UnsafeRewriteProtectionTests.cs
SqlXmlAnalyzer.Tests/Diagnostics/DiagnosticLoggingTests.cs'''.splitlines()
docs=['DOCS/'+x for x in ['IMP-02-04加固与实施符合性核对.md','软件改善实施规划.md','IMP-02审查反例验收矩阵.md','IMP-03安全写回与恢复说明.md','IMP-04高风险改写限制与诊断说明.md','acceptance/IMP-02/README.md']]
adapters=['DOCS/acceptance/IMP-02/'+x for x in ['AcceptanceInfrastructure.psm1','AcceptanceProbe.cs.txt','Run-AcceptanceMatrix.ps1','Test-AcceptanceInfrastructure.ps1']]
paths=set(git('ls-files').splitlines()+git('ls-files','--others','--exclude-standard').splitlines())
paths=sorted(p for p in paths if p and not p.startswith('DOCS/') and not re.search(r'(?i)(^|/)(\.git|bin|obj|\.vs|publish(?:-[^/]*)?|backups|\.tmp\.[^/]*)(/|$)',p))
current={p:sha(repo/p) if (repo/p).is_file() else None for p in paths}
before={e['Path']:e['Sha256'] for e in read(run/'source-before.json')['Files']}
# First-pass inventory used Git's quoted names and missed four existing Chinese-named assets.
# Repair only these four using the immutable prior accepted run, with matching current hashes.
previous=read(repo/'DOCS/acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/input-snapshot-before.json')
known={e['path']:e['sha256'] for e in previous['inputs']}
supplement=[]
for p in ['ssms_icons/死锁展示图.jpg','ssms_icons/节点属性参考.jpg','ssms_icons/节点属性显示信息.jpg','ssms_icons/节点提示信息文字有重叠.jpg']:
    if p in before or current.get(p)!=known.get(p): raise RuntimeError('Unexpected asset history: '+p)
    before[p]=known[p]
    supplement.append({'path':p,'priorSha256':known[p],'currentSha256':current[p],'unchanged':True})
save('baseline-inventory-supplement.json',{'reason':'Initial git ls-files used quoted non-ASCII names; four pre-existing image files were not hashed. Original source-before.json and failed validation remain unchanged.','priorEvidence':'DOCS/acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/input-snapshot-before.json','entries':supplement})
changes=[dict(path=p,beforeSha256=before.get(p),afterSha256=current.get(p),owner='IMP-02-04 joint hardening' if p in owned else 'UNEXPECTED') for p in sorted(set(before)|set(current)) if before.get(p)!=current.get(p)]
unexpected=[e for e in changes if e['owner']=='UNEXPECTED']
save('source-final.json',{'head':git('rev-parse','HEAD'),'files':current})
save('change-ownership.json',{'sourceChanges':changes,'documentationFiles':docs,'adapterFiles':adapters,'preservedUserChanges':['AGENTS.md','MainWindow.Services.cs','MainWindow.KeyboardShortcuts.cs','Views/ShellNavigationRail.xaml']})
snapshots=[]
for name in owned+docs+adapters:
    target=run/'source-snapshots-v2'/(name+'.snapshot')
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists(): raise RuntimeError('Snapshot already exists')
    target.write_bytes((repo/name).read_bytes())
    snapshots.append({'source':name,'snapshot':target.relative_to(run).as_posix(),'sha256':sha(target)})
save('source-snapshots.json',snapshots)
old=read(prior/'deliverable-validation.json')
histories=[repo/e['manifest'] for e in old['historyAndFinalArtifactChecks']]+[prior/'artifact-manifest.json',prior/'process-artifact-manifest.json']
debug=repo/'DOCS/acceptance/IMP-02/runs/20260908T095232442Z_c5b1e0766cfc_3a4bacdf'
release=repo/'DOCS/acceptance/IMP-02/runs/20260908T095314857Z_c5b1e0766cfc_db4a0d4e'
timeout=repo/'DOCS/acceptance/IMP-02/runs/20260908T095336975Z_c5b1e0766cfc_b33f24a6'
host=run/'host-fault-adapter/runs/20260908T095325626Z_c5b1e0766cfc_3cb6e8a2'
checks=[]
for manifest in histories+[debug/'artifact-manifest.json',release/'artifact-manifest.json',timeout/'artifact-manifest.json',host/'artifact-manifest.json']:
    data=read(manifest); entries=data if isinstance(data,list) else data['entries']
    failures=[e['path'] for e in entries if not (manifest.parent/e['path']).is_file() or sha(manifest.parent/e['path'])!=e['sha256'].upper()]
    checks.append({'manifest':manifest.relative_to(repo).as_posix(),'entries':len(entries),'failures':failures})
retained=[]
for e in old['retainedInputs']:
    actual=sha(repo/e['path']);retained.append({'path':e['path'],'sha256':actual,'unchanged':actual==e['afterSha256']})
inputDrift={}
for config,folder in [('Debug',debug),('Release',release)]:
    inputs=read(folder/'input-snapshot-before.json')
    expected={e['path']:e['sha256'] if e['exists'] else None for e in inputs['inputs'] if not e['path'].startswith('DOCS/')}
    inputDrift[config]=[p for p in set(expected)|set(current) if expected.get(p)!=current.get(p)]
process=read(run/'process-final-02/summary.json')
hostDiag=read(host/'host-diagnostic.json')
dumpPaths=[Path(e['verification']['Diagnostic']['DumpPath']) for e in process['results'] if 'verification' in e]+[Path(hostDiag['DumpPath'])]
dumps=[]
for p in dumpPaths:
    data=p.read_bytes(); sig,version,count,directory=struct.unpack_from('<IIII',data)
    valid=sig==0x504d444d and version&0xffff==0xa793 and count>0 and directory>=32 and directory+count*12<=len(data)
    if valid:
        for i in range(count):
            typ,size,offset=struct.unpack_from('<III',data,directory+i*12)
            valid &= offset+size<=len(data)
    dumps.append({'path':p.relative_to(repo).as_posix(),'bytes':len(data),'sha256':sha(p),'streamCount':count,'structureValid':valid})
save('dump-validation.json',dumps)
command=read(timeout/'infrastructure-tests.command.json')
negative={'timeout':command['timedOut'] and command['exitCode']!=0 and (timeout/'artifact-manifest.json').exists(), 'hostUnknown':read(run/'host-fault-exit.json')['exitCode']==2 and hostDiag['DumpCreated'] and Path(hostDiag['MetadataPath']).is_file()}
diff=subprocess.run(['git','diff','--check','--',*owned],cwd=repo,text=True,encoding='utf-8',capture_output=True)
save('scoped-diff-check.json',{'exitCode':diff.returncode,'stdout':diff.stdout,'stderr':diff.stderr})
links=[]
future={str((run/n).resolve()) for n in ['deliverable-validation-v2.json','artifact-manifest-v2.json']}
for doc in docs:
    p=repo/doc
    for target in re.findall(r'\]\(([^)]+)\)',p.read_text(encoding='utf-8-sig')):
        target=target.strip('<>').split('#',1)[0]
        if not target or re.match(r'https?://',target): continue
        resolved=(p.parent/target).resolve()
        links.append({'document':doc,'target':target,'exists':resolved.exists() or str(resolved) in future})
passed=not unexpected and not any(inputDrift.values()) and all(e['unchanged'] for e in retained) and all(not e['failures'] for e in checks) and all(e['structureValid'] for e in dumps) and all(negative.values()) and diff.returncode==0 and all(e['exists'] for e in links) and process['total']==process['passed']==10
for folder in [debug,release]:
    s=read(folder/'summary.json');passed &= s['existingSuite']['passed']==905 and s['existingSuite']['failed']==0 and s['infrastructureTests']['passed']==38 and s['probeConditionsMet']==4 and s['notMet']==18 and not s['protectedInputDrift']
save('deliverable-validation.json',{'step':'IMP-02-04 hardening','timestampUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'head':git('rev-parse','HEAD'),'sourceChanged':len(changes),'baselineInventorySupplement':supplement,'unexpectedChanges':unexpected,'retainedInputs':retained,'inputDrift':inputDrift,'artifactChecks':checks,'debug':read(debug/'summary.json'),'release':read(release/'summary.json'),'processValidation':process,'negativePaths':negative,'dumpValidation':dumps,'scopedDiffCheckExitCode':diff.returncode,'linksChecked':len(links),'brokenLinks':[e for e in links if not e['exists']],'sqlServerExecutionPerformed':False,'wpfVisualTestPerformed':False,'coverageGenerated':False,'passed':bool(passed)})
# Enumerate only this newly created evidence subtree, including ignored Debug/Release and .dmp/.bak artifacts.
entries=[{'path':p.relative_to(run).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(run.rglob('*')) if p.is_file() and p.name!='artifact-manifest.json']
# Include nested manifests too; only the manifest being created excludes itself.
entries += [{'path':p.relative_to(run).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(run.rglob('artifact-manifest.json'))]
save('artifact-manifest.json',entries)
print(json.dumps({'passed':bool(passed),'sourceChanged':len(changes),'unexpectedChanges':unexpected,'historyAndCurrentManifests':len(checks),'manifestEntries':len(entries),'dumps':len(dumps),'linksChecked':len(links),'brokenLinks':[e for e in links if not e['exists']]},ensure_ascii=False,indent=2))
if not passed: raise SystemExit(1)
