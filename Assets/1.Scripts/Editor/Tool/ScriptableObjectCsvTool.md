# SO CSV 도구

밸런스 데이터를 **스크립트 → ScriptableObject → 엑셀(CSV)** 로 잇는 Unity 에디터 전용 툴입니다.

- **메뉴**: `Tools ▸ GrayZone ▸ SO CSV 도구`
- **스크립트**: `Assets/1.Scripts/Editor/Tool/ScriptableObjectCsvTool.cs` (코드 생성은 `BalanceScaffold.cs`)
- **런타임 코드 변경 없음**: 모든 쓰기는 `SerializedObject`를 거치므로 Inspector와 똑같이 Undo / dirty / 저장이 동작합니다.
- 타입별 필드명을 코드에 박지 않고 자동 순회하므로, 새 SO 타입을 추가해도 별도 작업 없이 지원됩니다.

> **설계 배경은 여기 적지 않습니다.** SO의 적용 범위 경계(무엇을 SO에 두고 무엇을 데이터 매니저에 두는가), 파이프라인 전체 구조, `BindManager` 동작은 별도 정본 문서에 있습니다. 이 문서는 **툴 사용법**만 다룹니다.

---

## 창 구성

두 개의 탭입니다.

### 탭 1 — `스크립트 → SO`

`[BalanceField]`가 붙은 MonoBehaviour에서 대응하는 SO 클래스 소스와 `.asset`을 만듭니다.

| 입력 | 설명 |
|---|---|
| **스크립트 목록** | 변환할 `MonoScript`들. 추가/삭제 가능 |
| **시드 프리팹** | 생성한 `.asset`의 초기값을 여기서 복사해 옵니다 |
| **SO 클래스 이름** | 기본값 `{스크립트명}SO` |
| **SO 데이터 이름** | 기본값 `{프리팹명}_{클래스명}` |
| **생성 경로** | 기본값은 원본 스크립트 옆 |

**스크립트를 여러 개 넣으면 SO 하나로 묶입니다.** SO 단위는 "스크립트"가 아니라 **기획자가 튜닝하는 엔티티**이기 때문입니다. 적 하나가 `EnemyController` / `EnemyTargetSensor` / `EnemyAttack` / `EnemyHealth` 넷으로 나뉘어 있다고 해서 시트가 네 장이 되면 오히려 퇴보입니다.

생성된 소스에는 스크립트별 구분선 주석이 들어갑니다.

```csharp
// ───────────── ThirdPersonController ─────────────
```

**필드 이름이 겹치면 경고만 하고 진행합니다.** (실측: Player 88 + Enemy 28 + Weapon 48 = 164필드 중 충돌 0건. 도메인 접두어 관례 덕입니다.) 접미어를 자동으로 붙이지 않는 이유는, 조건부 접미어가 나중에 충돌이 생긴 순간 **엑셀 컬럼을 개명시켜 기획자 입력을 유실**시키기 때문입니다.

### 탭 2 — `SO → 엑셀(CSV)`

| 항목 | 설명 |
|---|---|
| **밸런스 타입만 표시** | `IBalanceTableData` 구현 타입만 목록에 남깁니다. 변환 동작 자체와는 무관한 **표시 필터**일 뿐입니다 |
| **타입 (에셋 수)** | 프로젝트가 정의한 SO 타입 목록. `Assembly-CSharp` / `Assembly-CSharp-Editor` 것만 나옵니다 |
| **다시 검색** | 타입 목록 재스캔 |
| **`{타입} → CSV 내보내기`** | 그 타입의 모든 에셋을 CSV 한 장으로 |
| **`전체 타입 → CSV 내보내기`** | 모든 타입을 각각 한 장씩 |
| **CSV 가져오기** | 다른 위치의 CSV를 직접 고를 때만 사용(감시 폴더는 자동) |

---

## 폴더

창에서 변경할 수 있고, `EditorPrefs`에 **프로젝트별로** 저장됩니다(팀원마다 다를 수 있음).

| 설정 | 기본값 | 용도 |
|---|---|---|
| CSV 폴더 | `Assets/5.Data/DataSheet/CsvData` | 내보내기 대상이자 **자동 가져오기 감시 폴더** |
| 신규 에셋 폴더 | `Assets/5.Data/ScriptableObject` | 가져오기에서 대응 에셋이 없을 때 새 `.asset`을 만들 위치 |

---

## 파일 이름 규약

**CSV 이름은 SO 타입 이름의 끝 `SO`를 `CSV`로 바꿔 만듭니다.**

```
PlayerCommonBalanceSO  →  PlayerCommonBalanceCSV.csv
WeaponControllerSO     →  WeaponControllerCSV.csv
```

폴더에서 에셋과 시트를 눈으로 구분하기 위한 것입니다. 가져오기는 **파일명이 아니라 시트 안의 `__Type` 행**으로 타입을 찾으므로, 이름을 바꿔도 왕복은 그대로 동작합니다.

