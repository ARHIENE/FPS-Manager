# 프로젝트 로그

## 개요
- FPS Manager: FM(풋볼매니저)류 팀 운영 루프 + AI가 대신 싸우는 5v5 FPS 이스포츠 매니지먼트 게임
- 프로젝트 루트(작업 디렉토리): `E:\Git\Fps Manager`
- Unity 6000.5.8f1, URP(Universal Render Pipeline)
- 버전관리: Git/GitHub (`github.com/ARHIENE/FPS-Manager`, public)
- 기획 원본: Notion "게임 제작기 > 기획(스펙 문서)" 페이지 참고

## Git 브랜치 전략 (1VS1 Game과 동일)
- `master`: 실제 작업 브랜치. 프로젝트 전체 파일 포함, **README.md 없음**
- `main`: GitHub 기본 브랜치. **README.md만 관리**(프로젝트 전체 파일 없음), 저장소 메인 페이지 노출용
- 두 브랜치는 공통 조상이 없는 별개 히스토리
- **SAVE 명령 시 main의 README.md도 그날 작업 반영해 최신화할 것** (전역 CLAUDE.md 규칙)

## SAVE 시 Notion 연동
- Notion "게임 제작기"(https://app.notion.com/p/334c4a0ecd3180668dbdc7d6d1aed848) 하위 "개발 일지" 페이지(https://app.notion.com/p/334c4a0ecd3181778dcaf0e6a8d57040) 아래 날짜별 하위 페이지 생성
- 형식: 제목은 날짜만(`YYYY-MM-DD`), 이모티콘은 페이지 아이콘으로만(오늘 작업 난이도/분위기 표현), 내용은 `1. 대제목` + 아래 들여쓴 상세설명
- 상세 규칙은 전역 CLAUDE.md SAVE 절차 7번 참고
- **ML-Agents 전용 페이지**(https://app.notion.com/p/3cbc4a0ecd3180428949e9aac23767a9): SAVE 시 이 세션에서 ML-Agents 작업이 있었다면 위 개발일지와 별도로 이 페이지에 진행 상황 정리. 형식은 개발일지와 동일(이모티콘/날짜/`1. 대제목` 구조)하되 내용은 진행률(현재 스텝/목표 스텝), 성능 체감(평균 보상 추이, 관찰된 행동 변화), 이번 세션에 바뀐 보상/관측/설정 위주로 정리
- **스크린샷 첨부**: unity-cli(`E:\Git\_tools\unity-cli`, 로컬 패치본)로 Unity 에디터 Game/Scene 뷰 스크린샷 캡처 가능(`capture_screenshot` 툴, `.unity/capture/`에 저장, git에는 안 올림) — 개발일지/ML-Agents 페이지 작성 시 관련 스크린샷을 같이 캡처해서 첨부할 것

## 기획 요약 (Notion 기획서 기준)
- 플레이어는 직접 조작하지 않음 — 선수 영입/훈련 후 AI가 5v5 FPS 경기를 대신 치름
- 핵심 루프: 영입/스카우팅 → 훈련/성장 → 경기 시뮬레이션(관전) → 결과/성장 반영
- 선수 스탯(1차 범위): 에임/반응속도/클러치/포지셔닝/팀워크 — **클러치/포지셔닝만 최소 구현**(`PlayerCombatStats`), 나머지 3개는 아직 미구현
- 관전은 매크로(탑뷰) + FPS 1인칭 두 시점 모두 필요(기획서 5번) — 현재는 정식 아키텍처 아닌 임시 관전 카메라만 존재
- 개발 우선순위는 원래 ①시뮬레이션 코어 → ②매크로 뷰 → ③FPS 1인칭 뷰였으나, **AI가 실제로 움직이며 싸우는 전투 자체를 먼저 구현**하기로 함(매니저 메타 루프·스탯 시스템은 이후 단계)

## 5v5 AI 배틀 — 현재 상태 (2026-08-29 기준)
대상 씬: `Assets/Scenes/AIBattle5v5.unity`. 스크립트: `Assets/Scripts/Battle/`

### 핸드튜닝 AI 계층 구조 (`AIBrain` 기반, 기본으로 켜져 있음)
- **`AIBrain`** — 상위 결정 레이어. 적 탐색, 정찰↔교전 전환, 교전 자세 3종(정지사격/스트레이프/엄폐 이동, 이미 도착한 엄폐물은 재선택 안 함), 시야 순간 끊김 시 잠깐 마지막 위치 유지(`targetMemoryDuration`), 피격 반응 4종 총괄
- **`MovementStepSelector`** — 이동 전담. 스트레이프에 전진/후퇴 변주, 정찰 목적지를 엄폐물 순찰로 다양화. 앉기/뛰기 속도 배율과 회피용 홉(`TriggerEvadeHop`) 기능은 구현됐지만 **AIBrain에는 아직 연동 안 함**(판단 로직 없음, 필요시 추가 작업)
- **`CombatReactionEvaluator`** — 피격 반응 Utility AI 스코어링(반격/엄폐 이탈/후퇴/역공격)
- **`PlayerCombatStats`** — 클러치/포지셔닝만 구현
- **`WeaponController`** — 부위별 데미지, 트레이서는 이제 자기 자신(`TracerFader`)이 페이드/삭제해서 안 남음, 명중률 집계(`TotalShotsFired`/`TotalHits`/`AccuracyPercent`) 추가 → 라운드 종료 시 `MatchManager`가 콘솔에 로그
- **`MatchManager`** — 5v5 스폰, 라운드제
- **`ArenaGenerator`** — 엄폐물 180도 대칭 배치(52x52 맵, 24개), NavMesh 런타임 베이크

### ML-Agents 실험 (`CombatMLAgent`, AIBrain 대체용, 기본은 꺼져 있음)
`Assets/Scripts/Battle/CombatMLAgent.cs` — AIBrain(핸드튜닝)을 대체하는 학습형 전투 에이전트. **AIBrain.cs는 삭제 안 하고 유지**, `CombatMLAgent`가 붙으면 `Awake()`에서 자동으로 AIBrain을 비활성화. TeamA/TeamB 프리팹 둘 다 `BehaviorParameters`+`CombatMLAgent`+`DecisionRequester` 부착됨(`BehaviorName: CombatAgent`, 16개 관측/연속4+이산1 행동).

**학습 환경**: `MLAgentsTraining/`(git에는 `trainer_config.yaml`만 추적, `venv/`·`results/`는 gitignore). Python 3.10.11 + mlagents 1.1.0 + torch 2.2.2(onnxscript 미설치 버전 — 최신 torch는 체크포인트 export 시 onnxscript 없어서 에러남, 반드시 2.2.2 유지). PPO, `max_steps: 2000000`.

**3차례 학습 진행, 아직 목표(명중률 40%+) 미달성**:
- v1: `Agent.MaxStep` 기본값(무제한)+라운드 시간제한 없음 → 에피소드가 안 끝나서 폐기
- v2: MaxStep=3000으로 고정 + torch 다운그레이드로 체크포인트 저장 버그 해결, 완주(2M 스텝)했지만 최종 평균 보상 여전히 마이너스(-0.45), 실측 명중률 **0.7%**(34/4669, 58/8237)
- v3: 조준 정렬 보상 + 엄폐물 관측/보상 + 헤드샷 보너스 추가, 적 안 보이면 발사 자체를 하드 게이팅(난사 방지). 최종 평균 보상은 크게 개선(+2.9~3.8)됐지만 **명중률은 오히려 더 낮음(0.5~1.2%)**, 하늘만 쏘는 등 조준 자체가 안 됨. 원인 진단 완료(아래 참고), 재학습 필요

**v3에서 발견한 버그 2개 (다음 세션에 고칠 것)**:
1. **보상 설계(reward hacking)**: 조준 정렬 보상(`aimRewardScale`)이 매 스텝 누적되는 방식이라, 실제로 맞히지 않고 그냥 적을 쳐다보고만 있어도 한 에피소드 동안 킬 보상(+1)에 맞먹는 보상이 쌓임 → 명중보다 "쳐다보기"만 학습해버림. 조준/거리/엄폐 보상 스케일을 지금보다 훨씬 작게(예: 5~10배 축소) 낮춰서 명중/킬 보상이 확실히 우세하도록 재조정 필요
2. **피치(상하) 조준 관측 누락**: 좌우(yaw)는 `dirX/dirZ`로 "어느 쪽으로 돌려야 하는지" 방향 정보를 줬는데, 상하(pitch)는 정렬도 스칼라값(`aimDot`) 하나만 줘서 "얼마나 틀렸는지"는 알아도 "위/아래 어느 쪽으로 고쳐야 하는지" 방향 정보가 없음 → 피치 제어를 못 배우고 하늘 등 엉뚱한 방향에 고정됨. 명시적인 수직 방향 관측(예: 목표까지의 수직 각도/오프셋) 추가 필요

**모델 결과물**: `Assets/ML-Agents/Models/CombatAgent.onnx`(v2), `CombatAgent_v3.onnx`(v3) — Unity 에셋으로 git 추적됨.

## unity-cli (Claude Code의 Unity 에디터 직접 조작)
- **로컬 패치본 사용 중**: 공식 `akiojin/unity-cli-bridge` 패키지는 Unity 6000.5의 `GetInstanceID()` deprecation 때문에 컴파일 안 됨(업스트림 [이슈 #231](https://github.com/akiojin/unity-cli/issues/231) 미해결) → `E:\Git\_tools\unity-cli`에 클론해서 7군데 `GetInstanceID()`→`GetEntityId().GetHashCode()`로 직접 패치, `Packages/manifest.json`에서 `file:` 경로로 참조
- **포트 16400 사용** (기본값 6400은 이 PC에서 무관한 다른 프로그램과 충돌해서 `ProjectSettings/UnityCliBridgeSettings.asset`로 변경) — CLI 사용 시 `UNITY_CLI_PORT=16400` 환경변수 필요
- **CLI 바이너리**: `~/.local/bin/unity-cli.exe`, 사용 시 `UNITY_PROJECT_ROOT`도 프로젝트 경로로 설정
- **알려진 불안정 이슈**: 브릿지가 Play 모드 진입 후 자주(때때로) 응답 없이 멈춤 — 원인 미해결. 대응법: (1) Unity 창에 포커스 주고 Enter 키 입력하면 풀리는 경우 많음 (2) 그래도 안 풀리면 Unity 프로세스 강제 종료 후 재시작(강제종료 자체는 데이터 손실 없음, 코드/프리팹은 이미 디스크에 저장돼 있음 — 단, 비정상 종료로 인식돼서 다음 실행 때 Library 캐시가 깨져 전체 재임포트가 도는 경우가 있어 시간이 오래 걸릴 수 있음) (3) 브릿지가 죽어도 `Logs/Editor.log`를 직접 읽으면 콘솔 로그는 계속 확인 가능

## Unity AI 활용 방침
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝하는 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
- **(신설)** unity-cli 도입 후로는 Claude Code가 Play 진입/콘솔 확인/프리팹 편집/스크린샷까지 직접 할 수 있게 됨 — 다만 위 불안정 이슈 때문에 여전히 사용자 확인이 필요할 때가 있음

## 다음 세션 확인/할 일
- **ML-Agents 재학습**: 위 "v3에서 발견한 버그 2개" 고치고 재학습 (`combat_v4` 등으로) → 명중률 재측정
- 앉기/뛰기/옆걸음/회피 홉을 `AIBrain`(핸드튜닝) 판단 로직에 연동할지 결정 필요(지금은 기능만 있고 아무도 안 씀)
- `HumanoidBattleAnimator`의 다리 애니메이션 방향 인지는 이번 세션에 일부 개선(전진/좌우 블렌딩) — 추가 다듬기 필요할 수 있음
- 밸런스 튜닝값(엄폐 활용 확률, 정찰 순찰 확률 등) 전부 임시값 — Play해보고 체감 튜닝 필요
- 아직 미구현: 에임/반응속도/팀워크 스탯, 매크로 관전 뷰, 매니저 메타 루프(영입/훈련/UI)
