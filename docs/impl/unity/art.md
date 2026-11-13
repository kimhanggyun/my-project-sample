# 임시 3D 아트 연결

> 상태: 연결 구현 (2026-11-09). 모델 파일은 저장소에 없다 — PC의 Unity에서 무료(CC0) 에셋을 넣는다. 최종 아트는 나중에 교체.
> 관련: [architecture.md](architecture.md) 0.6(3D 전장), [decisions.md](decisions.md)(3D 아트 방향)

## 1. 어떻게 동작하나

- 아트 목록 `unity/Assets/Game/Resources/Art/catalog.json`: 키 → 모델 경로(Resources 아래, 확장자 없이)·목표 높이·방향.
- `Scripts/Battle3D/ArtCatalog`가 목록을 읽고 모델(FBX·프리팹)의 **첫 메시**와 재질을 꺼낸다.
  - 크기는 목표 높이(`height`, 전장 1칸 = 1)로 맞추고, 바닥을 땅에, 가운데를 원점에 둔다 → 에셋마다 단위·원점이 달라도 그대로 쓴다.
  - 재질은 인스턴싱을 켠 사본. 셰이더를 쓸 수 없으면(분홍) 기본 재질(GameLit)에 텍스처·색만 옮긴다.
- **여러 조각·뼈대 모델** (2026-11-13): 처음 쓸 때 **메시 하나로 굳힌다**. 보통 메시(MeshFilter)와 뼈대 메시(SkinnedMeshRenderer)를 모두 모으고, 뼈대 메시는 **Idle 동작 첫 프레임 자세**로 `BakeMesh`, 뼈에 붙은 무기·방패 같은 자식 메시까지 모델 뿌리 기준으로 합친다. 재질이 같은 조각은 한 하위 메시로 묶는다. → KayKit 캐릭터 FBX를 그대로 넣어도 된다 (OBJ로 합칠 필요 없음).
  - 자세 동작 찾기: `catalog.json` 항목의 `poseFrom`(동작이 든 파일, Resources 경로, 예: KayKit 리그 FBX) 안에서 이름에 `poseClip`(기본 `"idle"`)이 들어간 동작 → 없으면 모델 파일 안의 동작 → 그것도 없으면 기본 자세. 일반(Generic) 리그 기준 (휴머노이드 동작은 안 먹을 수 있음 — 그때는 FBX Rig를 Generic으로).
  - 예: `"general.default": { "model": "Art/Models/general", "poseFrom": "Art/Models/Anim/Rig_Medium_General", "poseClip": "Idle", "height": 0.8 }`
- **모델 파일이 없으면 코드로 만든 병종 인형**(`Battle3D/SoldierMeshes`, 2026-11-11: 머리·몸통 + 보병 방패·칼 / 창병 긴 창 / 궁병 활·화살통 / 기병 말·창 / 정찰병 짧은 칼, 부위별 색: 갑옷·투구·방패 = 팀 색, 얼굴 = 살색, 무기 = 쇠색, 다리·말 = 갈색 — 하위 메시 4개 + 재질 4개)으로 그린다. 진짜 스킨(텍스처·모델)은 아직 없다: 무료 CC0 모델을 넣거나(2장), 수묵 아트 단계에서 바꾼다. 소품은 없음. 모델을 하나씩 넣으면 그 병종부터 바뀐다.
- **장수** (2026-11-11, 부대원과 구별): 군단마다 진형 한가운데 조금 뒤에 장수 인형 (`SoldierMeshes.General`) — 부대원보다 약 1.8배, 흰 말, 금빛 투구·깃털·어깨·칼, 팀 색 망토. **총대장**은 등에 군기(금 깃대 + 팀 색 깃발)를 하나 더. 군단 깃발은 그대로. 모델 키는 나중에 `general.default` 등으로 바꿀 수 있게 한다.
- **쓰러진 병사** (2026-11-11): 병력이 줄어 화면 병사가 빠지면 그 자리에 어두운 납작한 상자가 전투 끝까지 남는다 (최대 4,000개). 부대가 전멸하면 남은 병사 모두.
- **화면 병사 예산** `balance.json` `display.soldierBudget` = 900 (2026-11-11, 300에서 올림: 손실이 눈에 보이게. 부대 하나 약 50명).
- 그리기는 모두 GPU 인스턴싱(`InstanceBatch`, 1023개씩). 규칙 계산과 무관 (보이기 전용).