> ⚠️ **한 SO를 가리키는 CSV는 하나만 두세요.** 감시 폴더의 CSV는 저장되는 순간 SO를 덮어씁니다. 같은 `__Guid`를 가진 파일이 둘 이상이면 마지막에 바뀐 쪽이 이기고, 나중에 원인 모를 값 되돌아감이 생깁니다.

---

## 자동 가져오기

```
엑셀에서 CsvData/XXX.csv 저장 → (Unity 포커스 시) 변경 감지
        → AssetPostprocessor 가 자동 가져오기 → 해당 SO 에셋 갱신
```

- 주체는 `SoCsvAutoImporter`(`AssetPostprocessor`). `OnPostprocessAllAssets`에서 변경을 모아 `EditorApplication.delayCall`로 예약합니다(파이프라인 재진입 방지).
- **무한 루프 없음**: `.csv` 변경에 반응해 `.asset`만 갱신하고 `.csv`를 되쓰지 않습니다.
- **변경이 있을 때만 돕니다.** 파일이 그대로면 다시 굽지 않으므로, SO를 직접 수정해 CSV와 어긋나게 만들면 그 상태가 유지됩니다. 이때는 **다시 내보내기**로 맞추세요.
- 외부 에디터(엑셀)로 저장한 경우 **Unity로 포커스가 돌아올 때** 감지됩니다. Directory Monitoring이 켜져 있으면 더 빠릅니다.

---

## CSV 구조 (세로형)

**필드가 행, 에셋이 열입니다.** 밸런스 시트는 보통 필드가 수십 개인데 에셋은 몇 개뿐이라, 가로형이면 옆으로 한없이 스크롤해야 합니다. 세로형이면 설명이 필드 이름 바로 옆에 붙어 읽기도 낫습니다.

```csv
__Field,__Tooltip,PlayerTest_PlayerCommonBalanceSO
__Type,,PlayerCommonBalanceSO
__Guid,,9eb8d1c6b58418f4dbbb885a2d34b9ff
__Path,,Assets/5.Data/ScriptableObject/Player/PlayerTest_PlayerCommonBalanceSO.asset
m_moveSpeed,캐릭터의 기본 이동 속도입니다. 단위는 m/s입니다.,5
m_sprintSpeed,캐릭터의 전력질주 속도입니다. 단위는 m/s입니다.,5.335
```

| 위치 | 의미 |
|---|---|
| 1열 | 필드 이름 (또는 `__`로 시작하는 예약 행) |
| 2열 (`__Tooltip`) | 필드 설명. 스크립트의 `[Tooltip]`이 SO를 거쳐 자동 전파됩니다. **읽기 전용**이며 가져오기에서 무시됩니다 |
| 3열~ | 에셋 하나당 한 열 |

**예약 행** — 전부 `__`로 시작하며 값 대입 대상에서 제외됩니다.

| 행 | 의미 |
|---|---|
| `__Type` | SO 타입 FullName. 가져오기에서 타입 판별 |
| `__Guid` | 에셋 GUID. **매칭 1순위 키** |
| `__Path` | 에셋 경로. 매칭 2순위 + 신규 생성 위치 |

> 예전 **가로형**(첫 칸이 `__Type`) 파일도 계속 읽힙니다. 첫 칸이 `__Field`인지로 방향을 판별합니다. 다만 내보내기는 항상 세로형입니다.

### 셀 인코딩

| 필드 종류 | CSV 표현 |
|---|---|
| int / long / bool / float / double / string | 값 그대로 (`bool`은 `TRUE`/`FALSE`) |
| enum | enum 이름 (예: `Medicine`) |
| Vector2/3/4, Quaternion, Color | `;` 구분 (`1;2;3`, 색은 `r;g;b;a`) |
| Object 참조 (SO, Sprite, Prefab 등) | `guid:localId` (없으면 빈 칸) |
| 중첩 구조체 / 배열 / 리스트 | 셀 안에 JSON (`[{"type":"Credit","amount":100}]`) |
| AnimationCurve / Gradient / `[SerializeReference]` | **미지원** (라운드트립 제외) |

- 파일은 **UTF-8 (BOM 포함)** 이라 엑셀에서 한글이 깨지지 않습니다.
- 콤마·따옴표·줄바꿈·JSON이 든 셀은 자동으로 큰따옴표로 감싸고 이스케이프합니다(RFC 4180).
- `float`는 float 정밀도로 씁니다. (`SerializedProperty.doubleValue`를 그대로 쓰면 `5.335`가 `5.3350000381469727`이 됩니다.)

---

## 사용 방법

### A. 내보내기 (SO → CSV)

