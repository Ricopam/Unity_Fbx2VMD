# FBX2VMD E2E 테스트 도구

[프로젝트 요구사항](2026-09-24-FBX2VMD-E2E-테스트-적용-요구사항.md)을 이 폴더에서 관리한다. 이 폴더의 `boogle-sdk-0.1.0-beta.0.tgz`는 공용 SDK이며, FBX2VMD 테스트 설정과 시나리오는 별도로 작성한다.

## 첫 Unity 테스트 연결

Node.js 24와 `Assets/_Project/Tools/MainRecordingSettings`의 SDK 설치가 필요하다. 대상 프로젝트를 열고 있는 Unity Editor를 종료한 뒤 실행한다.

```powershell
node Tools/Boogle/run-unity-test.mjs "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
```

기본 실행 대상은 기존 `FbxPlaybackSmokeAutomationStoreTests`의 EditMode 테스트 한 건이다. 다른 기존 EditMode 테스트는 두 번째 인수에 테스트 전체 이름을 전달한다. SDK 실행 ID와 Unity 결과 요약은 `Docs/Workflow/Local/evidence/boogle/` 아래 봉인된다. 출력 JSON의 `runId`로 실행을 조회할 수 있다.

```powershell
cd Assets/_Project/Tools/MainRecordingSettings
npm run boogle -- detail f2f44dc8-83ef-46d0-9d26-b0e52d1c4d20 RUN_ID --data-root "D:\Project\02_Magical_Play\01_Develop\Unity_Fbx2VMD\Docs\Workflow\Local\evidence\boogle"
```

이 연결은 SDK와 기존 Unity 테스트의 연동 확인용이다. `F01`~`F15` 제품 E2E 판정은 요구사항 문서의 실제 자동 경로와 Game View 근거가 연결된 뒤 수행한다. SDK Unity 어댑터는 현재 원시 XML·에디터 로그를 저장하지 않고 결과 요약만 보존한다.

## 제품 2초 VMD smoke

Unity Editor에서 `Main_Auto`를 열고 Play 상태로 둔 뒤 실행한다. 이 요청은 기존 `FbxPlaybackSmokeRunner`의 `satisfaction_2.fbx` 자동 경로를 사용한다.

```powershell
node Tools/Boogle/run-product-smoke.mjs
```

`F06`의 `satisfaction_2.vmd` 동일 base name, 60프레임, VMD 헤더와 본 키 중복을 확인한다. SDK 결과와 bytes/frame metric은 위의 로컬 evidence 경로에, Unity 요청 상태와 신규 trace는 `product-smoke/<runId>/`에 남긴다. 기존 `satisfaction_2.vmd`와 `.meta`는 실행 전 백업하고 종료 후 복원한다. Editor가 준비되지 않으면 `BLOCKED`다. 실행 중 시간 초과가 발생하면 아직 쓰는 파일을 덮어쓰지 않도록 백업을 보존하고 로컬 manifest에 `restored: false`를 남긴다.

이 smoke의 `PASS`는 제품 E2E 전체 판정이 아니다. 프레임 탐색·하체 구간·의도적 실패는 아래 별도 명령으로 확인하며, 전체 모션·다른 모델 검증은 후속 항목이다. 제품 연결부는 현재 SDK `0.1.0-beta.0`의 `runWithAdapter`를 사용하므로 SDK 버전 변경 시 이 API 호환성을 확인해야 한다.

## 선택 전 상태 F01

새 `Main_Auto` Play 상태에서 FBX를 선택하기 전에 실행한다.

```powershell
node Tools/Boogle/run-product-smoke.mjs preselection
```

모델·Avatar·Plane·카메라·녹화 중지·4개 본의 0.5초 자세 드리프트를 검사하고 실제 Game View PNG와 상태 JSON을 `Docs/Workflow/Local/evidence/boogle/preselection/`에 보관한다. 구조와 캡처가 정상이면 SDK 상태는 `MANUAL_REVIEW_REQUIRED`이며, 기본 자세의 자연스러움은 화면 검토 전 자동 합격 처리하지 않는다. 비합격 상태는 종료 코드 1로 반환한다. Edit Mode 요청은 `BLOCKED`다.

이 폴더의 Markdown 문서는 로컬 전용이며 Git 추적 대상에서 제외된다.

## 전체 프레임 계측과 사람 표식

저장된 `Main_Auto` Edit 상태에서 `node Tools/Boogle/run-product-smoke.mjs full-clip`을 실행하면 `all-frames.csv`에 원본 FBX 발·발가락과 리타게팅 직후 대상 발·발가락의 높이·프레임 간 속도, 접지 평가 후 밑창·지지 수치가 같은 프레임으로 기록된다. 원본 FBX와 대상 모델의 좌표계·체형이 다르므로 높이를 직접 같은 물리 위치로 비교하지 않는다. 전체 프레임 CSV는 Game View 메시를 매 프레임 표시한 영상이 아니며, 화면 근거는 별도 캡처를 확인한다.