| 키 | 쓰는 곳 | 비고 |
|---|---|---|
| `soldier.<병종 id>` | 그 병종의 병사 (infantry, spearman, archer, cavalry, scout) | 없으면 `soldier.default` → 상자. `tint` = 팀 색 섞는 비율 (0~1) |
| `soldier.default` | 모델이 없는 병종 | |
| `prop.<지형 id>` | 그 지형 칸마다 소품 (forest, burnt, grass, hill, mountain, marsh, building …) | `models`로 변형 여러 개, `perCell` = 칸마다 수(소수면 확률), `jitter` = 크기 흔들림. 자리는 칸 번호로 정해져 늘 같다. 숲이 타면 `prop.burnt`로 바뀐다 |

- 병사가 맞을 때: 상자는 밝은 색으로, 모델은 살짝 튀어 오른다 (모델 색은 곱하기라 밝게 할 수 없음).
- 아직 없는 것: 깃발 모델, 걷기·공격 애니메이션(지금은 통째로 흔들기·찌르기), 장수 모델.

## 2. 넣는 방법 (PC의 Unity에서)

1. 무료 에셋을 내려받는다 (모두 CC0 = 출처 표시 없이 상업 이용 가능, 각 사이트에서 다시 확인):
   - **Kenney** (kenney.nl/assets): *Nature Kit*(나무·바위·풀·덤불), *Castle Kit*·*Fantasy Town Kit*(집·성벽), *Mini Characters* 또는 *Blocky Characters*(병사).
   - **Quaternius** (quaternius.com): *Ultimate Nature*, *Medieval Village*, 동물·기사 팩 (말 탄 병사 대용).
   - 애니메이션은 이후: **Mixamo**(무료, Adobe 계정) — 사람형 모델의 걷기·공격 동작.
2. 쓸 FBX(또는 GLB를 FBX로 바꾼 것)를 `unity/Assets/Game/Resources/Art/Models/` 에 복사하고 **목록의 이름으로 바꾼다** (예: 나무 → `tree_a.fbx`, `tree_b.fbx`, 병사 → `soldier.fbx`).
   - 이름을 바꾸기 싫으면 `catalog.json`의 경로를 그 파일 이름으로 고친다.
   - 텍스처(png)가 따로 있으면 같은 폴더에 함께 둔다.
3. Play → 출진 → [직접 지휘]. 모델이 없으면 콘솔에 "임시 아트 모델이 없어 단순 도형을 씁니다: …"가 한 번 뜬다.
4. 크기·방향이 어색하면 `height`·`yaw`(도)를 고친다. 병사가 뒤를 보면 `yaw: 180`.

| 목록 이름 | 넣을 것 (예) |
|---|---|
| `soldier` (기본), `soldier_infantry`, `soldier_spearman`, `soldier_archer`, `soldier_cavalry`, `soldier_scout` | 사람형 캐릭터 하나만 `soldier`로 넣어도 모든 병종이 그것을 쓴다 |
| `tree_a`, `tree_b`, `tree_burnt` | Nature Kit `tree_default`, `tree_pineRoundA`, 죽은 나무 |
| `rock_a`, `rock_b` | Nature Kit `rock_largeA`, `rock_tallA` |
| `grass`, `bush`, `reed` | Nature Kit 풀·덤불 |
| `house` | Town/Castle Kit 집 |
| `soldier_catapult` | 투석기 (공성 병기 하나, 바퀴 달린 나무 틀) |
| `soldier_engineer` | 공병 (도끼·삽 든 사람). 없으면 보병 모델 |
| `ship` | 배 (작은 돛배). 수군 4종(전투선·누선·충각선·쾌속선)이 모두 이것 |
| `general` | 장수 (말 탄 장수, 부대원보다 크게 보이게 `height` 0.8). 없으면 코드로 만든 장수 인형 |
| `general_commander` | 총대장 (등에 깃발 등). 없으면 `general` |

