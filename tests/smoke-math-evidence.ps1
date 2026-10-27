param([string]$BaseUrl = 'http://127.0.0.1:5392')
$ErrorActionPreference='Stop'
$base=$BaseUrl.TrimEnd('/') + '/api/research'
$stages=Invoke-RestMethod "$base/stages"
$study=Invoke-RestMethod $base -Method Post -ContentType 'application/json' -Body '{"title":"CONTROL STUDY: report gates and real Lean artifacts","discipline":"Mathematics"}'
Write-Host "PROJECT=$($study.id)"
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
  $payload=@{fields=$fields}|ConvertTo-Json -Depth 6
  $study=Invoke-RestMethod "$base/$($study.id)/stages/$step/draft" -Method Post -ContentType 'application/json' -Body $payload
  $study=Invoke-RestMethod "$base/$($study.id)/stages/$step/approve" -Method Post -ContentType 'application/json' -Body '{"reviewedByHuman":true}'
}
Write-Host "PREPARED_PHASE=$($study.activeStage)"
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
