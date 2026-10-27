param([string]$BaseUrl = 'http://127.0.0.1:5392')
$ErrorActionPreference='Stop'
$base=$BaseUrl.TrimEnd('/') + '/api/research'
$stages=Invoke-RestMethod "$base/stages"
# Meta readiness is a read-only local preflight. CI MUST NOT contact Meta or
# accidentally authorize paid requests, even when the UI exposes Muse buttons.
$meta=Invoke-RestMethod "$BaseUrl/api/meta/readiness"
if($meta.apiKeyConfigured -or $meta.creditsConfirmedByOwner -or
   $meta.ownerApprovedPaidRequests -or $meta.paidRequestsSwitchEnabled -or
   $meta.liveCallsPermitted -or $meta.promotionalBalanceVerifiedAutomatically -or
   $meta.maxRequestsPerProcess -ne 2 -or
   $meta.museModel -ne 'muse-spark-1.3' -or
   $meta.samModel -ne 'sam-3.1') {
  throw 'ERROR: CI Meta billing and credential safeguards are not closed'
}
Write-Host 'META_READINESS=NO_KEY,NO_BILLING_CONFIRMATION,NO_OWNER_AUTHORIZATION,PAID_DISABLED; REQUEST_CAP=2'
try {
  Invoke-RestMethod "$BaseUrl/api/math/plan" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM","language":"es"}' | Out-Null
  throw 'ERROR: Muse plan allowed paid traffic without gates'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 409) { throw }
}
try {
  Invoke-RestMethod "$BaseUrl/api/ai/segment-frame" -Method Post -ContentType 'application/json' -Body '{"concept":"ball","imageDataUrl":"data:image/png;base64,dGVzdA=="}' | Out-Null
  throw 'ERROR: SAM allowed paid traffic without gates'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 409) { throw }
}
Write-Host 'META_MODELS=SPARK_BLOCKED_409,SAM_BLOCKED_409; NO_PAID_CALLS=True'
$study=Invoke-RestMethod $base -Method Post -ContentType 'application/json' -Body '{"title":"CONTROL STUDY: Goldbach finite range and real Lean artifacts","discipline":"Mathematics"}'
Write-Host "PROJECT=$($study.id)"
try {
  Invoke-RestMethod "$base/$($study.id)/stages/1/muse-draft?lang=es" -Method Post -ContentType 'application/json' -Body '{}' | Out-Null
  throw 'ERROR: Muse generated a paid scientific draft with gates disabled'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 409) { throw }
}
Write-Host 'MUSE_DRAFT_BLOCKED=409; NO_MODEL_INFERENCE=True'
try {
  Invoke-RestMethod "$base/$($study.id)/math/offline-cycle" -Method Post -ContentType 'application/json' -Body '{}' | Out-Null
  throw 'ERROR: offline rehearsal allowed before stages 1-3'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 400) { throw }
  Write-Host 'OFFLINE_BEFORE_PROTOCOL_BLOCKED=400'
  try {
    Invoke-RestMethod "$base/$($study.id)/math/memory-cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}' | Out-Null
    throw 'ERROR: memory verification ran before the protocol was acknowledged'
  } catch {
    if($_.Exception.Message -like 'ERROR:*') { throw }
    if([int]$_.Exception.Response.StatusCode -ne 400) { throw }
    Write-Host 'MEMORY_BEFORE_PROTOCOL_BLOCKED=400'
  }
}
try {
  Invoke-RestMethod "$base/$($study.id)/math/attempts" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM","strategy":"omega"}' | Out-Null
  throw 'ERROR: early proof attempts allowed'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  Write-Host "BEFORE_PROTOCOL_BLOCKED=$([int]$_.Exception.Response.StatusCode)"
}
foreach($step in 1..3) {
  $definition=$stages | Where-Object {$_.index -eq $step}
  $fields=@{}
  foreach($field in $definition.fields) {
    $fields[$field.key]='CONTROL DATA ONLY. This report belongs to a testing fixture, not a peer-reviewed research result or novel scientific finding.'
  }
  if($step -eq 1) { $fields.sources='https://lean-lang.org/doc/reference/latest/Tactic-Proofs/Tactic-Reference/' }
  $guided=Invoke-RestMethod "$base/$($study.id)/stages/$step/guided-draft?lang=es"
  $guidedEn=Invoke-RestMethod "$base/$($study.id)/stages/$step/guided-draft?lang=en"
  $guidedFields=@($guided.fields.PSObject.Properties.Name)
  $guidedEnFields=@($guidedEn.fields.PSObject.Properties.Name)
  if($guided.mode -ne 'CURATED_DETERMINISTIC_OFFLINE_TEMPLATE_NOT_AI' -or
     $guided.isAutonomousAi -or $guided.isScientificDiscovery -or
     -not $guided.humanReviewRequired -or
     $guidedFields.Count -ne $definition.fields.Count -or
     $guidedEnFields.Count -ne $definition.fields.Count) {
    throw "ERROR: stage $step did not produce complete bilingual, non-AI drafts"
  }
  foreach($field in $definition.fields) {
    if($guided.fields.($field.key).Length -lt $field.minimumCharacters -or
       $guidedEn.fields.($field.key).Length -lt $field.minimumCharacters) {
      throw "ERROR: stage $step guided draft missing required detail"
    }
  }
  Write-Host "GUIDED_STAGE_$($step)=ES_EN_FIELDS_OK; AUTO_APPROVAL=False"
  $payload=@{fields=$fields}|ConvertTo-Json -Depth 6
  $study=Invoke-RestMethod "$base/$($study.id)/stages/$step/draft" -Method Post -ContentType 'application/json' -Body $payload
  $study=Invoke-RestMethod "$base/$($study.id)/stages/$step/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}'
}
Write-Host "PREPARED_PHASE=$($study.activeStage)"
try {
  Invoke-RestMethod "$base/$($study.id)/stages/4/guided-draft?lang=es" | Out-Null
  throw 'ERROR: stage4 draft preview invented evidence before tools ran'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 400) { throw }
  Write-Host 'GUIDED_STAGE4_NO_EVIDENCE_BLOCKED=400'
}
# Memory experiment A: exact known theorem, cold start then compiler-driven retry.
$empty=Invoke-RestMethod "$BaseUrl/api/math/memory/NAT-ADD-COMM"
if($empty.selectionMode -ne 'COLD_START' -or $empty.strategy -ne 'rfl') {
  throw 'ERROR: clean memory should initially select rfl'
}
$memA=Invoke-RestMethod "$base/$($study.id)/math/memory-cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}'
if($memA.advice.selectionMode -ne 'COLD_START' -or $memA.attempts.Count -ne 2 -or
   $memA.attempts[0].verification.outcome -ne 'REJECTED' -or
   $memA.attempts[1].verification.outcome -ne 'VERIFIED' -or
   $memA.isAutonomousAi -or $memA.isNewDiscovery) {
  throw 'ERROR: cold memory experience must observe a real Lean rejection and retry'
}
$sourceAttempt=$memA.attempts[1].verification.attemptId
$afterMemory=Invoke-RestMethod "$BaseUrl/api/math/memory/NAT-ADD-COMM"
if($afterMemory.strategy -ne 'omega' -or
   $afterMemory.selectionMode -ne 'RECHECK_VERIFIED_MEMORY' -or
   $afterMemory.sourceAttemptId -ne $sourceAttempt -or
   $afterMemory.sourceResearchId -ne $study.id) {
  throw 'ERROR: memory should select only exact previously verified tactic'
}
$unrelated=Invoke-RestMethod "$BaseUrl/api/math/memory/NAT-ADD-ZERO"
if($unrelated.selectionMode -ne 'COLD_START') {
  throw 'ERROR: memory improperly transferred tactics between theorems'
}
try {
  Invoke-RestMethod "$BaseUrl/api/math/memory/GOLDBACH-STRONG" | Out-Null
  throw 'ERROR: open conjecture must never use known theorem Lean memory'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 400) { throw }
}
# Independent research B: re-use A evidence, but Lean creates a new proof attempt.
$studyB=Invoke-RestMethod $base -Method Post -ContentType 'application/json' -Body '{"title":"MEMORY REPLAY CONTROL B: separate test-only dossier","discipline":"Mathematics"}'
foreach($step in 1..3) {
  $fields=@{}
  $definition=$stages | Where-Object {$_.index -eq $step}
  foreach($field in $definition.fields) {
    $fields[$field.key]='TEST FIXTURE ONLY. Synthetic completeness fields for software validation; no scientific peer review or new discovery.'
  }
  if($step -eq 1) { $fields.sources='https://lean-lang.org/doc/reference/latest/Tactic-Proofs/Tactic-Reference/' }
  $body=@{fields=$fields}|ConvertTo-Json -Depth 6
  $studyB=Invoke-RestMethod "$base/$($studyB.id)/stages/$step/draft" -Method Post -ContentType 'application/json' -Body $body
  $studyB=Invoke-RestMethod "$base/$($studyB.id)/stages/$step/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}'
}
$memB=Invoke-RestMethod "$base/$($studyB.id)/math/memory-cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}'
$afterB=Invoke-RestMethod "$base/$($studyB.id)"
if($memB.attempts.Count -ne 1 -or $memB.advice.strategy -ne 'omega' -or
   $memB.advice.sourceAttemptId -ne $sourceAttempt -or
   $memB.advice.sourceResearchId -ne $study.id -or
   $memB.attempts[0].verification.outcome -ne 'VERIFIED' -or
   $memB.attempts[0].verification.attemptId -eq $sourceAttempt -or
   $memB.isNewDiscovery -or $memB.isAutonomousAi -or
   $afterB.activeStage -ne 4) {
  throw 'ERROR: warm memory must re-verify independently in another dossier'
}
$memoryRecord=Invoke-RestMethod "$base/$($studyB.id)/math/memory-cycles"
$memoryReport=Invoke-WebRequest "$base/$($studyB.id)/reports/4?lang=es" -UseBasicParsing
$memoryReportEn=Invoke-WebRequest "$base/$($studyB.id)/reports/4?lang=en" -UseBasicParsing
if($memoryRecord.Count -ne 1 -or
   -not $memoryReport.Content.Contains($memB.cycleId) -or
   -not $memoryReportEn.Content.Contains($memB.cycleId)) {
  throw 'ERROR: bilingual memory evidence has not been recorded'
}
Write-Host "MEMORY_COLD=$(($memA.attempts|ForEach-Object {$_.verification.outcome}) -join ',') MEMORY_WARM=$($memB.attempts[0].verification.outcome) MEMORY_REUSED=$sourceAttempt NEW_ID=$($memB.attempts[0].verification.attemptId) CROSS_GOAL_BLOCKED=True"
# The same dossier has a strict budget of three memory-guided cycles.
$memB2=Invoke-RestMethod "$base/$($studyB.id)/math/memory-cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}'
$memB3=Invoke-RestMethod "$base/$($studyB.id)/math/memory-cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}'
try {
  Invoke-RestMethod "$base/$($studyB.id)/math/memory-cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}' | Out-Null
  throw 'ERROR: fourth memory cycle must be blocked'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  if([int]$_.Exception.Response.StatusCode -ne 400) { throw }
}
$allMemoryCycles=Invoke-RestMethod "$base/$($studyB.id)/math/memory-cycles"
if($allMemoryCycles.Count -ne 3) {
  throw 'ERROR: memory cycle limit or history persistence is incorrect'
}
Write-Host 'MEMORY_CYCLE_LIMIT=3 FOURTH_BLOCKED=400'

