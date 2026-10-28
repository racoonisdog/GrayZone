# 테이블 시트 임포터

행이 여러 개인 데이터 표(레벨 표, 드롭 표, 웨이브 표 등)를 **시트 → ScriptableObject**로 굽는 에디터 전용 도구입니다.

- **코드**: `Assets/1.Scripts/Editor/Table/` (`CsvTable.cs`, `TableSheetImporter.cs`, `Sheets/*.cs`)
- **시트 폴더**: `Assets/5.Data/DataSheet/TableData/` — 저장하면 자동으로 가져옵니다
- **수동 전체 가져오기**: `Tools ▸ GrayZone ▸ 테이블 시트 전체 다시 가져오기`
- **빌드**: CSV와 임포터는 빌드에 들어가지 않습니다. 런타임은 구워진 SO만 읽습니다.

## SO CSV 도구와의 차이

| | SO CSV 도구 (`Editor/Tool`) | 테이블 시트 임포터 (`Editor/Table`) |
|---|---|---|
| 정본 | SO (CSV는 스냅샷) | **시트** (SO는 결과물) |
| 모양 | 세로형: 행 = 필드, 열 = 에셋 | **가로형: 행 = 레코드, 열 = 필드** |
| 맞는 데이터 | 무기·적 하나의 튜닝 값 수십 개 | 레벨·드롭·웨이브처럼 행이 여러 개인 표 |
| 자동 가져오기 | 기본 꺼짐 | 켜짐 |

## 시트 규약

```csv
UpgradeId,Level,Cost,Info,#Memo
# 이 줄은 주석 행이라 읽지 않습니다
Trap,1,1,"화염 범위 +15%, 지속 시간 +2초",TrapUpgradeTable 수치와 맞출 것
Shooter,1,1,지원 사격 인원 +1,
Shooter,2,1,지원 사격 인원 +1,
```

- **시트 하나 = CSV 파일 하나**, 파일 이름 = 시트 이름 = 임포터의 `SheetName`
- **첫 행은 헤더**입니다. 열 순서는 자유이고 이름으로 찾습니다(대소문자 무시)
- 헤더가 `#`으로 시작하는 열은 **기획 메모 열**이라 읽지 않습니다
- 첫 칸이 `#`으로 시작하는 행은 **주석 행**, 빈 행은 건너뜁니다
- 리스트를 셀에 넣지 않습니다. 레벨처럼 여러 개인 값은 **행을 늘리고 키 열(`UpgradeId` + `Level`)로 구분**합니다
- 업그레이드 단위의 공통 정보(이름·아이콘 등)까지 시트로 관리해야 할 때만 마스터 시트를 따로 두고 ID로 연결합니다
- 엑셀에서 **"CSV UTF-8(쉼표로 분리)"**로 저장합니다. 그냥 "CSV"로 저장하면 한글이 깨져 가져오기를 거부합니다

## 오류 처리

- 값이 잘못되면 `파일명 N행 [열]: 이유`로 콘솔에 에러를 남깁니다
- 시트 전체를 검증한 뒤 마지막에 한 번에 쓰므로, **실패한 시트는 SO를 전혀 바꾸지 않습니다**
- 가져오기는 `Undo`를 지원합니다

## 새 시트 추가하기

1. 데이터 SO에 시트 한 행에 대응하는 `[Serializable] struct`와 `List<>` 필드를 둡니다
   (에디터 전용 `EditorReplace...` 메서드로 리스트를 통째로 교체)
2. `Sheets/`에 `TableSheetImporter`를 상속한 클래스를 만듭니다
   - `SheetName` = CSV 파일 이름
   - `Import(CsvTable)`에서 `RequireColumns` → 행마다 `GetInt/GetFloat/GetBool/GetEnum/GetString` → 검증 → `FindSingleAsset<T>()`에 기록
3. CSV를 시트 폴더에 넣으면 자동으로 등록·가져오기 됩니다(`TypeCache`가 임포터를 찾습니다)

예시: `Sheets/ScrambleUpgradeLevelSheetImporter.cs` (`ScrambleUpgradeLevel.csv` → `ScrambleUpgradeTableSO`)
