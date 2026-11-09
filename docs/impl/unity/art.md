# 임시 3D 아트 연결

> 상태: 연결 구현 (2026-11-09). 모델 파일은 저장소에 없다 — PC의 Unity에서 무료(CC0) 에셋을 넣는다. 최종 아트는 나중에 교체.
> 관련: [architecture.md](architecture.md) 0.6(3D 전장), [decisions.md](decisions.md)(3D 아트 방향)

## 1. 어떻게 동작하나

- 아트 목록 `unity/Assets/Game/Resources/Art/catalog.json`: 키 → 모델 경로(Resources 아래, 확장자 없이)·목표 높이·방향.
- `Scripts/Battle3D/ArtCatalog`가 목록을 읽고 모델(FBX·프리팹)의 **첫 메시**와 재질을 꺼낸다.
  - 크기는 목표 높이(`height`, 전장 1칸 = 1)로 맞추고, 바닥을 땅에, 가운데를 원점에 둔다 → 에셋마다 단위·원점이 달라도 그대로 쓴다.
  - 재질은 인스턴싱을 켠 사본. 셰이더를 쓸 수 없으면(분홍) 기본 재질(GameLit)에 텍스처·색만 옮긴다.
- **모델 파일이 없으면 지금의 단순 도형**(상자 병사, 소품 없음)으로 그린다. 그래서 모델을 하나씩 넣어 가며 볼 수 있다.
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

## 3. 주의

- 모델은 **메시 하나만** 쓴다 (여러 조각으로 된 모델은 첫 조각만). 조각이 여러 개면 Unity에서 하나로 합치거나 다른 모델을 고른다.
- 폰 성능: 병사 모델은 **삼각형 500개 이하**의 로우폴리를 고른다 (화면 병사 수백 명 × 모델).
- `Resources` 폴더의 파일은 모두 빌드에 들어간다. 쓰지 않는 모델은 넣지 않는다.
- 모델 파일은 저장소에 올려도 된다 (CC0). 올리면 라이선스 파일(License.txt)을 같은 폴더에 함께 둔다.