### 2.2 넣을 수 있는 모든 자리 (2026-11-13, 원격 정리)

> 파일만 넣으면 코드 수정 없이 바뀐다. 없으면 지금처럼 코드로 만든 임시 그림·소리.

| 자리 | 경로 (`unity/Assets/Game/Resources/` 아래) | 형식·조건 |
|---|---|---|
| 병사 (병종별) | `Art/Models/soldier*.fbx` (위 표) | 메시 하나, 삼각형 500 이하, 앞이 +Z(아니면 `yaw`) |
| 장수 | `Art/Models/general.fbx`, `general_commander.fbx` | 메시 하나, 삼각형 2,000 이하 |
| 배·투석기 | `Art/Models/ship.fbx`, `soldier_catapult.fbx` | 메시 하나 |
| 지형 소품 | `Art/Models/tree_a` … (위 표) | 메시 하나 |
| 장수 초상 | `Art/portraits/<장수 id>.png` | 정사각형 512px, Sprite (2D and UI). 장수 id 목록: `data/Themes/ancientwar/generals.json` |
| 화면 장식 | `Ui/paper.png`(한지 바탕, 이어 붙일 수 있게), `Ui/frame.png`(버튼·카드 테두리, 9-slice 경계 설정), `Ui/brush.png`(가로 붓 획), `Ui/seal.png`(낙관 네모), `Ui/enso.png`(초상 고리) | Sprite. 흰색·회색으로 그리면 코드가 색을 입힌다 |
| 효과음 | `Audio/<id>.wav` (ogg도 됨) | id: `click` `drum` `duel` `clash` `arrow` `horn` `gong` `fire` `cheer` `alert` `battle`(반복 전장 소음, 2~4초) |
| 글꼴 | `Fonts/title.otf`, `Fonts/body.ttf` | 이미 들어 있음 (본명조·Pretendard, OFL). 바꾸려면 같은 이름으로 |

### 2.1 장수 초상 (전투 정보 패널)

- `unity/Assets/Game/Resources/Art/portraits/<장수 id>.png` (예: `general_a.png`)를 넣고 Texture Type을 **Sprite (2D and UI)**로 둔다. 코드 수정은 필요 없다.
- 그림이 없으면 진영 색 원 안에 이름 첫 글자를 보여 준다 (`Portrait.cs`).

## 3. 주의

- 모델은 **메시 하나만** 쓴다 (여러 조각으로 된 모델은 첫 조각만). 조각이 여러 개면 Unity에서 하나로 합치거나 다른 모델을 고른다.
- 폰 성능: 병사 모델은 **삼각형 500개 이하**의 로우폴리를 고른다 (화면 병사 수백 명 × 모델).
- `Resources` 폴더의 파일은 모두 빌드에 들어간다. 쓰지 않는 모델은 넣지 않는다.
- 모델 파일은 저장소에 올려도 된다 (CC0). 올리면 라이선스 파일(License.txt)을 같은 폴더에 함께 둔다.

## 받은 에셋 목록

> 데스크탑 Claude가 에셋을 넣을 때마다 한 줄 추가한다 ([collab.md](../../collab.md) 6장). 이미지·모델은 LFS라 원격 Claude는 내용을 못 볼 수 있으니 **생김새**를 글로 적는다.

**2026-11-13 1차 (Kenney) → 같은 날 2차 (KayKit, 나무·바위·풀·집), 모두 CC0.** 모델 미리보기 그림(원격도 볼 수 있음): [docs/captures/models/](../../captures/models/) `units.png`·`props.png` (간이 렌더, 방향·조명은 실제와 다름). — 경로는 `Game/Resources/` 아래. 모델은 **OBJ**(FBX 아님): Kenney FBX 캐릭터는 뼈대(SkinnedMesh)라 `ArtCatalog`(MeshFilter만 읽음)가 못 쓰고, 여러 조각이라 첫 조각만 쓰이므로 OBJ 조각들을 **한 메시로 합쳤다**(무기도 손 위치에 붙여 합침). 각 `.obj` 옆에 같은 이름 `.mtl`, 텍스처는 `Art/Models/Textures/`. 모두 앞이 +Z로 가정 — 뒤를 보면 `catalog.json` `yaw: 180`.

