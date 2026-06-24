# ScriptableObject ↔ CSV 범용 동기화 툴

프로젝트의 **모든 ScriptableObject 타입**을 CSV로 내보내고(직렬화), CSV를 다시 SO 에셋에 반영(역직렬화)하는 Unity 에디터 전용 툴입니다.
타입별 필드명을 코드에 박지 않고 `SerializedObject`로 자동 순회하므로, 새 SO 타입을 추가해도 별도 작업 없이 바로 지원됩니다.

- **스크립트**: `Assets/Editor/ScriptableObjectCsvTool.cs`
- **런타임 코드 변경 없음**: 모든 쓰기는 `SerializedObject`를 통하므로 Inspector와 동일하게 Undo / dirty / 저장이 동작합니다.
- 기존 `FacilityDefinitionCsvTool`(전용 툴)을 대체합니다. `FacilityDefinition`도 이 범용 툴로 동일하게 처리됩니다.

### 고정 폴더

| 폴더 | 용도 |
|------|------|
| `Assets/5.Other/CsvData` | **CSV 파일이 사는 곳.** Export 결과가 여기로 저장되고, 이 폴더의 CSV가 바뀌면 자동 import(굽기) |
| `Assets/5.Other/SOData` | import 시 매칭되는 에셋이 없을 때 **새 `.asset`을 만드는 기본 위치** |

---

## 메뉴 위치

Unity 상단 메뉴: **GrayZone ▸ SO CSV Tool** → 전용 창이 열립니다.

창 구성:
- **Rescan**: 프로젝트의 SO 타입을 다시 스캔
- **Type** 드롭다운: 감지된 SO 타입 목록(에셋 개수 표시)
- **Export Selected Type → CsvData**: 선택한 타입의 모든 에셋을 `Assets/5.Other/CsvData/타입명.csv`로 내보내기
- **Export ALL Types → CsvData**: 모든 타입을 각각 `타입명.csv`로 CsvData 폴더에 내보내기
- **Import CSV → SO (auto-detect type)**: CSV를 골라 SO에 반영(타입은 파일 안의 `__Type` 컬럼으로 자동 판별)

> 내보내기는 파일 선택 창 없이 곧바로 `Assets/5.Other/CsvData`로 저장됩니다(폴더가 없으면 자동 생성).

---

## 자동 import (핵심)

`Assets/5.Other/CsvData` 폴더 안의 `.csv`가 **추가/변경되면 Unity가 감지해 자동으로 SO에 굽습니다.** 매번 Import 메뉴를 누를 필요가 없습니다.

```
엑셀에서 CsvData/XXX.csv 저장 → (Unity 포커스 시) 변경 감지
        → AssetPostprocessor 가 해당 CSV를 자동 Import → XXX 타입 SO 에셋 갱신
```

- 동작 주체: `SoCsvAutoImporter`(`AssetPostprocessor`). `OnPostprocessAllAssets`에서 CsvData의 `.csv` 변경을 모아 `EditorApplication.delayCall`로 굽기를 예약합니다(파이프라인 재진입 방지).
- 무한 루프 없음: `.csv` 변경에 반응해 `.asset`만 갱신하므로 `.csv`를 되쓰지 않습니다.
- 외부 에디터(엑셀)로 저장한 경우, **Unity로 포커스가 돌아올 때** 감지됩니다(Auto Refresh 기준). Directory Monitoring이 켜져 있으면 더 빠릅니다.
- 수동 **Import** 메뉴는 그대로 남아 있어, 다른 위치의 CSV를 직접 굽거나 강제 트리거할 때 쓸 수 있습니다.

---

## CSV 구조

타입마다 **CSV 파일 1개**입니다(타입마다 필드 = 컬럼이 다르므로). 한 타입의 모든 에셋이 그 파일의 행이 됩니다.

### 예약 컬럼 (앞 3개)

| 컬럼 | 의미 |
|------|------|
| `__Type` | SO 타입의 전체 이름(FullName). import 시 어떤 타입인지 판별 |
| `__Guid` | 에셋 GUID. **import 매칭 1순위 키** |
| `__Path` | 에셋 경로. 매칭 2순위 + 신규 생성 위치 |

### 필드 컬럼

`__` 뒤로는 SO의 직렬화된 최상위 필드들이 각각 한 컬럼이 됩니다(`m_Script` 제외). 컬럼 순서는 자유이고 이름으로 매칭됩니다.

### 셀 인코딩 규칙

| 필드 종류 | CSV 셀 표현 |
|-----------|-------------|
| int / long / bool / float / double / string | 값 그대로 (`bool`은 `TRUE`/`FALSE`) |
| enum | enum 이름 (예: `Medicine`) |
| Vector2/3/4, Quaternion, Color | `;` 구분 (예: `1;2;3`, 색은 `r;g;b;a`) |
| Object 참조 (다른 SO, Sprite, Prefab 등) | `guid:localId` (없으면 빈 칸) |
| **중첩 구조체 / 배열 / 리스트** | **셀 안에 JSON** (예: `[{"type":"Credit","amount":100}]`) |
| AnimationCurve / Gradient / 다형성 참조 | 미지원(라운드트립 제외) |

> JSON fallback 덕분에 `CostEntry[]` 같은 배열·중첩 구조도 자동 처리됩니다. 다만 그런 셀은 사람이 직접 편집하기엔 까다로우니 주의하세요.

### CSV 예시 (`FacilityDefinition.csv`)

