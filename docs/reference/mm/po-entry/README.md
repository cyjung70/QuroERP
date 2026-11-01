# PO Entry (MM · 구매오더 등록) — 구현 기준

Claude Code 로 넘기는 구현 기준 패키지입니다. 화면 시안은 Claude Design 캔버스 "QuroERP PO Entry", 색·글꼴·간격 규칙은 디자인 시스템 "QuroERP"가 기준입니다.

## 1. 파일 구성

| 경로 | 내용 | 상태 |
|---|---|---|
| `src/QuroERP.Web/Features/MM/PurchaseOrder/PoModels.cs` | 헤더·라인·상태·RowState·통화 정밀도 모델 | 컴파일·검증 완료 |
| `.../PoCalculator.cs` | 금액 계산(라인별 반올림 후 합산, 삭제표시 행 제외) | 컴파일·검증 완료 |
| `.../PoStatusRules.cs` | 상태별 버튼 활성 규칙 | 컴파일·검증 완료 |
| `.../IPurchaseOrderApi.cs` | REST 클라이언트 계약 | 인터페이스만 |
| `.../PoEntry.razor` / `.razor.cs` | 화면 + 코드비하인드 (MudBlazor + QuroGrid) | QuroGrid 미완성으로 미컴파일 |
| `.../ItemSearchDialog.razor` | 품목 검색 팝업(코드·이름·분류) | 미컴파일 |
| `src/QuroERP.Web/Resources/Features/MM/PurchaseOrder/PoEntry(.ko).resx` | 영문/한국어 리소스 84개 키 | 생성 완료 |
| `src/QuroERP.Web/wwwroot/css/quro-entry.css` | 공통 엔트리 화면 프레임 + 토큰 | — |
| `src/QuroERP.Web/wwwroot/js/quro.js` | PDF 직접 다운로드 헬퍼 | — |
| `tests/QuroERP.Web.Tests/MM/PurchaseOrder/PoCalculatorTests.cs` | xUnit 테스트 (계산 4건, 상태 3건) | 동일 케이스 오프라인 실행 통과 |

## 2. 화면 프레임 (MM·SD 공통)

1. 타이틀바: 화면명 · 문서번호 · 상태 칩
2. 커맨드바: 조회·신규·저장 | 행 추가·삭제 표시·행 복원 | 승인·마감·취소 | 삭제·PDF
3. 헤더 필드(4열 그리드) + 오른쪽 합계 패널
4. 라인 그리드(QuroGrid)
5. 감사 정보(등록/수정자·일시)

입고(GR), 공급업체 송장, 수주(SO), 출고, 청구 화면은 이 프레임을 그대로 쓰고 필드와 그리드 컬럼만 바꿉니다.

## 3. 필드 규칙

- 노란색 = 입력 가능, 회색 = 읽기전용/계산값, 호박색 왼쪽 마커 + `*` = 필수.
- 소수점: 수량 2, 단가 4, 금액 2 (MM 설정에서 통화별로 재정의, `GetPrecisionAsync`). KRW 금액은 0자리.
- 날짜·숫자 표시는 로그인 시 선택한 문화권(ko-KR / en-US)을 따름.
- AS IS의 `F.O.B` 컬럼은 `Unit Price`로 바꾸고, 인코텀즈(FOB 등)는 헤더 필드로 분리.

## 4. 계산식

```
Gross    = round(Qty × UnitPrice, amt)
Discount = round(Gross × Disc% / 100, amt)
Net      = Gross − Discount
Tax      = round(Net × TaxRate / 100, amt)
Total    = Σ Net + Σ Tax            (삭제 표시 행 제외)
Local    = round(Total × ExchangeRate, localAmt)
반올림: MidpointRounding.AwayFromZero
```

## 5. 상태와 버튼

| 상태 | 저장/행편집 | 승인 | 마감 | 취소 | 삭제 |
|---|---|---|---|---|---|
| Draft 작성중 | ○ | ○ (저장 후) | × | × | ○ |
| Approved 승인 | ○ (입고 전) | × | ○ | ○ (입고 전) | ○ (입고 전) |
| PartiallyReceived 부분입고 | × | × | ○ | × | × |
| Received / Closed / Cancelled | × | × | × | × | × |

입고수량이 하나라도 있으면 상태와 관계없이 수정·삭제 불가 (`PoStatusRules.HasReceipt`). API도 같은 규칙으로 422를 반환해야 합니다.

## 6. 행 동작

- **행 추가**: 헤더의 창고·세금코드·납기일을 기본값으로 복사.
- **삭제 표시**: 토글. 저장 전 신규 행은 즉시 제거, 기존 행은 취소선 표시 후 저장 시 삭제.
- **행 복원**: 선택 행을 조회 시점 값(`Original` 스냅샷)으로 복원. 신규 행은 제거.

## 7. 채번

AS IS와 동일하게 `PO` + `YYMM` + 5자리 일련번호 (예: `PO261000012`). AS IS 샘플 `PO080800002`에서 확인한 형식이며, 채번은 API에서 트랜잭션 안에서 수행합니다.

## 8. QuroGrid 계약 (가정)

QuroGrid는 아직 개발 중이므로 이 화면은 아래 API를 가정했습니다. 실제 API가 다르면 `PoEntry.razor`의 그리드 블록만 맞추면 됩니다.

```razor
<QuroGrid TItem Items SelectedItem(@bind) RowClass(Func<T,string>)
          CellEditable(Func<T,string,bool>) OnCellCommitted(QuroCellCommit<T>) OnLookup(Func<T,Task>)>
  <QuroColumn TItem Field Title Kind(Index|Editable|Lookup|Calculated) Width Numeric Decimals Date Mono Required />
</QuroGrid>
```

`QuroLookupField`(코드+이름+검색 버튼)도 공통 컴포넌트로 가정했습니다.

## 9. AS IS 대비 변경 및 로직 경고

> 🔴 **AS IS 로직 문제**: `m_po_hdr`가 공급업체를 `v_name`(이름)으로 저장·조인합니다 (`po.xml`, `po.jrxml`). 공급업체명이 바뀌면 과거 PO가 끊어지고, 동명 업체를 구분할 수 없습니다. TO BE는 `vendor_code`를 키로 사용합니다.
## 10. 미완성 / TODO

- `PoQueryDialog`, `VendorSearchDialog`, `WarehouseSearchDialog`: `ItemSearchDialog`와 같은 패턴으로 작성 필요.
- 로컬 통화·통화 목록을 설정에서 읽도록 교체.
- API 쪽(ASP.NET Core + Dapper + PostgreSQL) 엔드포인트, 채번, 동시성(`xmin`) 구현.
- FastReport.NET PO 양식(`po.jrxml` 레이아웃 기준).