`Main_Recoding`을 포함한 하체 교차 검증은 저장된 대상 씬의 Edit 상태에서 `grounding-case`를 실행한다. 케이스 JSON에 `scene_path`, `fbx_file`(파일명), `model_name`(씬에서 고유한 Humanoid 루트), `model_asset_path`(해시를 남길 에셋 경로)를 지정한다. 예: `{"scene_path":"Assets/_Project/Scene/Main_Recoding.unity","fbx_file":"satisfaction_2.fbx","model_name":"YYB Hatsune Miku_default_1.0ver","model_asset_path":"Assets/_Project/Model/YYB Hatsune Miku_default/YYB Hatsune Miku_default_1.0ver.fbx"}`. 실행 명령은 `node Tools/Boogle/run-product-smoke.mjs grounding-case Docs/Workflow/Local/runtime/grounding-case.json`이다. 결과는 기존 `full-clip/<runId>/<requestId>/`와 `full-clip-runs/<runId>/`에 남고, Play 전후의 모델·씬 상태를 비교한다. Native 준비에 막힌 모델의 접지 진단에만 `"lower_body_only":true`를 추가한다. 이때 실행 매니페스트의 `lowerBodyDirectAssessment`가 `DIAGNOSTIC_ONLY`이며 실제 재생의 합격 근거가 아니다. 기본 실행의 `MANUAL_REVIEW_REQUIRED`도 계측 완료일 뿐 화면 품질 합격이 아니다.

같은 케이스에 `"segments":[[216,238],[3858,3870]]`처럼 서로 겹치지 않는 두 구간을 넣고 `grounding-live`로 실행하면 0프레임부터 연속 재생하며 각 구간 직전부터 Game View MP4를 기록한다. 예: `node Tools/Boogle/run-product-smoke.mjs grounding-live Docs/Workflow/Local/runtime/grounding-live-main-recoding-tetoris-yyb.json`. 실시간 렌더 누락 프레임은 `state.json`의 `missing_frames`에 기록된다. `lower_body_only`는 허용하지 않으며 영상과 정수 프레임 계측의 일대일 대응은 검증 전이므로 결과는 사람 검토 대상으로 남는다.

`playback`, `foot-live`, `full-clip` 실행 폴더의 `human-labels.csv`는 사람이 원본 모션을 먼저 보고 작성한다. `human-labels-template.csv`는 구간 확인용이므로 수정하지 않는다. 표식 완료 후 아래 명령으로 구간·양발·허용 표식·검토자 누락을 확인한다. 구조가 유효해도 원본 관찰 여부와 접지 의도는 자동 합격되지 않는다.

51열 전체 계측이 완료되면 저장된 CSV만으로 원본 지지·자유발 후보, 불확실 구간, 전환점, 최종 결과와 어긋난 프레임을 추린다. 사람 표식과 Jev API 키는 필요하지 않다.

```powershell
node Tools/Boogle/analyze-contact-events.mjs "Docs/Workflow/Local/evidence/boogle/full-clip/RUN_ID/REQUEST_ID"
```

같은 로컬 실행 폴더에 `contact-events.json`을 기록한다. `full-clip` 외에도 허용된 증거 패밀리인 `full-clip-f14`(하체 전용 F14 경로)와 `vrm-character`의 `<RUN_ID>/<REQUEST_ID>` 폴더도 분석할 수 있고, 각 패밀리의 실행 매니페스트(`full-clip-runs`, `alternate-model-runs`, `vrm-character-runs`)와 경로가 맞지 않으면 거부한다. `review_queue`는 우선 검토할 사건 ID이며 원본 바닥 높이와 물리 이상 임계값은 임시 후보 기준이다. 원본의 XZ·압력 정보가 없어 고정·의도된 이동과 앞꿈치·뒤꿈치 접촉을 확정하지 않는다. 결과 `MANUAL_REVIEW_REQUIRED`는 화면·의도 확인 전의 상태이며 품질 합격을 뜻하지 않는다. `790` 같은 기존 시각 회귀 프레임은 사건이 없어도 다음 국소 캡처에서 별도 확인한다.

```powershell
node Tools/Boogle/validate-human-labels.mjs "Docs/Workflow/Local/evidence/boogle/full-clip/RUN_ID/REQUEST_ID/human-labels.csv"
```

Jev 보조 검토는 새 47열 `full-clip` 계측이 끝난 뒤 별도 명령으로 실행한다. `TYPESAFE_API_KEY`가 없으면 외부 전송 없이 애매한 구간의 영어 요청 초안만 `jev-review.json`에 남긴다. 환경변수에 키가 있는 상태로 명령을 실행하면 구간당 원본 계측 1회, 예비 판정과 최종 지지 역할이 다를 때만 단계별 계측 1회를 Jev API에 보낸다. 키를 파일이나 Git에 저장하지 않는다.