| 경로 | 출처 | 삼각형 | 생김새·용도 |
|---|---|---|---|
| `Art/Models/soldier_infantry.obj` | [Blocky Characters](https://kenney.nl/assets/blocky-characters) `character-m` + [Mini Characters](https://kenney.nl/assets/mini-characters) `weapon-sword`·`shield-rectangle` | 500 | 마인크래프트풍 네모 사람(초록 웃옷·갈색 끈), 오른손 칼, 왼팔 앞 네모 방패. 보병 |
| `Art/Models/soldier_spearman.obj` | Blocky `character-a` + Mini `weapon-spear` | 188 | 네모 사람(맨살·수염), 오른손에 키보다 조금 짧은 창을 세워 듦. 창병 |
| `Art/Models/soldier_archer.obj` | Blocky `character-f` + Mini `weapon-bow` | 232 | 네모 사람(짙은 초록 옷), 왼손에 활. 궁병 |
| `Art/Models/soldier_scout.obj` | Blocky `character-r` + Mini `weapon-sword`(작게) | 232 | 검은 옷·복면(닌자풍) 네모 사람, 짧은 칼. 정찰병 |
| `Art/Models/soldier_engineer.obj` | Blocky `character-k` + [Pirate Kit](https://kenney.nl/assets/pirate-kit) `tool-shovel` | 408 | 붉은 웃옷 네모 사람, 오른손에 삽. 공병 |
| (기병·기본 병사 없음) | — | — | **일부러 비움**: `soldier_cavalry`·`soldier`가 없으면 코드로 만든 말 탄 인형을 그대로 쓴다 (네모 사람은 말이 없어 기병이 사람으로 보이게 됨) |
| `Art/Models/general.obj` | Mini `character-knight` + `weapon-sword` | 940 | 둥근 꼬마 기사(투구·갑옷, 회색·파랑 계열), 오른손 칼. 걸어 있는 모습(말 없음). 장수 |
| `Art/Models/general_commander.obj` | 위 기사 + [Castle Kit](https://kenney.nl/assets/castle-kit) `flag-pennant` | 1,176 | 장수와 같고 등 뒤에 삼각 깃발 깃대. 총대장 |
| `Art/Models/soldier_catapult.obj` | Castle Kit `siege-catapult` | 1,492 | 나무 바퀴 4개 달린 투석기, 던지는 팔 위로 |
| `Art/Models/ship.obj` | Pirate Kit `ship-small` | 2,384 | 돛대 하나·흰 돛의 작은 범선(서양 해적선풍). 길이가 Z축. 수군 4종 공통 |
| `Art/Models/tree_a.obj` | [KayKit Forest Nature Pack](https://kaylousberg.itch.io/kaykit-forest) `Tree_1_A` | 530 | **(2차, KayKit)** 굵고 짧은 주황 줄기 + 크고 둥근 연두 잎 덩어리 하나. 만화풍 |
| `Art/Models/tree_b.obj` | KayKit Forest `Tree_4_A` | 404 | **(2차)** 가는 주황 줄기 위 초록 원뿔 잎 2~3층 (소나무) |
| `Art/Models/tree_burnt.obj` | KayKit Forest `Tree_Bare_1_B` | 332 | **(2차)** 잎 없는 주황 고목, 위가 두 갈래 (탄 숲) |
| `Art/Models/rock_a.obj` | KayKit Forest `Rock_1_A` | 48 | **(2차)** 각진 회색 바위 두 덩이 (낮음) |
| `Art/Models/rock_b.obj` | KayKit Forest `Rock_1_J` | 521 | **(2차)** 회색 바위가 층층이 쌓여 솟은 봉우리 (산) |
| `Art/Models/grass.obj` | KayKit Forest `Grass_1_B` | 132 | **(2차)** 넓적한 연두 풀잎 2장 포기 |
| `Art/Models/bush.obj` | KayKit Forest `Bush_1_A` | 72 | **(2차)** 둥근 연두 덤불 덩어리 |
| `Art/Models/reed.obj` | KayKit Forest `Grass_2_C` | 528 | **(2차)** 가늘고 긴 연두 풀잎 여러 가닥 다발 (갈대) |
| `Art/Models/Textures/*.png` | 위 팩들의 텍스처 (`blocky_*`, `mini_colormap`, `castle_colormap`, `pirate_colormap`, `kaykit_forest`, `kaykit_medieval`) | — | 색 팔레트 그림 |
| `Art/Models/house.obj` | [KayKit Medieval Hexagon Pack](https://kaylousberg.itch.io/kaykit-medieval-hexagon) `building_home_A_red` | 1,011 | **(2차)** 붉은 지붕·흰 벽·나무 기둥의 작은 집, 회색 굴뚝. 귀여운 만화풍 |
| `Audio/click.ogg` | [Interface Sounds](https://kenney.nl/assets/interface-sounds) `click_001` | — | 짧은 딸깍 |
| `Audio/clash.ogg` | [Impact Sounds](https://kenney.nl/assets/impact-sounds) `impactMetal_medium_000` | — | 쇠 부딪히는 소리 |
| `Audio/gong.ogg` | Impact Sounds `impactBell_heavy_000` | — | 무거운 종 소리 (징 대용) |
| `Audio/drum.ogg` | Impact Sounds `impactSoft_heavy_000` | — | 둔탁한 쿵 (북 대용) |
| `Audio/alert.ogg` | Interface Sounds `error_001` | — | 짧은 경고음 |
| (`arrow` `horn` `fire` `cheer` `duel` `battle` 없음) | — | — | 받은 팩에 맞는 소리가 없어 코드 합성 그대로 |

- 라이선스: 모두 CC0 (출처 표시 불필요, 개인·교육·상업 이용 가능). `Art/Models/License.txt`에 정리.
- **화풍 방향 (2026-11-13 사용자 결정): KayKit으로 통일.** 남은 Kenney(병사·장수·투석기·배)도 차례로 KayKit으로 바꾼다. KayKit 캐릭터(Adventurers)는 **뼈대(SkinnedMesh)·삼각형 5,800~8,900**이라 지금 `ArtCatalog`가 못 읽는다 → 원격에 "SkinnedMesh 지원" 요청. 병사는 무거워 보류 (폰 측정 후 기준 조정).
- [RPG Audio](https://kenney.nl/assets/rpg-audio)도 받았지만 이번에는 쓰지 않았다 (책·문·가죽 소리 위주).

## 효과음 (임시, 2026-11-12)

- 녹음 파일 없이 코드로 합성한 소리 (`Scripts/UI/Sfx.cs`): 북(drum)·칼 부딪힘(clash)·화살(arrow)·나팔(horn)·징(gong)·불(fire)·함성(cheer)·경고(alert)·누름(click)·전장 소음(battle, 반복)·일기토 북(duel).
- 진짜 소리로 바꾸려면 `Resources/Audio/<id>` (예: `Resources/Audio/clash.wav`)를 넣으면 같은 id로 그 파일을 먼저 쓴다. 코드 수정 없음.
- 전투: 시작 북, 보이는 접전 수만큼 커지는 전장 소음, 접전 중 칼 소리, 원거리 교전 화살 소리, 이벤트 소리(스킬 = 나팔, 퇴각·괴멸·포로 = 징, 복병 = 북 + 함성, 화계 = 불, 군량·사기 위험·일기토 신청 = 경고). 결과 화면: 승리 함성 / 패배 징. 모든 버튼 = 누름 소리.
- 설정의 소리 끄기(E26)가 전부 끈다.