```csv
__Type,__Guid,__Path,m_facilityId,m_facilityName,m_unlockedByDefault,m_unlockCost,m_defaultslot
FacilityDefinition,a1b2...,Assets/5.Other/SOData/FacilityDefinition.asset,M_103,MedicalCenter,TRUE,[],0
```

- 파일은 **UTF-8 (BOM 포함)** 으로 저장되어 엑셀에서 한글이 깨지지 않습니다.
- 콤마·따옴표·줄바꿈·JSON이 들어간 셀은 자동으로 큰따옴표로 감싸고 이스케이프합니다(RFC 4180). 파서도 동일하게 복원합니다.

---

## 사용 방법

### A. 내보내기 (SO → CSV)

1. **GrayZone ▸ SO CSV Tool** 로 창 열기
2. (목록이 비어 있으면) **Rescan**
3. **Type** 드롭다운에서 대상 타입 선택
4. **Export Selected Type → CsvData** 클릭 → 곧바로 `Assets/5.Other/CsvData/타입명.csv`로 저장 (선택 창 없음)
   - 또는 **Export ALL Types → CsvData** 로 전 타입을 한 번에 내보내기
5. 완료 다이얼로그/콘솔에서 개수 확인

### B. 엑셀/시트에서 편집

- `Assets/5.Other/CsvData/` 의 CSV를 엑셀/시트로 열어 값 수정, 새 행 추가(신규 에셋), 행 삭제(에셋 삭제는 **안 됨** — 아래 주의 참고)
- 신규 에셋을 만들 행은 `__Guid`를 비우고 `__Path`에 만들 경로(예: `Assets/5.Other/SOData/NewItem.asset`)를 적어두면 그 위치에 생성됩니다. 비워두면 `Assets/5.Other/SOData/타입명.asset`로 자동 생성됩니다.
- 다시 **CSV(UTF-8)** 로 저장 → Unity로 돌아오면 **자동 import**

### C. 가져오기 (CSV → SO)

CsvData 폴더의 CSV는 저장 시 자동 import되므로 보통 별도 동작이 필요 없습니다. 다른 위치의 CSV를 직접 굽거나 강제로 다시 굽고 싶을 때만:

1. **Import CSV → SO (auto-detect type)** 클릭
2. CSV 선택 → 요약 다이얼로그에서 **Import**
3. 행 처리 규칙:
   - `__Guid`로 기존 에셋 찾음 → **갱신(Update)**
   - 못 찾으면 `__Path`로 재시도 → 있으면 갱신
   - 둘 다 없음 → `__Path`(또는 기본 폴더 `Assets/5.Other/SOData`)에 **신규 생성(Create)**
   - `__Type`을 해석 못하면 **스킵(Skip)**
4. 완료 후 `Updated / Created / Skipped` 요약 표시

---

## 동작 / 안전성 정리

- **매칭 키 우선순위**: `__Guid` → `__Path` → (없으면) 신규 생성. GUID가 가장 안정적이므로 export한 파일을 그대로 편집해 다시 import하는 워크플로를 권장합니다.
- **Undo 지원**: `Ctrl+Z`로 import 결과를 되돌릴 수 있습니다.
- **덮어쓰기 주의**: import는 CSV에 존재하는 컬럼을 기준으로 값을 **덮어씁니다**. 빈 셀도 반영되므로(예: 비운 셀 → 0/빈 문자열/빈 배열), 내보낸 뒤 필요한 부분만 수정하세요.
- **삭제는 하지 않음**: CSV에서 행을 지워도 해당 에셋은 삭제되지 않습니다(안전을 위해 import는 생성/갱신만 수행).
- **폴더 자동 생성**: CsvData / 신규 에셋 대상 폴더가 없으면 자동으로 생성합니다.
- **Object 참조**: 같은 프로젝트 내 에셋 참조만 안정적으로 복원됩니다. 씬 오브젝트 참조는 지원하지 않습니다.
- **Export→자동 import 순환**: Export 직후 자동 import가 한 번 도는데, 값이 동일하므로 결과는 같습니다(idempotent). 다만 에셋이 dirty/save될 수 있습니다.

---

## 지원 범위 한계 (정직하게)

- CSV는 평면 포맷이라 깊은 중첩은 셀 안 JSON으로 우회합니다. 그래서 매우 복잡한 SO는 "정상 라운드트립은 되지만 셀이 JSON 덩어리"가 됩니다.
- `AnimationCurve`, `Gradient`, `[SerializeReference]` 다형성 필드는 라운드트립 대상에서 제외됩니다(해당 셀은 비거나 무시됨). 이런 필드가 핵심인 타입은 이 툴로 관리하지 않는 것이 좋습니다.

---

## 커스터마이즈 포인트 (코드)

`ScriptableObjectCsvTool.cs`:

- `CsvFolder` — CSV 저장/감시 폴더 (기본 `Assets/5.Other/CsvData`)
- `NewAssetFolder` — 신규 `.asset` 생성 기본 폴더 (기본 `Assets/5.Other/SOData`)
- `ColType` / `ColGuid` / `ColPath` — 예약 컬럼 이름
- `SoCsvCodec` — 단순 타입 셀 인코딩/디코딩 (타입 추가/형식 변경 지점)
- `SoCsvTree` — 중첩 구조 ↔ JSON 트리 변환
- `MiniJson` — 셀 JSON 직렬화/파서
- `SoCsvAutoImporter` — CsvData 폴더 자동 import(굽기) 후처리기
