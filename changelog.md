# 변경 이력

## 2026-08-28 — 배틀로얄 전환(4인×20팀) + 자기장 시스템
- (세션 초반) `ArenaGenerator` 엄폐물 배치를 180도 회전 대칭으로 변경(한쪽에 배치한 위치를 원점 기준 180도 회전해 반대쪽에 그대로 미러링, 회전값도 +180도) — 이후 배틀로얄 전환으로 `ArenaGenerator` 자체는 미사용 상태가 됨(코드는 삭제하지 않고 유지)
- **배틀로얄 모드 전환**(4인 팀 × 20팀, 총 80명). 파밍/자기장/차량/부활은 이번 요청서 범위에서 제외하고 전투·이동·성능 먼저 구현. 기존 5v5 구조(`MatchManager` 등)는 "완전 전환" 방식 선택(사용자 확인) — `teamASpawns` 등 옛 필드를 참조하던 씬 인스펙터 값은 소실되어 재배선 필요했음, `AIBattle5v5.unity` 씬을 그대로 재활용
  - `TerrainGenerator` 신규(펄린 노이즈 랜덤 지형, 사용자 첨부 스크립트 기반) — 생성 직후 NavMesh 자동 베이크 → `MatchManager.BeginMatch()` 순서로 연동. 엄폐물(`SpawnObject.isCover`)엔 Cover 태그 + 렌더러 바운드 기반 NavMeshObstacle 자동 부착
  - `BattleRoyaleSpawner` 신규 — 맵 위에 20개 팀 클러스터를 최소 거리 제약으로 랜덤 배치, 클러스터 내 4명 분산, 전부 `NavMesh.SamplePosition`으로 검증
  - `MatchManager` 전면 재작성 — `teamA/teamB` 고정 2팀 → `List<BattleRoyaleTeam>`(N팀). 팀 전멸 시 `OnTeamEliminated`, 최후 1팀 생존 시 `OnMatchEnded`(라운드/스코어 개념 제거). 80명 규모 성능 대응으로 **공간 분할 그리드**(20m 셀, 0.15초 주기 재구성) 도입, `GetNearbyEnemies`로 AI 탐지 후보를 주변 셀로만 제한
  - `AIBrain` — 적 탐지를 그리드 기반으로 전환 + 탐지 스캔 자체를 매 프레임이 아닌 `detectInterval`(0.15초, 개체별 랜덤 오프셋) 주기로 스로틀링. 로밍/후퇴 목적지도 팀 스폰 대신 `MatchManager.GetWanderPoint`/`GetTeamSpawnCenter` 기반으로 재계산
  - `SpectatorCamera` — 숫자키 1~0(최대 10명) 빙의 → **Tab(팀 순환, 생존자 우선) + ←→(팀원 순환)** 2단계 방식으로 변경(20팀 대응)
  - `BattleHUD` — 2팀 스코어 패널 → "N/20 TEAMS LEFT" 요약 + 팀 탈락 킬피드 로그로 개편
- **지형 랜덤 시드**: `TerrainGenerator.randomizeSeedOnStart`(기본 켜짐) 추가 — Play할 때마다 `xOffset`/`zOffset`을 새로 뽑아 지형·오브젝트 배치가 매번 달라지도록 함(이전엔 고정 시드라 지형/오브젝트 배치가 항상 동일했고, 스폰 위치만 진짜 랜덤이었음)
- **팀별 캐릭터 색상**: `TeamColorApplier` 신규(MaterialPropertyBlock 기반, 머티리얼 인스턴스 생성 안 해 80명 규모에도 가벼움) — 스폰 시 `MatchManager.GenerateTeamColor`(20팀 hue 균등 분배, 채도/명도 고정이라 겹치는 색 없음)로 계산한 팀 색을 캐릭터에 적용. 이후 사용자가 `HumanoidBattleAnimator.SetTeamColor` 연동을 직접 추가해 보강(디스크 변경 확인, 정상 반영)
- **자기장(세이프존) 시스템** 추가 (PUBG 참고):
  - `ZoneManager` 신규 — 6단계 기본 단계표(대기/축소시간, 축소비율, 데미지가 뒤로 갈수록 커짐), 다음 원은 항상 이전 원 안에서 랜덤 위치 선정. `MatchManager.OnMatchStarted` 이벤트를 구독해 매치 시작마다 자동 재시작(직접 결합 없이 이벤트로 연동). 시각화는 반투명 실린더(현재 원, 스케일 갱신)+흰색 LineRenderer 링(다음 원 예고)
  - `CombatReactionEvaluator.Context.zoneUrgency` 추가 — 자기장 밖 위급도(0~1)를 `ScoreFight -= urgency*1.2`/`ScoreRetreat += urgency*1.5`로 기존 Utility 스코어링에 자연스럽게 반영(하드 전환 아님)
  - `AIBrain.ComputeZoneUrgency()` — 체력/클러치/현재 단계 데미지로 위급도 산출, 임계치 근처에서 확률적으로(부드러운 램프, 하드컷 아님) 교전 중이라도 자기장 이동을 우선하도록 강제 전환. 로밍 반경/후퇴 목적지도 자기장 인지형으로 변경
  - `MatchManager.GetAllPlayers()` 추가, 자기장으로 사망(`killer == null`) 시 킬피드에 "☣ 자기장으로 사망" 별도 문구로 분기
  - `BattleHUD`에 자기장 단계/다음 축소까지 남은 시간 패널, 관전 중인 플레이어가 자기장 밖이면 경고 표시 추가
  - Play 테스트 결과(Editor.log 직접 확인): 컴파일 정상, 80명 스폰·전투·팀 탈락(20→10팀 관찰) 정상 동작, 런타임 예외 0건. 이 세션 도중엔 `ZoneManager`가 씬에 아직 안 붙어있어 자기장 자체는 미검증이었으나, 세션 종료 시점에 사용자가 Unity AI로 `Terrain` GameObject에 `ZoneManager` 부착 + `ZoneWallMat` 머티리얼(Transparent, Render Face Both) 연결까지 완료한 것을 씬 파일에서 직접 확인함(다음 세션에서 실제 Play 확인 필요)