1. `Tools ▸ GrayZone ▸ SO CSV 도구` → **`SO → 엑셀(CSV)`** 탭
2. 목록이 비어 있으면 **다시 검색**
3. 타입 선택 → **`{타입} → CSV 내보내기`**
4. 콘솔/다이얼로그에서 개수 확인

### B. 엑셀에서 편집

- CSV 폴더의 파일을 열어 값 수정
- **새 에셋을 만들려면 새 열을 추가**하고, `__Guid`를 비운 뒤 `__Path`에 만들 경로를 적습니다. 비워두면 신규 에셋 폴더에 자동 생성됩니다
- **CSV(UTF-8)** 로 저장 → Unity로 돌아오면 자동 가져오기

### C. 가져오기 (CSV → SO)

감시 폴더는 자동이므로 보통 필요 없습니다. 다른 위치의 파일을 굽거나 강제로 다시 구울 때만 **CSV 가져오기** 버튼을 씁니다. 열 하나(=에셋 하나)마다:

1. `__Guid`로 기존 에셋 찾음 → **갱신**
2. 못 찾으면 `__Path`로 재시도 → 있으면 갱신
3. 둘 다 없음 → `__Path`(또는 기본 폴더)에 **신규 생성**
4. `__Type`을 해석 못하면 **건너뜀**

완료 후 `갱신 / 생성 / 건너뜀` 요약이 로그에 남습니다.

---

## 동작 / 안전성

- **매칭 우선순위**: `__Guid` → `__Path` → 신규 생성. GUID가 가장 안정적이므로, 내보낸 파일을 그대로 편집해 되돌리는 흐름을 권장합니다.
- **Undo 지원**: `Ctrl+Z`로 가져오기 결과를 되돌릴 수 있습니다.
- **덮어쓰기 주의**: 시트에 존재하는 행을 기준으로 값을 **덮어씁니다.** 빈 셀도 반영되므로(→ 0 / 빈 문자열 / 빈 배열), 내보낸 뒤 필요한 부분만 고치세요.
- **삭제는 하지 않습니다**: 열을 지워도 에셋은 삭제되지 않습니다(생성·갱신만 수행).
- **클램프는 여기서 하지 않습니다.** 가져오기는 값을 그대로 넣고, 범위 보정은 게임플레이 직전 `BindManager.Bind()`가 `[BalanceField(Min, Max)]`를 보고 수행합니다. 즉 **시트에 이상한 값이 들어가도 SO에는 그대로 들어가고, 컴포넌트에 들어가기 직전에 막힙니다.**
- **씬 오브젝트 참조는 지원하지 않습니다.** 같은 프로젝트 내 에셋 참조만 복원됩니다.

---

## 한계 (정직하게)

- CSV는 평면 포맷이라 깊은 중첩은 셀 안 JSON으로 우회합니다. 매우 복잡한 SO는 "왕복은 되지만 셀이 JSON 덩어리"가 됩니다.
- `AnimationCurve`, `Gradient`, `[SerializeReference]` 다형성 필드는 왕복 대상에서 제외됩니다. 이런 필드가 핵심인 타입은 이 툴로 관리하지 않는 편이 낫습니다.

---

## ⚠️ CLI에서 부를 때

**`ExportType` 같은 메뉴용 메서드를 `unity-cli exec`로 부르지 마세요.** 끝의 `EditorUtility.DisplayDialog`(모달)가 응답을 기다려 **Unity가 멈춥니다.** 실제로 8분간 정지시킨 적이 있습니다.

- 대화상자 없는 내부 메서드를 쓰세요: `WriteCsvForType`, `ImportFile`
- 또는 메뉴를 직접 실행: `unity-cli menu --menu_path "Tools/GrayZone/SO CSV 도구"`

---

## 코드 확장 지점

`ScriptableObjectCsvTool.cs`:

| 이름 | 역할 |
|---|---|
| `CsvFolder` / `NewAssetFolder` | 폴더 설정 (EditorPrefs) |
| `GetCsvFileName(Type)` | `~SO` → `~CSV` 파일명 규약 |
| `ColField` / `RowTooltip` / `ColType` / `ColGuid` / `ColPath` | 예약 이름 |
| `WriteCsvForType` / `ImportTransposed` | 세로형 쓰기·읽기 |
| `SoCsvCodec` | 단순 타입 셀 인코딩/디코딩 |
| `SoCsvTree` / `MiniJson` | 중첩 구조 ↔ JSON |
| `SoCsvAutoImporter` | 감시 폴더 자동 가져오기 |

`BalanceScaffold.cs`:

| 이름 | 역할 |
|---|---|
| `GenerateSoScript` | SO 클래스 소스 생성 (다중 스크립트) |
| `CollectBlocks` | 스크립트별 필드 묶음 + 이름 충돌 검출 |
| `BuildAssetName` | `{프리팹}_{클래스}` 이름 규약 |