```powershell
node Tools/Boogle/review-contact-intent.mjs "Docs/Workflow/Local/evidence/boogle/full-clip/RUN_ID/REQUEST_ID"
```

결과는 같은 로컬 폴더의 `jev-review.json`·`jev-review.csv`에 저장한다. Jev 응답은 검토 순서 제안이며 E2E 합격이나 제품 보정값을 바꾸지 않는다. 기존 39열 계측은 원본 속도 값이 없어 `BLOCKED`로 반환한다.

## 가져오기·재생·하체 구간 F02~F05

새 `Main_Auto` Play 상태에서 실행한다. 전체 모션의 팔 표면 사전 계산이 끝날 때까지 기다리므로 실행에 10분 이상 걸릴 수 있다.

```powershell
node Tools/Boogle/run-product-smoke.mjs playback
```

선택 파일·모델·Avatar·클립·임포트 설정과 단계 로그, 재생→일시정지→166·544·166프레임 재탐색→재개를 기록한다. 기본 30프레임에서 양발 지지 가중치·접점·밑창 거리·발목/발가락 회전·골반/무릎 위치와 Game View를 수집한다. 0·166·544는 측면 화면도 저장한다. JSON·CSV·PNG는 `Docs/Workflow/Local/evidence/boogle/playback/<requestId>/`에 남긴다. `frame-map.csv`의 VMD 프레임은 30fps 기준 예상값이며 실제 VMD 출력 프레임의 관측값은 아니다. `human-labels.csv`에는 원본 모션을 먼저 보고 각 구간·발의 접촉 형태(`공중/앞꿈치/뒤꿈치/발 전체/불확실`)와 이동 형태(`고정/구르기/의도된 이동/불확실`)를 사람이 적는다. 구조 검사가 통과해도 자세의 의도와 자연스러움은 `MANUAL_REVIEW_REQUIRED`다.

P1 사건 분석을 마친 47열 F10 실행 폴더로 P2 국소 캡처를 요청한다. 상위 사건과 166·544·790·1324~1332·8020의 연속 프레임, 정상 대조 후보를 한 번의 Play에서 수집한다.

```powershell
node Tools/Boogle/run-product-smoke.mjs contact-capture "Docs/Workflow/Local/evidence/boogle/full-clip/RUN_ID/REQUEST_ID"
```

원본·리타게팅 CSV 행, 접지 안정화 전후 본 배열, 최종 밑창, Game View와 대표 측면 PNG의 인덱스는 원본 F10 폴더의 `contact-capture-<runId>.json`에 기록한다. 입력 FBX·모델·씬 해시가 바뀌면 `NOT_COMPARABLE`이다. 결과 `MANUAL_REVIEW_REQUIRED`는 접지 품질 합격이 아니다.

## 의도적 실패 F07

새 `Main_Auto` Play 상태에서 실행한다.

```powershell
node Tools/Boogle/run-product-smoke.mjs invalid-input
```

없는 FBX 경로와 무효 Avatar를 실제 자동 가져오기 경로에 넣어 제품 상태 `Failed`와 원본 오류를 수집한다. 두 실패가 예상대로 발생하면 테스트 결과는 `PASS`이며, 부분 상태와 로그는 `Docs/Workflow/Local/evidence/boogle/invalid-input/`에 보관한다. Avatar는 종료 시 복원한다.

## F01~F08 단일 실행과 환경 복원

Unity Editor에서 저장된 `Main_Auto`를 Edit 상태로 열고 실행한다.

```powershell
node Tools/Boogle/run-product-smoke.mjs suite
```

SDK 실행 ID 하나에서 Edit 상태를 기록한 뒤 Play 진입, F01 사전 상태, F07 의도적 실패, F02~F05 가져오기·재생·접지, F06 짧은 VMD, Play 종료를 순서대로 실행한다. `suite-runs/<runId>/manifest.json`에 전후 씬·모델·Avatar·Recorder·`Time.captureFramerate`와 기존 VMD·`.meta` 해시를 비교해 남긴다. 화면 자연스러움과 접지 의도는 여전히 `MANUAL_REVIEW_REQUIRED`이며, SDK 종료 코드 1이 정상적인 수동 검토 상태다.

정상·F07 실패 경로의 Edit 복원은 실제 통합 실행으로 확인했다. 시간 초과 뒤에는 같은 요청의 종료를 최대 3분 더 기다려 자동 복원을 시도한다. `restored: false`가 남으면 Unity 작업이 끝난 뒤 다음 명령으로 같은 실행 ID를 복구한다. 늦게 생성된 VMD는 실행 폴더에 보관하고 원본 VMD·`.meta`를 복원하며, 최초 `TIMED_OUT` 판정은 유지한다. 실행 중인 요청이나 다른 요청이 있으면 복원하지 않는다. 시간 초과 복구의 실제 Unity 재현 검증은 아직 남아 있다.

```powershell
node Tools/Boogle/run-product-smoke.mjs recover <runId>
```