$conjectures=Invoke-RestMethod "$BaseUrl/api/math/conjectures"
if(-not ($conjectures | Where-Object {$_.id -eq 'GOLDBACH-STRONG' -and -not $_.isExecutableLeanGoal})) {
  throw 'ERROR: curated open conjecture missing or incorrectly executable'
}
$finite=Invoke-RestMethod "$base/$($study.id)/math/conjectures/GOLDBACH-STRONG/finite-check" -Method Post -ContentType 'application/json' -Body '{"evenLimit":100}'
if($finite.result.evenCasesExamined -ne 49 -or
   $finite.result.casesWithPrimePair -ne 49 -or
   $finite.result.formalProofVerified -or $finite.result.isNewDiscovery) {
  throw 'ERROR: bounded computation incorrectly reported'
}
$finiteId=$finite.result.experimentId
$finiteHistory=Invoke-RestMethod "$base/$($study.id)/math/finite-checks"
if(-not ($finiteHistory | Where-Object {$_.result.experimentId -eq $finiteId})) {
  throw 'ERROR: finite evidence was not persisted'
}
Write-Host "FINITE_GOLDBACH_CASES=$($finite.result.evenCasesExamined) PROOF=$($finite.result.formalProofVerified) ID=$finiteId"
# Scripted agent rehearsal: real controlled scientific tools, no inference.
$cap=Invoke-RestMethod "$BaseUrl/api/math/agent/capabilities"
if(-not $cap.offlineRehearsalAvailable -or $cap.museLiveValidated -or
   $cap.arbitraryLeanExecutionEnabled -or $cap.maxToolCallsPerCycle -ne 3) {
  throw 'ERROR: unsafe offline rehearsal capabilities'
}
$rehearsal=Invoke-RestMethod "$base/$($study.id)/math/offline-cycle" -Method Post -ContentType 'application/json' -Body '{}'
$steps=@($rehearsal.events | Where-Object {$_.kind -eq 'TOOL'})
if($rehearsal.toolCallCount -ne 3 -or $steps.Count -ne 3 -or
   $rehearsal.isAutonomousAi -or $rehearsal.isScientificDiscovery -or
   $rehearsal.goldbachFormalProofVerified -or
   $steps[0].tool -ne 'CHECK_GOLDBACH_FINITE' -or
   $steps[0].outcome -ne 'NO_COUNTEREXAMPLE_IN_FINITE_RANGE' -or
   $steps[1].outcome -ne 'REJECTED' -or
   $steps[2].outcome -ne 'VERIFIED' -or
   $rehearsal.conclusionEn -notmatch 'Goldbach.*UNSOLVED') {
  throw 'ERROR: scripted scientific feedback cycle not correct or incorrectly claims discovery'
}
$offlineEvents=Invoke-RestMethod "$base/$($study.id)/math/offline-events"
if(-not ($offlineEvents | Where-Object {$_.cycleId -eq $rehearsal.cycleId -and $_.kind -eq 'CONCLUSION'})) {
  throw 'ERROR: offline decisions or conclusions were not persisted'
}
$goldbachEvidence=Invoke-RestMethod "$base/$($study.id)/math/finite-checks"
$proofEvidence=Invoke-RestMethod "$base/$($study.id)/math/attempts"
foreach($step in $steps) {
  if(-not $step.evidenceId -or $step.evidenceSha256.Length -ne 64) {
    throw 'ERROR: tool event lacks a real evidence identifier or hash'
  }
}
if(-not ($goldbachEvidence | Where-Object {$_.result.experimentId -eq $steps[0].evidenceId}) -or
   -not ($proofEvidence | Where-Object {$_.verification.attemptId -eq $steps[1].evidenceId}) -or
   -not ($proofEvidence | Where-Object {$_.verification.attemptId -eq $steps[2].evidenceId})) {
  throw 'ERROR: offline decision trace is not linked to real tool results'
}
$after=Invoke-RestMethod "$base/$($study.id)"
if($after.activeStage -ne 4 -or
   ($after.reports | Where-Object {$_.index -eq 4}).status -ne 'DRAFT') {
  throw 'ERROR: rehearsal improperly autoapproved scientific reports'
}
Write-Host "OFFLINE_MODE=$($rehearsal.mode) OFFLINE_CYCLE=$($rehearsal.cycleId) TOOLS=$($rehearsal.toolCallCount) RESULTS=$(($steps | ForEach-Object {$_.outcome}) -join ',') AI=$($rehearsal.isAutonomousAi) DISCOVERY=$($rehearsal.isScientificDiscovery)"