- **Notion 기획 문서 재구성**: "기획(스펙 문서)"를 8개 파트 하위 페이지로 분리하는 과정에서 `replace_content`+`allow_deleting_content:true` 실수로 새로 만든 하위 페이지 8개가 전부 휴지통으로 이동 → 동일 내용으로 재생성해 복구(이후부턴 하위 페이지 있는 곳엔 `update_content` 부분 치환만 사용하기로 함). 배틀로얄 전환 이후 사용자가 "스펙 바뀌면 추가 말고 교체" 규칙을 전역 CLAUDE.md에 신설 → 1/5/7번 파트의 "5v5"/"2팀" 서술 교체, "9. 배틀로얄 모드" 파트 신규 추가

## 2026-08-26 — 프로젝트 생성
- FPS Manager: Unity 신규 프로젝트 생성
- Unity 6000.5.8f1, URP(Universal Render Pipeline) 3D 템플릿으로 시작
- 버전관리: 원래 Plastic SCM으로 초기화되어 있었으나 Git으로 전환 (`github.com/ARHIENE/FPS-Manager`, public 예정)
- 상태: Unity 3D(URP) 기본 템플릿 그대로인 초기 상태(SampleScene 외 커스텀 스크립트/씬 없음), 게임 기획/구체적 기능 미정
- 같은 날 Notion 연동 설정: SAVE 시 Notion "게임 제작기 > 개발 일지"에 날짜별 페이지로 작업 내용을 기록하는 규칙 도입(전역 CLAUDE.md 반영)

## 2026-08-27 — 5v5 AI 배틀 마일스톤 1 + AI 전투 행동 결정 레이어
- 1VS1 Game의 FPS 미니게임 스크립트를 Photon 제거 + NavMeshAgent 기반으로 포팅해 5v5 AI 배틀 마일스톤 1 구현
  (`PlayerMovement`/`WeaponController`/`PlayerHealth`/`AIBrain`/`MatchManager`/`ArenaGenerator`, `AIBattle5v5` 씬, 팀별 캐릭터 프리팹)
- 헤드샷 즉사(1VS1 원본) 로직 제거 → 부위(머리/몸통)별 누적 데미지로 변경
- `NavMeshAgent.stoppingDistance`를 정찰/교전 상태별로 분리해 정찰 중 멈춰버리는 버그 수정
- 사용자가 Unity 에디터 + Unity AI로 `HumanoidBattleAnimator`(절차적 뼈대 애니메이션), `BattleHUD`(OnGUI 스코어보드/킬피드/라운드 배너), 캐릭터 모델(Kevin Iglesias Human Character Dummy)/머티리얼 직접 추가
- "교전 중 무빙이 단조롭다"는 피드백에 따라 좌우 스트레이프 + 페이크(주크) 무빙 1차 구현
- 이어서 "AI 전투 행동 결정 레이어" 요청으로 대규모 리팩터링 진행:
  - `MovementStepSelector` 신설 — "어떻게 움직이는가" 전담, 스트레이프 로직 이관, 정지사격↔이동 전환을 블렌딩으로 부드럽게 처리
  - `CombatReactionEvaluator` 신설 — 피격 시 반격/엄폐 이탈/완전 후퇴/역공격 4종에 대한 Utility AI 스코어링, 상위 2개 후보 중 점수 비례 확률로 선택(예측 불가능성 확보)
  - `PlayerCombatStats` 신설 — 클러치/포지셔닝(0~1) 최소 스탯만 구현(나머지 스탯은 추후 매니저 메타 루프에서 확장)
  - `PlayerHealth`에 `OnDamaged`(비치명타 피격) 이벤트 추가
  - `Cover` 태그 신규 등록(`TagManager.asset`) + `Cover.prefab`에 적용 — 엄폐물 탐색용
  - `AIBrain`을 "교전 중 정지사격 우선(확률 기반 planted/strafe 전환) + 피격 반응 총괄"하는 상위 결정 레이어로 재구성(클래스명은 기존 프리팹 참조 유지를 위해 `AIBrain` 그대로 사용, 요청서의 `UtilityAIBrain`으로 개명하지 않음)
- 참고: `MovementStepSelector`/`PlayerCombatStats`는 `AIBrain.Awake()`에서 없으면 런타임에 자동 부착됨 — 프리팹에 직접 붙어있지 않아도 Play 시 정상 동작(단, 인스펙터에서 개별 수치를 프리팹에 고정하고 싶다면 에디터에서 수동으로 부착 필요)