$suggest4=Invoke-RestMethod "$base/$($study.id)/stages/4/guided-draft?lang=es"
$suggest4En=Invoke-RestMethod "$base/$($study.id)/stages/4/guided-draft?lang=en"
if(-not $suggest4.fields.manifest.Contains($steps[2].evidenceId) -or
   -not $suggest4.fields.manifest.Contains($steps[0].evidenceId) -or
   -not $suggest4En.fields.manifest.Contains($steps[0].evidenceId) -or
   $suggest4.isAutonomousAi -or $suggest4.isScientificDiscovery) {
  throw 'ERROR: stage4 guided draft lacks genuine Lean or finite evidence'
}
Write-Host 'GUIDED_STAGE_4=ACTUAL_IDS_ONLY; GOLDBACH_PROOF=False'
$fields4=@{}
foreach($field in (($stages | Where-Object {$_.index -eq 4}).fields)) {
  $fields4[$field.key]='CONTROL DATA ONLY. This is a synthetic description of a documentation stage, not physical or mathematical discovery.'
}
$payload4=@{fields=$fields4}|ConvertTo-Json -Depth 7
$study=Invoke-RestMethod "$base/$($study.id)/stages/4/draft" -Method Post -ContentType 'application/json' -Body $payload4
try {
  Invoke-RestMethod "$base/$($study.id)/stages/4/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}' | Out-Null
  throw 'ERROR: completed stage4 without Lean attempt'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  Write-Host "NO_EVIDENCE_BLOCKED=$([int]$_.Exception.Response.StatusCode)"
}
$cycle=Invoke-RestMethod "$base/$($study.id)/math/cycle" -Method Post -ContentType 'application/json' -Body '{"problemId":"NAT-ADD-COMM"}'
Write-Host "CYCLE_MODE=$($cycle.mode) ATTEMPTS=$($cycle.attempts.Count) RESULTS=$(($cycle.attempts|ForEach-Object {$_.verification.outcome}) -join ',') NEW_DISCOVERY=$($cycle.isScientificDiscovery)"
$attempts=Invoke-RestMethod "$base/$($study.id)/math/attempts"
$record=$attempts[-1]
Write-Host "LAST_ATTEMPT=$($record.verification.attemptId)"
try {
  Invoke-RestMethod "$base/$($study.id)/stages/4/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}' | Out-Null
  throw 'ERROR: stage4 approved without citation'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  Write-Host "NO_ATTEMPT_CITATION_BLOCKED=$([int]$_.Exception.Response.StatusCode)"
}
$fields4.manifest="SERVER ATTEMPT ID: $($record.verification.attemptId). A known theorem was checked by Lean4, this does not imply novelty. SHA256=$($record.verification.sourceSha256)"
$payload4=@{fields=$fields4}|ConvertTo-Json -Depth 7
$study=Invoke-RestMethod "$base/$($study.id)/stages/4/draft" -Method Post -ContentType 'application/json' -Body $payload4
$study=Invoke-RestMethod "$base/$($study.id)/stages/4/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}'
Write-Host "STAGE4_APPROVED_NEXT=$($study.activeStage)"
$suggest5=Invoke-RestMethod "$base/$($study.id)/stages/5/guided-draft?lang=es"
$suggest5En=Invoke-RestMethod "$base/$($study.id)/stages/5/guided-draft?lang=en"
if(-not $suggest5.fields.verification.Contains($record.verification.attemptId) -or
   -not $suggest5.fields.results.Contains($steps[0].evidenceId) -or
   -not $suggest5En.fields.verification.Contains($record.verification.attemptId) -or
   $suggest5.isAutonomousAi -or $suggest5.isScientificDiscovery) {
  throw 'ERROR: stage5 guided draft must cite genuine, distinct formal and finite evidence'
}
Write-Host 'GUIDED_STAGE_5=ACTUAL_IDS_ONLY; NO_AUTO_DISCOVERY=True'
$fields5=@{}
foreach($field in (($stages | Where-Object {$_.index -eq 5}).fields)) {
  $fields5[$field.key]='CONTROL DATA ONLY. This is a known math theorem verification benchmark, not a new theorem or open problem solution.'
}
$fields5.verification='CONTROL DATA ONLY. The proof must be referenced by a real server-generated attempt; missing now.'
$payload5=@{fields=$fields5}|ConvertTo-Json -Depth 7
$study=Invoke-RestMethod "$base/$($study.id)/stages/5/draft" -Method Post -ContentType 'application/json' -Body $payload5
try {
  Invoke-RestMethod "$base/$($study.id)/stages/5/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}' | Out-Null
  throw 'ERROR: stage5 approved without citation'
} catch {
  if($_.Exception.Message -like 'ERROR:*') { throw }
  Write-Host "STAGE5_NO_CITATION_BLOCKED=$([int]$_.Exception.Response.StatusCode)"
}
$fields5.verification="CONFIRMED BENCHMARK: Lean4 attempt $($record.verification.attemptId) outcome $($record.verification.outcome); this is a previously known theorem, not a new discovery."
$payload5=@{fields=$fields5}|ConvertTo-Json -Depth 7
$study=Invoke-RestMethod "$base/$($study.id)/stages/5/draft" -Method Post -ContentType 'application/json' -Body $payload5
$study=Invoke-RestMethod "$base/$($study.id)/stages/5/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}'
$paper=Invoke-WebRequest "$base/$($study.id)/paper?lang=en" -UseBasicParsing
$report=Invoke-WebRequest "$base/$($study.id)/reports/4?lang=es" -UseBasicParsing
Write-Host "FINISHED=$($study.activeStage) PAPER_HTTP=$($paper.StatusCode) PAPER_EVIDENCE=$($paper.Content.Contains($record.verification.attemptId)) REPORT4_EVIDENCE=$($report.Content.Contains($record.verification.attemptId))"
$reportEn=Invoke-WebRequest "$base/$($study.id)/reports/4?lang=en" -UseBasicParsing
$paperEs=Invoke-WebRequest "$base/$($study.id)/paper?lang=es" -UseBasicParsing
if(-not $report.Content.Contains('**RECHAZADO**') -or
   -not $report.Content.Contains('**VERIFICADO**') -or
   -not $report.Content.Contains('Lean rech') -or
   $report.Content.Contains("Tactic `'rfl`' failed")) {
  throw 'ERROR: Spanish report must explain Lean outcomes in Spanish, not raw English.'
}
if(-not $reportEn.Content.Contains('**REJECTED**') -or
   -not $reportEn.Content.Contains('**VERIFIED**') -or
   -not $reportEn.Content.Contains('Lean rejected')) {
  throw 'ERROR: English report must explain Lean outcomes in English.'
}
if(-not $paperEs.Content.Contains('**VERIFICADO**') -or
   -not $paperEs.Content.Contains('Generado en UTC:')) {
  throw 'ERROR: Spanish consolidated report was not localized.'
}
if(-not $report.Content.Contains($finiteId) -or
   -not $reportEn.Content.Contains($finiteId) -or
   -not $paper.Content.Contains($finiteId) -or
   -not $paperEs.Content.Contains($finiteId)) {
  throw 'ERROR: bounded finite evidence must appear in stage/paper reports in both languages'
}
if(-not $report.Content.Contains($rehearsal.cycleId) -or
   -not $reportEn.Content.Contains($rehearsal.cycleId) -or
   -not $paper.Content.Contains($rehearsal.cycleId) -or
   -not $paperEs.Content.Contains($rehearsal.cycleId) -or
   -not $report.Content.Contains('Ensayo del flujo científico SIN Muse Spark') -or
   -not $reportEn.Content.Contains('WITHOUT Muse Spark')) {
  throw 'ERROR: offline tool decisions must appear in both research report languages'
}
Write-Host 'BILINGUAL_REPORTS=ES_OK,EN_OK; CANONICAL_IDS_AND_HASHES_UNCHANGED=True; FINITE_EVIDENCE=ES_OK,EN_OK; OFFLINE_TRACE=ES_OK,EN_OK'
