# Bàn giao Backend → Frontend & Mobile

> Cập nhật: 21/10/2026 · Backend: `emergency-dispatch-backend` (chưa commit lên `main`, sẽ push sau)
> Tài liệu API đầy đủ: [`emergency-dispatch-backend/API_DOCUMENTATION.md`](emergency-dispatch-backend/API_DOCUMENTATION.md) · Swagger: `http://localhost:5000/swagger`

File này chỉ liệt kê những việc phía **Web (React)** và **Mobile (Expo)** cần làm vì thay đổi ở backend, hoặc lỗi backend phát hiện được khi đối chiếu. Backend không sửa trực tiếp code FE/Mobile.

| Mức | Ý nghĩa |
|---|---|
| 🔴 Bắt buộc | Không sửa sẽ hiển thị sai hoặc gọi API lỗi |
| 🟡 Nên làm | API backend đã sẵn sàng, FE/Mobile chưa tích hợp |
| ⚪ Để biết | FE đang mock vì backend chưa có API — chưa cần làm gì |

---

## 1. Thay đổi phía Backend (đã xong)

### 1.1. Severity chỉ còn 4 giá trị
`Level4` và `Level5` đã **bị xóa khỏi enum và DB** (DB có CHECK constraint, không thể lưu giá trị khác).

| Giá trị JSON | Số | Ý nghĩa |
|---|---|---|
| `"Unclassified"` | 0 | **Reject** / chưa phân loại — báo khống, nấu nướng, diễn tập, hoặc AI lỗi → Operator phải xem thủ công |
| `"Level1"` | 1 | Nhẹ |
| `"Level2"` | 2 | Trung bình |
| `"Level3"` | 3 | Khẩn cấp cao nhất (tự động leo thang) |

- `PUT /api/incidents/{id}/verify` chỉ nhận `confirmedSeverity` = `Level1` / `Level2` / `Level3`. Gửi `Unclassified` sẽ bị **400**. Sự cố cần bác bỏ → gọi `PUT /api/incidents/{id}/cancel`.
- `aiClassification.severityScore` giờ chỉ là `0..3`.

### 1.2. Link API thống nhất — đã xóa link cũ
Chỉ còn tiền tố `/api/...`. Các link sau **trả 404**:

| Link cũ (đã xóa) | Link mới |
|---|---|
| `/api/v1/dispatch/...` | `/api/dispatch/...` |
| `/api/v1/traffic/...` | `/api/traffic/...` |
| `/api/RescueUnits/...` | `/api/rescue-units/...` |
| `/api/v1/escalation-rules` | `/api/escalation/rules` |
| `/api/v1/escalation-logs` | `/api/escalation/logs` |
| `/api/v1/escalation/trigger-check` | `/api/escalation/trigger-check` |

✅ Đã kiểm tra: code Web và Mobile hiện **không gọi** link cũ nào → không cần sửa gì. Khi tích hợp các module trên thì dùng link mới.

---

## 2. 🔴 Bắt buộc sửa

### 2.1. [Web] `Unclassified` đang bị hiển thị thành Cấp 1
**File:** `src/types/incident.ts` (dòng ~19, ~106, ~181–188, ~222)

Hiện tại:
```ts
export type SeverityLevel = 1 | 2 | 3;
export type BackendSeverityLevel = 'Unclassified' | 'Level1' | 'Level2' | 'Level3' | 'Level4' | 'Level5';

const SEVERITY_FROM_BACKEND: Record<BackendSeverityLevel, SeverityLevel> = {
  Unclassified: 1,   // ❌ sự cố Reject / AI lỗi bị hiện như "Cấp 1 - Nhẹ"
  Level1: 1, Level2: 2, Level3: 3,
  Level4: 3, Level5: 3,   // ❌ không còn tồn tại
};
// ...
severity: SEVERITY_FROM_BACKEND[dto.severity] ?? 1,
```

**Vấn đề:** Backend xếp `Unclassified` **lên đầu hàng đợi** vì cần Operator xem gấp, nhưng UI lại gắn nhãn "Cấp 1" (nhẹ nhất) → Operator dễ bỏ qua.

**Đề xuất sửa:**
```ts
export type SeverityLevel = 0 | 1 | 2 | 3;   // 0 = Reject / cần xem thủ công
export type BackendSeverityLevel = 'Unclassified' | 'Level1' | 'Level2' | 'Level3';

const SEVERITY_FROM_BACKEND: Record<BackendSeverityLevel, SeverityLevel> = {
  Unclassified: 0,
  Level1: 1,
  Level2: 2,
  Level3: 3,
};

// Khi verify chỉ cho chọn 1–3 (0 không gửi lên verify, dùng cancel)
const SEVERITY_TO_BACKEND: Record<Exclude<SeverityLevel, 0>, BackendSeverityLevel> = {
  1: 'Level1',
  2: 'Level2',
  3: 'Level3',
};

// ...
severity: SEVERITY_FROM_BACKEND[dto.severity] ?? 0,
```
Sau đó cập nhật các component hiển thị severity (badge, bộ lọc, `IncidentDetailDrawer`) để có nhãn riêng cho mức 0, ví dụ **"Chưa phân loại / Cần xác minh"**. Nếu không muốn đổi type, ít nhất thêm cờ `needsManualReview: dto.severity === 'Unclassified'` thay vì quy về 1.

### 2.2. [Web] Xóa case `Level4` / `Level5`
**File:** `src/components/citizen/CitizenReportsModal.tsx` (dòng ~140–141)
```ts
case 'Level5':   // ❌ xóa
case 'Level4':   // ❌ xóa
case 'Level3':
```
Đồng thời nên thêm case `'Unclassified'` (hiện đang rơi vào `default`).

---

## 3. 🟡 API đã sẵn sàng — chưa tích hợp

### 3.1. [Mobile + Web Citizen] Đường di chuyển xe cứu hộ cho Người dân
API: `GET /api/dispatch/tracking/incident/{incidentId}` (công khai, không cần token).
Trả về vị trí xe, **`routeTrajectory`** (mảng điểm để vẽ polyline), `distanceKm`, `estimatedArrivalMinutes`, các mốc thời gian. Chưa có xe → `dispatchStatus: "PendingDispatch"`.

Mobile — thêm vào `src/api/endpoints.ts`:
```ts
DISPATCH: {
  TRACKING: (incidentId: string) => `/dispatch/tracking/incident/${incidentId}`,
},
```
Type:
```ts
export interface RoutePoint { latitude: number; longitude: number; }

export interface CitizenTracking {
  incidentId: string;
  incidentTitle: string;
  incidentAddress: string;
  incidentLat: number;
  incidentLng: number;
  incidentStatus: string;
  incidentSeverity: 'Unclassified' | 'Level1' | 'Level2' | 'Level3';
  rescueUnitId?: string;
  plateNumber?: string;
  unitTypeName?: string;
  stationName?: string;
  stationPhoneNumber?: string;
  vehicleLat?: number;
  vehicleLng?: number;
  lastLocationUpdateAt?: string;
  routeTrajectory: RoutePoint[];
  distanceKm: number;
  estimatedArrivalMinutes: number;
  dispatchStatus: 'PendingDispatch' | 'Dispatched' | 'Accepted' | 'EnRoute' | 'OnScene' | 'Completed' | 'Cancelled';
  dispatchedAt?: string;
  acceptedAt?: string;
  arrivedAt?: string;
  completedAt?: string;
  notes?: string;
}
```
Cách dùng: gọi API 1 lần để vẽ lộ trình → sau đó nghe SignalR (mục 3.3) để di chuyển marker xe realtime → gọi lại API khi nhận `ReceiveAssignmentStatus` hoặc mỗi 15–30 giây để cập nhật lộ trình/ETA.

### 3.2. [Mobile] Gửi SOS thật (hiện đang giả lập)
`HomeScreen.tsx` / `MediaReportModal.tsx` đang dùng `setTimeout` và địa chỉ GPS cố định, chưa gọi API. Mobile đã có `media.service.ts` (upload) nhưng **chưa có incident service**.

```ts
// src/services/incident.service.ts
import { apiService } from '../api/client';   // giống media.service.ts
import { Endpoints } from '../api/endpoints';

export interface CreateIncidentPayload {
  title?: string;
  description: string;
  latitude: number;
  longitude: number;
  locationAddress: string;
  reporterName?: string;
  reporterPhone?: string;
  mediaUrls?: string[];
}

export const incidentService = {
  create: (payload: CreateIncidentPayload) =>
    apiService.post(Endpoints.INCIDENTS.BASE, payload),       // trả về ngay, status = AiProcessing
  getById: (id: string) => apiService.get(Endpoints.INCIDENTS.BY_ID(id)),
  cancel: (id: string, reason?: string) =>
    apiService.put(Endpoints.INCIDENTS.CANCEL(id), { reason }),
};
```
Luồng: lấy GPS thật (`expo-location`) → upload ảnh (`/media/upload`) → `POST /incidents` với `mediaUrls` → lưu `incidentId` → mở màn theo dõi (mục 3.1). Kết quả AI đến sau qua SignalR hoặc gọi lại `GET /incidents/{id}`.

### 3.3. [Web + Mobile] Kết nối SignalR (chưa có client nào)
Backend đã bắn đủ sự kiện realtime nhưng Web/Mobile chưa cài `@microsoft/signalr`.

```bash
npm install @microsoft/signalr
```
```ts
import * as signalR from '@microsoft/signalr';

const BASE = 'http://localhost:5000'; // Android emulator: http://10.0.2.2:5000

export function createHub(path: '/hubs/incidents' | '/hubs/tracking', getToken: () => string | null) {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${BASE}${path}`, { accessTokenFactory: () => getToken() ?? '' })
    .withAutomaticReconnect()
    .build();
}

// Operator dashboard
const incidents = createHub('/hubs/incidents', getAccessToken);
incidents.on('ReceiveNewIncident', (incident) => { /* thêm vào hàng đợi */ });
incidents.on('ReceiveIncidentUpdate', (incident) => { /* cập nhật severity AI, trạng thái */ });
incidents.on('ReceiveJobAssigned', (assignment) => { /* ... */ });
incidents.on('ReceiveAssignmentStatus', (assignment) => { /* ... */ });
incidents.on('ReceiveTrafficHotspot', (hotspot) => { /* ... */ });
incidents.on('ReceiveEscalationAlert', (alert) => { /* còi + popup đỏ */ });
await incidents.start();

// Citizen theo dõi xe (không bắt buộc đăng nhập)
const tracking = createHub('/hubs/tracking', getAccessToken);
tracking.on('ReceiveRescueUnitLocation', (p) => {
  // p = { rescueUnitId, incidentId, latitude, longitude, headingDegrees, speedKmh, timestampUtc }
});
await tracking.start();
await tracking.invoke('SubscribeIncident', incidentId);

// Staff gửi GPS mỗi 3–5 giây (chỉ RescueStaff/Admin)
await tracking.invoke('UpdateLocation', {
  rescueUnitId, incidentId, latitude, longitude, headingDegrees, speedKmh,
});
```
Danh sách đầy đủ sự kiện và nhóm nhận: `API_DOCUMENTATION.md` → Phân hệ 9.

### 3.4. [Web Operator / Staff] Các module đang dùng mock nhưng backend đã có API

| Màn hình (Web) | Đang dùng | API thật |
|---|---|---|
| Live Map / DispatchPanel | `liveMapMock`, `DispatchContext` | `GET /api/dispatch/recommend`, `POST /api/dispatch/assign`, `GET /api/rescue-units`, `GET /api/traffic/active` |
| Staff Portal — nhiệm vụ | `staffMock` | `GET /api/dispatch/my-assignments`, `PUT /api/dispatch/assignments/{id}/status`, `POST /api/dispatch/assignments/{id}/resolve` |
| Admin — Trạm | `stationAdminService` (mock) | `/api/stations` (CRUD) |
| Admin — Xe | `vehicleAdminService` (mock) | `/api/rescue-units` (CRUD + `/status`, `/location`) |
| Admin — Tài khoản | `accountAdminService` (mock) | `GET/POST /api/users`, `PUT/DELETE /api/users/{id}` |
| Admin — Leo thang | `adminSettingsService` (mock) | `/api/escalation/rules`, `/api/escalation/logs` |
| Admin — Tổng quan | `overviewAdminService` (mock) | `GET /api/dashboard/stats` |

Body / query chi tiết: xem `API_DOCUMENTATION.md` Phân hệ 5, 6, 7, 10, 11, 12, 13.

---

## 4. ⚪ Để biết — backend chưa có, FE cứ giữ mock

| Tính năng FE | File FE | Ghi chú backend |
|---|---|---|
| **Tổ làm việc dạng thực thể** (id, tên, xe phụ trách, thành viên, tổ trưởng) | `services/stationTeamService.ts`, `types/stationTeam.ts` | Backend hiện chỉ lưu **tên tổ** dạng text cho từng nhân viên (`PUT /api/users/{id}/assign-station-team`, `GET /api/users/my-team`). Chưa có `StationTeam` (vehicleId, crewRole, tổ trưởng). Backend sẽ làm entity riêng — khi xong sẽ cập nhật file này kèm contract. |
| Khóa sự cố khi Operator đang xử lý (`lockedByOperatorId`, `lockExpiresAt`) | `types/incident.ts`, `IncidentDetailDrawer.tsx` | `IncidentResponseDto` chưa có các field này → luôn `undefined`. |
| Thông báo cho Staff (danh sách, đã đọc) | — | Chưa có `GET /api/notifications`. Hiện chỉ nhận realtime qua `ReceiveJobAssigned`. |
| Đóng hồ sơ sự cố / Audit trail / Báo cáo của tôi | `AuditTrailPage`, `CitizenReportsModal` | Chưa có `/incidents/{id}/close`, `/incidents/{id}/audit-trail`, `/incidents/my-reports`. |

---

## 5. Đối chiếu với SRS (Report3) — Web đang làm sai hoặc ngoài phạm vi

> Nguồn đối chiếu: `Report3_Software Requirement Specification.md` (bản 29/09) và repo `emergency-dispatch-frontend` tại commit `ec36793`.

### 5.1. 🔴 Làm sai so với SRS

| # | Vấn đề | Vị trí | SRS nói gì | Đề xuất |
|---|---|---|---|---|
| 1 | **Bỏ hẳn Level 0**: comment ghi *"Cấp 1–3, không có Cấp 0. AI không phân loại được → Cấp 1 + % tin cậy thấp"* | `types/incident.ts:18-19`, `:182`, `:222` | FR-OP-02, FR-SYS-01: Level 0 (Reject / cần xác minh) được xếp **ngang Level 3 ở đầu hàng đợi**, có banner `[⚠️ LEVEL 0 - AI REJECTED: VERIFICATION REQUIRED]`; **cấm hạ ưu tiên** Level 0 | Thêm mức 0 vào `SeverityLevel` (xem mục 2.1), có badge/banner riêng |
| 2 | **Không có tag `fire` thì mặc định là Tai nạn** | `types/incident.ts:220` → `category: hazardTags.includes('fire') ? 'fire' : 'accident'` | FR-SYS-01: EventType = `FireExplosion` / `TrafficAccident` / `None` | Sự cố AI lỗi (tags `Unclassified`) bị coi là tai nạn → gợi ý nhầm xe cứu thương. Thêm loại `'unknown'` và bắt Operator chọn khi duyệt. Backend sẽ bổ sung trường `eventType` (xem 5.4) |
| 3 | **Đếm ngược 120 giây** để đội cứu hộ Chấp nhận / Từ chối, quá hạn tự trả xe | `data/dispatchConfig.ts:4-5`, `context/DispatchContext.tsx:27,117-161,417-457`, `types/vehicle.ts` (`awaiting_ack`, `ackDeadline`), `types/incident.ts:31` (`kind: 'timeout'`) | Change log SRS 26/09/2026: **"Removed … 120s dispatch countdown (FR-OP-04, FR-RS-01, BR-21)"** | Gỡ timeout tự động; chỉ giữ Chấp nhận / Từ chối kèm lý do (BR-22) |
| 4 | **Bàn giao bằng QR** trong báo cáo kết thúc ca | `components/staff/StaffQrHandover.tsx`, import tại `StaffClosureReportModal.tsx:22` | Change log 26/09: **"Removed … FR-RS-04 (QR handover)"** | Gỡ khỏi `StaffClosureReportModal` |
| 5 | **Leo thang chỉ xét Cấp 3** | `utils/escalation.ts:22` → `if (incident.severity !== 3) return false;` | FR-SYS-02: quét `SeverityScore IN (0, 3)` | Thêm Level 0 (sau khi sửa #1) |
| 6 | Vẫn còn **thang cũ 0–5** (`IncidentSeverity`) cho Staff portal và Admin escalation | `types/incident.ts:3-5`, `types/dashboard.ts` | Thang chuẩn 0–3 | Gộp về một kiểu duy nhất |
| 7 | **Báo cáo kết thúc ca không có validation**: xóa hết ảnh vẫn nộp được; ảnh, số nạn nhân (`2`), tóm tắt, nơi bàn giao **được điền sẵn dữ liệu mẫu**; `teamLeader` viết cứng `'Nguyễn Văn An'`; thiếu chữ ký thì tự gán `'SIGNED-DIGITALLY'` | `StaffClosureReportModal.tsx:46-130` | FR-RS-03 / BR-25: bắt buộc ≥ 1 ảnh (MSG42), số nạn nhân ≥ 0, tóm tắt 10–1000 ký tự | Bỏ giá trị mặc định, chặn nộp khi thiếu ảnh hoặc tóm tắt < 10 ký tự, lấy tổ trưởng từ tài khoản đăng nhập. Backend `resolve` cũng đang yêu cầu `resolutionSummary` tối thiểu 10 ký tự |

> ⚠️ SRS **tự mâu thuẫn** về ngưỡng leo thang: FR-SYS-02 ghi **5 phút** (đêm 2.5 phút, BR-19), còn BR-17 ghi **3 phút** cho Level 3 (MSG46 cũng ghi 3 phút). Web đang dùng 5 / 2.5 phút, backend mặc định 3 phút. **Nhóm cần chốt một con số** và sửa lại SRS.

### 5.2. 🟠 Logic nghiệp vụ đang chạy ở client (phải chuyển về backend)
Các phần dưới đây là **nguồn sự thật nằm trong trình duyệt** — mỗi Operator mở tab sẽ tính ra kết quả riêng, không đồng bộ và không ghi nhận được cho nghiên cứu RQ2/RQ3. Giữ được để demo UI, nhưng khi tích hợp phải lấy kết quả từ API:

| Logic | File FE | API backend thay thế |
|---|---|---|
| Thuật toán gợi ý xe (Nearest / Load-Aware, mở rộng bán kính) | `utils/dispatch.ts` | `GET /api/dispatch/recommend` |
| Phát hiện leo thang | `utils/escalation.ts` | SignalR `ReceiveEscalationAlert` |
| Vòng đời xe, hồi sức 5 phút | `context/DispatchContext.tsx` | `ReceiveAssignmentStatus`, `GET /api/rescue-units` |

### 5.3. 🟡 Ngoài phạm vi SRS — cần nhóm xác nhận trước khi làm tiếp

| Tính năng | File chính | Quy mô | Ghi chú |
|---|---|---|---|
| **Xếp ca trực / Lịch ca / Xin nghỉ / Đổi ca / Sao chép tuần / Tăng ca / Check-in-out** | `pages/admin/ShiftsPage.tsx`, `services/admin/shiftService.ts`, `components/staff/schedule/*` (8 file), `types/shift.ts`, `data/admin/shiftSeed.ts` | **~4.400 dòng** | SRS không có FR nào cho quản lý ca. Backend chưa có và chưa có kế hoạch làm |
| Chữ ký điện tử khi kết thúc ca | `components/staff/StaffSignaturePad.tsx` | ~150 dòng | Không có trong FR-RS-03 |
| Khóa / mở khóa tài khoản Citizen bởi Admin | `pages/admin/CitizensPage.tsx`, `services/admin/citizenAdminService.ts` | ~250 dòng | Không có FR tương ứng (SRS chỉ có chặn số spam ở FR-OP-03) |
| "Trưởng trạm" (`headOperatorId`) — Operator quản lý trạm | `types/adminFleet.ts:31-32`, `types/stationTeam.ts` | — | SRS chỉ nhắc *Station Commander* là người **nhận cảnh báo leo thang**, không phải vai trò quản lý trạm |
| Tổ làm việc có tổ trưởng / vai trò kíp / xe phụ trách | `services/stationTeamService.ts` | ~1.100 dòng (commit `ec36793`) | Có trong yêu cầu nhóm ("Operator thêm staff vào trạm và Tổ làm việc") nhưng **SRS chưa mô tả**. Cần chốt thiết kế với backend trước khi làm tiếp (xem mục 4) |

### 5.4. Lệch tên gọi Web ↔ Backend (cần thống nhất khi tích hợp)
Các `TODO(backend)` trong Web đang ghi **tên API tự đặt** không khớp backend:

| Web dự kiến | Backend thực tế |
|---|---|
| `/api/Vehicles`, `PATCH /api/Vehicles/{id}/status` | `/api/rescue-units`, `PUT /api/rescue-units/{id}/status` |
| `GET /api/Users?roles=Operator,RescueStaff` | `GET /api/users?role=` (lọc **1** role mỗi lần) |
| `/api/Settings/escalation`, `/api/Settings/dispatch` | Chưa có — hiện chỉ có `/api/escalation/rules` |
| `/api/AuditTrail` | Chưa có |
| `/api/Citizens`, `/api/BlockedPhoneNumbers`, `/api/Shifts` | Chưa có |

**Trạng thái xe** — Web và backend dùng hai bộ khác nhau:

| Web (`types/vehicle.ts`, `adminFleet.ts`) | Backend `RescueUnitStatus` |
|---|---|
| `available` | `Available` |
| `awaiting_ack` | *(bỏ — xem 5.1 #3)*, tương ứng assignment `Dispatched` |
| `accepted`, `en_route` | `Dispatched` (chi tiết nằm ở assignment: `Accepted`, `EnRoute`) |
| `on_scene` | `OnScene` |
| `recovering` | *(chưa có — backend sẽ bổ sung CoolDown)* |
| `maintenance` | `Maintenance` |
| `out_of_service` | *(chưa có)* |

**Loại xe**: Web có 4 loại (`ambulance`, `fire_truck`, `police`, `rescue`), backend có 6 (`Ambulance`, `FireTruck`, `LadderTruck`, `RescueBoat`, `HeavyRescueVehicle`, `PolicePatrol`). Đề xuất map `rescue` ↔ `HeavyRescueVehicle`, `police` ↔ `PolicePatrol`, và hiển thị thêm `LadderTruck`, `RescueBoat`.


---

## 6. Cập nhật đợt 2 (21/10/2026) — Backend sửa theo SRS, FE/Mobile cần làm gì

> ⚠️ **Trạng thái:** code backend đợt này **chưa commit** và **chưa áp migration lên Neon**. Còn 3 migration chờ chạy: `Reconcile_Legacy_Schema_And_Dispatch_Fields`, `Recreate_Completion_Report_History_Notifications`, `Security_PII`. Trước khi backend chạy `dotnet ef database update`, đừng test các API ở mục này. Khi đã áp xong, backend sẽ báo lại.
>
> Mọi lỗi giờ có thêm trường **`errorCode`** trong response: `CONFLICT` (HTTP 409), `FORBIDDEN` (403), `NOT_FOUND` (404), `TOO_MANY_REQUESTS` (429). FE nên dựa vào `errorCode` hoặc HTTP status, **không** dò chuỗi `message`.

### 6.1. 🔴 Thay đổi phá vỡ tương thích (breaking) — bắt buộc sửa

| # | Thay đổi backend | Ảnh hưởng & việc FE/Mobile cần làm |
|---|---|---|
| 1 | `GET /api/incidents` và `GET /api/incidents/queue` **chỉ cho `Operator`, `Admin`** (trước đây công khai, lộ số điện thoại và hồ sơ y tế) | Web `services/incidentService.ts` → `getIncidents()` đang ghi chú "hỗ trợ Citizen xem lịch sử": gọi bằng tài khoản Citizen giờ sẽ nhận **403**. Lịch sử của Citizen sẽ dùng `GET /api/incidents/my-reports` (backend đang làm, xem 6.5) |
| 2 | `GET /api/incidents/{id}`: phải đăng nhập; Citizen chỉ xem được sự cố của chính mình (403 nếu không phải) | Gửi token khi gọi |
| 3 | `PUT /api/incidents/{id}/verify` **chỉ `Operator`, `Admin`** | Không ảnh hưởng nếu chỉ dùng ở dashboard Operator |
| 4 | `PUT /api/incidents/{id}/cancel` **bắt buộc đăng nhập** (`Citizen` chỉ hủy được sự cố của mình, `Operator`, `Admin`). Citizen **không hủy được khi xe đã điều động** (`Dispatched`, `InProgress` hoặc còn xe đang chạy) → 400 kèm MSG20 | Web/Mobile Citizen: ẩn/khóa nút Hủy từ `Dispatched` trở đi và hiện: *"Đội cứu hộ đang trên đường tới… vui lòng liên hệ 114/115"*. Khách vãng lai không hủy được nữa |
| 5 | **SignalR `/hubs/incidents` và `/hubs/tracking` bắt buộc token.** `SubscribeIncident(incidentId)` chỉ cho người báo (Citizen chủ sự cố), Operator/Admin, và RescueStaff của trạm có xe được giao; người khác → `HubException` | Luôn truyền `accessTokenFactory`. **Khách vãng lai không theo dõi realtime được nữa** (quyết định của nhóm: theo dõi bắt buộc đăng nhập) |
| 6 | `GET /api/dispatch/tracking/incident/{id}` và `GET /api/dispatch/assignments/incident/{id}` **bỏ công khai**: bắt buộc đăng nhập và có quyền như mục 5 (403 nếu không) | Sửa ghi chú "công khai, không cần token" ở mục 3.1 / 5.9: phải gửi token |
| 7 | **Che số điện thoại:** `reporterPhone`, `reporterMedicalProfile.phoneNumber` và SĐT trong `iceContacts` giờ trả dạng `098***456`, **trừ khi người xem chính là người báo** | Web: nút "Hiện số" trong `IncidentDetailDrawer.tsx` gọi API thật `POST /api/incidents/{id}/reveal-phone` (Operator/Admin/RescueStaff đang làm nhiệm vụ) → `{ incidentId, phoneNumber }`. **Bỏ** `incidentService.logPhoneReveal()` mock, vì backend tự ghi Audit Trail mỗi lần gọi |
| 8 | **Mô tả SOS tối đa 200 ký tự** (FR-CIT-02; trước là 2000) | Web `SosModal.tsx`, Mobile `MediaReportModal.tsx`: giới hạn ô nhập 200 ký tự, có bộ đếm |
| 9 | **Chống spam SOS** (BR-CIT-01): mỗi tài khoản / mỗi SĐT tối đa 1 sự cố đang hoạt động trong 3 phút → **429** `TOO_MANY_REQUESTS`. Thêm giới hạn 5 SOS/phút/IP và 10 lần login/phút/IP (429) | Hiện thông báo từ `message`, kèm gợi ý gọi 114/115. Đừng tự động retry |
| 10 | **SĐT bị chặn gửi SOS** → **403** `FORBIDDEN` | Hiện `message`, gợi ý gọi trực tiếp 114/115 |
| 11 | **Ghi đè mức độ AI phải có lý do:** khi `confirmedSeverity` khác mức AI đề xuất, `operatorNotes` bắt buộc ≥ 5 ký tự (BR-03 / MSG25) → nếu thiếu: 400 | Web `IncidentDetailDrawer.tsx` đã có `overrideReason`: gửi nó vào trường `operatorNotes` của body `verify` |
| 12 | `POST /api/dispatch/assign` **trả về MẢNG** `DispatchAssignmentResponseDto[]` (trước là 1 object). Body mới: `{ incidentId, rescueUnitIds: [..], algorithm?, allowExtraUnits?, notes? }`. `rescueUnitId` đơn vẫn nhận để tương thích | Đọc `data` là mảng. Các quy tắc: sự cố **phải xác minh xong** (Level 1–3) mới được điều xe; Level 3 tối đa 5 xe, Level 1–2 tối đa 1 xe (muốn thêm: `allowExtraUnits: true` + `notes` ≥ 5 ký tự); xe vừa bị Operator khác giao → **409** (MSG32): tải lại danh sách gợi ý. Nên gửi `algorithm` (`NearestAvailable`/`LoadAware`) để lưu dữ liệu RQ2 |
| 13 | **Thứ tự trạng thái nhiệm vụ bắt buộc** (BR-24): `Dispatched → Accepted → EnRoute → OnScene`; nhảy cóc/lùi → 400. `PUT …/status` **không** nhận `Completed` (dùng `resolve`) và **không** nhận `Rejected` (dùng `reject`). RescueStaff chỉ cập nhật được xe của trạm mình (403); `Cancelled` chỉ Operator/Admin | Web Staff (`StaffStatusStepper.tsx`), Mobile (`MissionStepper.tsx`): chỉ cho bấm nút kế tiếp, không cho nhảy thẳng tới OnScene |
| 14 | **`POST …/assignments/{id}/resolve` đổi body** (FR-RS-03, BR-25): `evidenceMediaUrls` **bắt buộc ≥ 1 ảnh** (MSG42), `victimsAssistedCount` **bắt buộc ≥ 0**, thêm `equipmentUsed: string[]`, `handoverEntity`, `handoverContactName`, `handoverContactPhone`, `handoverNotes`. RescueStaff chỉ nghiệm thu được khi đã `OnScene` | Web `StaffClosureReportModal.tsx` (xem 5.1 #7): upload ảnh qua `/api/media/upload` rồi gửi URL; bỏ dữ liệu mẫu. Map `casualtiesTreated` → `victimsAssistedCount`, `resourcesUsed` → `equipmentUsed` (dạng chuỗi "Bột chữa cháy ABC x4"), `handoverEntity`/`ContactName`/`ContactPhone` giữ nguyên tên. **Bỏ** `handoverQrData` và `signatureDataUrl` (backend không nhận) |
| 15 | **Sự cố nhiều xe chỉ đóng khi mọi xe đã `resolve`** (BR-14). Xe nghiệm thu xong sẽ chuyển `CoolDown` 5 phút (BR-27) rồi tự về `Available` | Message trả về cho biết còn bao nhiêu xe. Ở web, `DispatchContext.tsx` không tự đếm ngược "hồi sức" nữa, chỉ đọc `status` từ API/SignalR |
| 16 | **Enum mới:** `RescueUnitStatus` thêm `CoolDown`, `OutOfService`; `DispatchAssignmentStatus` thêm `Rejected` | Thêm vào type FE/Mobile. Bảng map trạng thái xe ở 5.4: `recovering` ↔ `CoolDown`, `out_of_service` ↔ `OutOfService` |

### 6.2. 🟡 API mới / trường mới backend đã có — FE/Mobile tích hợp

**a) Khóa mềm sự cố 2 phút** (FR-OP-02, BR-15) — thay mock `incidentService.lock()`/`unlock()` của Web:

| Method | Endpoint | Ghi chú |
|---|---|---|
| PUT | `/api/incidents/{id}/lock` | Operator/Admin. Giữ hoặc gia hạn khóa 2 phút → `{ incidentId, lockedByOperatorId, lockedByOperatorName, lockExpiresAt }`. Người khác đang giữ → **409**. Gọi lại khoảng mỗi 60s khi vẫn đang mở sự cố |
| DELETE | `/api/incidents/{id}/lock` | Nhả khóa khi đóng drawer. Admin thêm `?force=true` để nhả khóa của người khác |

`IncidentResponseDto` giờ có thật `lockedByOperatorId`, `lockedByOperatorName`, `lockExpiresAt` (null nếu khóa đã hết hạn), đúng tên FE đang chờ. `verify` / `cancel` / `assign` khi người khác giữ khóa → 409.

**b) Từ chối nhiệm vụ** (FR-RS-01, BR-22) — Web `StaffMissionAlertModal.tsx`, Mobile `EmergencyAlertModal.tsx`:
```http
POST /api/dispatch/assignments/{id}/reject
{ "reason": "VehicleBreakdown", "notes": "Hết bình ắc quy" }
```
`reason`: `VehicleBreakdown` (xe sẽ tự chuyển `Maintenance`), `AlreadyOnMission`, `TooFar`, `MedicalLeave`, `NoResponse`, `Other` (với `Other`, `notes` bắt buộc ≥ 5 ký tự). Được từ chối khi đang `Dispatched`, `Accepted` hoặc `EnRoute`. Nếu không còn xe nào khác đang xử lý, sự cố quay về `Verified` để Operator giao xe thay thế. Operator nhận qua `ReceiveAssignmentStatus` (`status: "Rejected"`, `rejectionReason`, `rejectionNotes`).

**c) Gợi ý xe (`GET /api/dispatch/recommend`) đã tính theo đúng SRS:**
- Load-Aware: `Cost = 0.5·D/R + 0.3·tải trạm + 0.2·nhiệm vụ trạm` (cost thấp xếp trước), giống `utils/dispatch.ts`.
- Bán kính tự mở rộng 10 → 15 → 22.5 → 30 km; sự cố `Escalated` thì ×2.
- Chỉ gợi ý xe `Available` đúng loại.
- `maxDistanceKm` giờ là tùy chọn: bỏ trống để backend tự mở rộng bán kính.
- Response thêm: `cost`, `stationLoad`, `stationActiveMissions`, `searchRadiusKm`, `radiusExpanded`; `score = (1 − cost) × 100`.

→ Web có thể **bỏ `utils/dispatch.ts`** và hiển thị trực tiếp kết quả API.

**d) Phân công (`DispatchAssignmentResponseDto`) có thêm:** `enRouteAt`, `dispatchAlgorithm`, `distanceKm`, `estimatedArrivalMinutes`, `assignedByUserId`, `rejectionReason`, `rejectionNotes`, `rejectedAt`, `completionReport { actionsSummary, victimsAssistedCount, equipmentUsed[], evidenceMediaUrls[], handover…, submittedAt }`.

**e) Tracking nhiều xe** (FR-CIT-03): `GET /api/dispatch/tracking/incident/{id}` thêm `units[]`. Mỗi phần tử gồm `assignmentId`, `rescueUnitId`, `plateNumber`, `unitTypeName`, `stationName`, `vehicleLat/Lng`, `routeTrajectory[]`, `distanceKm`, `estimatedArrivalMinutes`, `dispatchStatus` và các mốc thời gian. Các trường đơn cũ vẫn giữ (là xe đầu tiên). Level 3 nên vẽ **tất cả** xe trong `units`. Sửa type `CitizenTracking` ở mục 3.1.

**f) AI trả về nhánh sự kiện:** `aiClassification.eventType` (`FireExplosion` / `TrafficAccident` / `None`), `incidentType` (`fire`/`accident`/`none`), `isTrafficJam`. → Web `types/incident.ts:220`: dùng `eventType` thay cho việc đoán từ `hazardTags` (xem 5.1 #2); `None` → bắt Operator chọn nhánh.

**g) Hàng đợi** `GET /api/incidents/queue` (FR-OP-02):
- Level 0 và Level 3 **cùng nhóm cao nhất, FIFO**, rồi Level 2, rồi Level 1.
- Hàng đợi giờ có cả sự cố `AiProcessing` và sự cố `Escalated` chưa được duyệt.

→ Web đừng sắp xếp lại phía client.

**h) Settings** (thay mock `adminSettingsService.ts`, đúng hình dạng FE):

| Method | Endpoint | Quyền |
|---|---|---|
| GET / PUT | `/api/settings/escalation` → `{ dayThresholdMinutes, nightThresholdMinutes, nightStart, nightEnd, radiusMultiplier, maxRadiusKm }` | GET: Admin, Operator · PUT: Admin |
| GET / PUT | `/api/settings/dispatch` → `{ weights: { distance, stationLoad, activeMissions }, radiusStepsKm[], recoveryMinutes }` | như trên |
| GET | `/api/settings/defaults` → `{ escalation, dispatch }` | Admin, Operator |

GET trả `{ values, updatedAt, updatedByName }` (null = đang dùng mặc định). PUT kiểm tra giống `validateEscalationSettings` / `validateDispatchSettings` của FE; lỗi trả 400 kèm `errors[]`. **Đã bỏ `crewAckTimeoutSeconds`** (SRS bỏ đếm ngược 120s). Mặc định: ngày 5 phút, đêm 22:00–06:00 là 2.5 phút. Leo thang giờ quét **Level 0 và Level 3**.

**i) Chặn số spam** (FR-OP-03, BR-05) — thay mock `blockedNumberService.ts` và `incidentService.blockReporterPhone()`:

| Method | Endpoint | Quyền | Body |
|---|---|---|---|
| POST | `/api/incidents/{id}/block-reporter` | Operator, Admin | `{ duration, reason }` (reason ≥ 5 ký tự). Số đang bị chặn → 409 |
| GET | `/api/blocked-numbers?status=&search=&pageIndex=&pageSize=` | Admin, Operator | `status`: `Active` / `Expired` / `Removed` |
| POST | `/api/blocked-numbers/{id}/unblock` | Admin | `{ reason }` (≥ 5 ký tự) |
| POST | `/api/blocked-numbers/{id}/extend` | Admin | `{ duration, reason }`, tính lại từ thời điểm hiện tại |

Map `duration` giữa FE (`BlockDuration`) và backend: `'7d'` ↔ `SevenDays`, `'30d'` ↔ `ThirtyDays`, `'1y'` ↔ `OneYear`, `'permanent'` ↔ `Permanent`. Item trả về: `{ id, maskedPhone, duration, status, blockedAt, expiresAt, reason, incidentId, blockedByName, lastExtendedAt, removedAt, removedByName, removeReason }`, khớp `BlockedNumberListItem` của FE (status viết hoa chữ đầu).

**j) Khóa tài khoản:**
- **Đăng nhập sai 5 lần → khóa 15 phút** (MSG07): `POST /api/auth/login` trả 401 kèm message báo số phút còn lại.
- Admin khóa thủ công: `POST /api/users/{id}/lock` `{ reason }` (≥ 5 ký tự; thu hồi mọi phiên đăng nhập) và `POST /api/users/{id}/unlock`.
- `UserResponseDto` thêm `lockReason`, `lockedAt`, `lockoutEndUtc`.
- Map sang `AccountLock` của Web (`types/internalAccount.ts`):
  - `status === 'Suspended'` → `{ kind: 'admin', reason: lockReason, lockedAt }`
  - `lockoutEndUtc != null` → `{ kind: 'failed_login', until: lockoutEndUtc }`
  - còn lại → `{ kind: 'none' }`

**k) Phân quyền tài khoản:** Operator chỉ được tạo / sửa / gán trạm cho tài khoản **RescueStaff** (403 với role khác). Chỉ Admin tạo được Operator/Admin. Khớp ghi chú của FE "Admin không tạo được Admin khác"; nếu nhóm muốn Admin cũng không tạo được Admin thì báo backend.

### 6.3. 🔴 Lỗi mới tìm thấy ở Web (ngoài mục 5)

| # | Vị trí | Lỗi | Sửa |
|---|---|---|---|
| W1 | `services/apiClient.ts` → `getApiErrorMessage()` | Mọi lỗi **401** đều hiện "Email hoặc mật khẩu không chính xác.", kể cả khi token hết hạn ở các API khác | Chỉ dùng câu đó cho `/Auth/login`; ưu tiên `data.message` (đã có), API khác thì hiện "Phiên đăng nhập đã hết hạn" |
| W2 | `services/incidentService.ts:76` | Comment ghi *"Backend nhận thẳng một JSON string làm lý do"* nhưng code đã gửi `{ reason }` (đúng) | Sửa comment cho khỏi gây nhầm |
| W3 | `context/DispatchContext.tsx`, `utils/escalation.ts`, `utils/dispatch.ts` | Logic điều phối / leo thang / hồi sức chạy ở client (xem 5.2) | Backend đã làm đủ ở 6.1–6.2: chuyển sang dùng API + SignalR |

### 6.4. 🔴 Lỗi mới tìm thấy ở Mobile

| # | Vị trí | Lỗi | Sửa |
|---|---|---|---|
| M1 | `src/store/useAppStore.ts:9-12` (`mapBackendUser`) | `Operator` và `Admin` bị map thành `'staff'`, nên vào giao diện đội cứu hộ. SRS: mobile chỉ dành cho **Citizen** và **RescueStaff**. Operator vào màn Staff sẽ gọi các API staff và nhận 403 vì không thuộc trạm | Chỉ `RescueStaff` → `'staff'`; `Operator`/`Admin` hiện thông báo "Vui lòng dùng Web Command Center" rồi đăng xuất |
| M2 | `src/store/useAppStore.ts:21-22` | `teamName` lấy nhầm `stationName`; `staffId` giả (`ST-${role}`, `'ST-115'`) | Gọi `GET /api/users/my-team` sau khi đăng nhập: `myTeamName`, `stationName`, `stationVehicles` |
| M3 | `src/store/useAppStore.ts:100-106` (`switchDemoRole`) | Tạo token giả `demo-token-*`, nên mọi API trả 401 rồi interceptor cố refresh | Chỉ bật khi `__DEV__`, ẩn hẳn ở bản build thật |
| M4 | Hồ sơ y tế: `types/user.ts:221`, `ProfileEditModal.tsx`, `IceContactCard.tsx` | Danh bạ ICE chỉ có **1 liên hệ chính** qua các trường `emergencyContact*` của `/users/me`; chưa dùng API danh bạ nhiều người | Dùng `GET/POST /api/users/ice-contacts`, `PUT/DELETE /api/users/ice-contacts/{contactId}` (`{ name, phoneNumber, relationship, isPrimary }`) |
| M5 | `src/api/endpoints.ts` | Thiếu toàn bộ nhóm `DISPATCH` (staff), `TRAFFIC`, `RESCUE_UNITS` | Bổ sung theo bảng 6.6 |
| M6 | Màn Staff (`StaffDashboardScreen`, `IncidentOverviewScreen`, `MissionStepper`) | Toàn bộ là dữ liệu mẫu, chưa gọi API | Tích hợp `my-assignments`, `…/status`, `…/reject`, `…/resolve`, `PUT /api/rescue-units/{id}/location` hoặc SignalR `UpdateLocation` (chỉ gửi được vị trí xe của trạm mình) |

### 6.5. ⚪ Backend đang làm — FE cứ giữ mock, sẽ báo contract khi xong

| Hạng mục | Endpoint dự kiến |
|---|---|
| Tổ làm việc (`StationTeam`: tên, xe phụ trách, thành viên + `crewRole`, tối đa 1 tổ trưởng, 2–6 người) — khớp `types/stationTeam.ts` | `GET/POST /api/stations/{stationId}/teams`, `PUT/DELETE /api/stations/{stationId}/teams/{teamId}` |
| Lịch sử báo cáo của Citizen (FR-CIT-05) | `GET /api/incidents/my-reports?status=All\|Completed\|Cancelled` |
| Audit Trail | `GET /api/incidents/{id}/audit-trail`, `GET /api/audit-trail` (Admin) |
| Thông báo Staff/Citizen + realtime `ReceiveNotification` | `GET /api/notifications`, `PUT /api/notifications/{id}/read`, `PUT /api/notifications/read-all` |
| Push FCM (còi hú) | Dùng `fcmToken` đã đăng ký qua `PUT /api/users/fcm-token` |
| Quy tắc quản trị trạm/xe (xóa trạm → xe `OutOfService`, chặn xóa xe đang làm nhiệm vụ, biển số duy nhất, bán kính phủ sóng 5–50 km) | `/api/stations`, `/api/rescue-units` |
| Số liệu RQ3 (T_AI, T_Operator, T_Response, độ đồng thuận AI–Operator) | `GET /api/benchmark/telemetry-summary` |

### 6.6. 🟡 Bảng đầy đủ: backend đã có — Web / Mobile chưa dùng

| Phân hệ | Endpoint | Web | Mobile |
|---|---|:---:|:---:|
| Auth | `POST /api/auth/refresh` | ✅ | ✅ |
| Incidents | `GET /api/incidents/{id}` | ❌ | ❌ |
| Incidents | `PUT /api/incidents/{id}/lock`, `DELETE …/lock`, `POST …/reveal-phone`, `POST …/block-reporter` | ❌ | — |
| Incidents | `POST /api/incidents` (SOS), `PUT …/cancel` | ✅ | ❌ |
| Users | `GET /api/users/ice-contacts` (+ POST/PUT/DELETE) | ✅ | ❌ |
| Users (quản trị) | `GET/POST /api/users`, `PUT/DELETE /api/users/{id}`, `POST …/lock`, `POST …/unlock`, `PUT …/assign-station-team`, `GET /api/users/station/{stationId}` | ❌ (mock) | — |
| Users | `GET /api/users/my-team` | ❌ | ❌ |
| Dispatch | `GET /recommend`, `GET /available-units`, `POST /assign` | ❌ (mock) | — |
| Dispatch | `GET /my-assignments`, `PUT /assignments/{id}/status`, `POST …/reject`, `POST …/resolve`, `GET /assignments/{id}` | ❌ (mock) | ❌ (mock) |
| Dispatch | `GET /tracking/incident/{id}`, `GET /assignments/incident/{id}` | ❌ | ❌ |
| Rescue units | `GET/POST /api/rescue-units`, `GET/PUT/DELETE /{id}`, `GET /station/{stationId}`, `PUT /{id}/location`, `PUT /{id}/status` | ❌ (mock) | ❌ |
| Stations | `GET/POST /api/stations`, `GET/PUT/DELETE /{id}`, `GET /{id}/staff` | ❌ (mock) | — |
| Traffic | `POST /api/traffic/report`, `GET /api/traffic/active`, `PUT /api/traffic/{id}/resolve` | ❌ (mock `LiveMapPage.tsx:69`) | ❌ |
| Escalation | `GET/POST /api/escalation/rules`, `GET/PUT/DELETE /{id}`, `GET /logs`, `POST /trigger-check` | ❌ | — |
| Settings | `GET/PUT /api/settings/escalation`, `/dispatch`, `GET /defaults` | ❌ (mock) | — |
| Blocked numbers | `GET /api/blocked-numbers`, `POST /{id}/unblock`, `POST /{id}/extend` | ❌ (mock) | — |
| Dashboard | `GET /api/dashboard/stats` | ❌ (mock `overviewAdminService`) | — |
| AI | `POST /api/ai/analyze` | ❌ | ❌ |
| Localization | `GET /api/localization/info` | ❌ | ❌ |
| SignalR | `/hubs/incidents`, `/hubs/tracking` | ❌ | ❌ |

### 6.7. Đính chính các mục trước trong file này

- **Mục 1.1:** "Gửi `Unclassified` vào `verify` bị 400" là **quyết định thiết kế lệch SRS**: SRS (`TriageDecision.FinalSeverityLevel`) cho phép 0, còn backend quy định bác bỏ thì phải dùng `cancel`. Nhóm cần cập nhật SRS cho khớp.
- **Mục 3.1 / 3.3:** Citizen phải `SubscribeIncident` trên **cả** `/hubs/incidents` (nhận `ReceiveIncidentStatus`, `ReceiveAssignmentStatus`) **lẫn** `/hubs/tracking` (nhận `ReceiveRescueUnitLocation`); chỉ nghe tracking hub thì không biết khi nào xe đổi trạng thái. Bỏ câu "Citizen theo dõi xe (không bắt buộc đăng nhập)": giờ bắt buộc token (6.1 #5).
- **Mục 3.2:** bổ sung ràng buộc FR-CIT-02 / FR-CIT-01 cho Mobile: mô tả ≤ 200 ký tự, video ≤ 10 giây, nhấn giữ nút SOS 1.5 giây (`SosPulseButton.tsx`).
- **Mục 3.4:** các dòng Admin "Leo thang / Tài khoản / Xe" trước đây ghi "backend đã có API" là chưa chính xác. Leo thang dùng `/api/settings/escalation` (6.2 h), tài khoản cần thêm `lock`/`unlock` (6.2 j). Xe còn thiếu `crewMemberId`, `maintenanceSince`, `PATCH station`: chưa có trong kế hoạch, cần chốt với backend.
- **Mục 5.3:** thông tin bàn giao liên ngành (đơn vị nhận, người liên hệ, SĐT) **đúng SRS BR-26, giữ lại**; chỉ **chữ ký** và **QR** là ngoài phạm vi.
- **SRS cần cập nhật theo code** (gửi người viết tài liệu):
  - Route trong SRS dạng `/api/v1/...` (`incidents/report`, `map/snapshot`, `location/update`, `assignments/{id}/accept`) ≠ code `/api/...`.
  - Tên sự kiện SignalR (`NewIncident`, `IncidentStatusChanged`, `UnitLocationUpdated`, `EmergencyHub`, `incident_{id}`) ≠ code (`ReceiveNewIncident`, `ReceiveIncidentUpdate`, `ReceiveRescueUnitLocation`, `incident:{id}`).
  - Loại xe: FR-AD-01 và FR-OP-01 liệt kê khác nhau, SRS tự mâu thuẫn.
  - Ngưỡng leo thang: FR-SYS-02 ghi 5 phút, BR-17/MSG46 ghi 3 phút. Đã chốt: **cấu hình qua Settings, mặc định 5 / 2.5 phút**.


---

## 7. Cập nhật đợt 3 (21/10/2026): các mục ở 6.5 backend đã làm xong, kèm contract

> ⚠️ Vẫn **chưa áp migration lên Neon**. Có thêm migration `Missing_Modules`, nên tổng cộng còn 4 migration chờ backend chạy. Migration này **tự chuyển tên tổ đang lưu trong `DependentsNote`** của RescueStaff sang bảng tổ mới.

### 7.1. 🟡 Tổ làm việc (StationTeam) — thay mock `services/stationTeamService.ts`

| Method | Endpoint | Quyền |
|---|---|---|
| GET | `/api/stations/{stationId}/teams` | Admin, Operator, RescueStaff |
| POST | `/api/stations/{stationId}/teams` | Admin, Operator |
| PUT | `/api/stations/{stationId}/teams/{teamId}` | Admin, Operator — gửi **đầy đủ** danh sách thành viên mong muốn |
| DELETE | `/api/stations/{stationId}/teams/{teamId}` | Admin, Operator (thành viên vẫn thuộc trạm) |

Body POST/PUT (khớp `StationTeamInput` của FE, đổi tên trường cho đúng backend):
```json
{
  "name": "Tổ phản ứng nhanh số 1",
  "rescueUnitId": "…hoặc null nếu trực tại trạm",
  "members": [
    { "staffId": "…", "crewRole": "Commander" },
    { "staffId": "…", "crewRole": "Driver" }
  ]
}
```
Response `StationTeamDto`: `{ id, stationId, name, rescueUnitId, rescueUnitPlate, members: [{ staffId, fullName, phoneNumber, crewRole }], isUnderstaffed }`.

| FE (`types/stationTeam.ts`, `shift.ts`) | Backend |
|---|---|
| `vehicleId` | `rescueUnitId` |
| `CrewRole`: `commander`, `driver`, `firefighter`, `rescuer`, `medic` | `Commander`, `Driver`, `Firefighter`, `Rescuer`, `Medic` |
| `StationStaff.teamId` | lấy từ `members` của các tổ, hoặc `teamName` trong `GET /api/users/station/{stationId}` |

Quy tắc kiểm tra giống bản mock của FE:
- Tên tổ không trùng trong trạm (409).
- Tối đa 6 người; tối đa 1 `Commander`.
- Thành viên phải là RescueStaff đang hoạt động của trạm.
- Mỗi người chỉ thuộc 1 tổ (409); mỗi xe chỉ có 1 tổ phụ trách (409).
- Dưới 2 người thì `isUnderstaffed = true` (chỉ cảnh báo).

Thêm/bớt nhân viên khỏi **trạm** vẫn dùng `PUT /api/users/{id}/assign-station-team` `{ stationId }` (bỏ trống `teamName`). Gửi `teamName` vẫn được: backend tự tạo tổ theo tên nếu chưa có, để tương thích ngược.

`GET /api/users/my-team` có thêm `myTeamId`, `myCrewRole`, `myTeamRescueUnitId`, `myTeamRescueUnitPlate`, `myTeamMembers[]` (đồng đội cùng tổ). `teamMembers[]` vẫn là toàn bộ nhân sự của trạm. → Mobile M2 (6.4) dùng các trường này.

`GET /api/stations/{id}/staff` giờ **bắt buộc đăng nhập** (Admin/Operator/RescueStaff); trước đây ai cũng xem được email và SĐT nhân viên.

### 7.2. 🟡 Lịch sử báo cáo của Citizen (FR-CIT-05) — thay cho cách gọi `GET /api/incidents` ở 6.1 #1

```http
GET /api/incidents/my-reports?status=All|Completed|Cancelled&pageIndex=1&pageSize=10
```
Chỉ dành cho **Citizen** đã đăng nhập; mới nhất trước. Mỗi item `MyReportDto` gồm:
- Thông tin sự cố: `id`, `title`, `description`, `locationAddress`, `status`, `severity`, `resolutionSummary`, `cancellationReason`, `mediaUrls[]`.
- Mốc thời gian: **`reportedAt`, `verifiedAt`, `dispatchedAt`, `onSceneAt`, `completedAt`**.
- `dispatchedUnits[]`: `{ plateNumber, unitType, stationName, status }`.

→ Web `CitizenReportsModal.tsx`, Mobile `MyReportsScreen.tsx`.

### 7.3. 🟡 Audit Trail — thay mock `services/admin/auditTrailService.ts`

| Method | Endpoint | Quyền |
|---|---|---|
| GET | `/api/incidents/{id}/audit-trail?pageIndex=&pageSize=` | Operator, Admin |
| GET | `/api/audit-trail?incidentId=&actorId=&action=&from=&to=&pageIndex=&pageSize=` | Admin |

Item: `{ id, incidentId, incidentTitle, actorId, actorName, actorRole, action, description, oldStateJson, newStateJson, timestamp }`. `from > to` → 400 (giống câu báo lỗi FE đang dùng). Danh sách `action`:

`Created`, `AiAnalyzed`, `Verified`, `SeverityOverridden`, `Dispatched`, `AssignmentAccepted`, `AssignmentRejected`, `StatusChanged`, `Escalated`, `DeEscalated`, `Cancelled`, `Completed`, `Closed`, `ReDispatched`, `PhoneRevealed`, `ReporterBlocked`

⚠️ Audit Trail của backend **chỉ ghi các thao tác trên sự cố**. Các thao tác quản trị (sửa trạm/xe/tài khoản/settings) mà mock FE đang ghi vào `auditTrailService.record()` **chưa có** trong backend. Nếu nhóm cần thì báo backend làm thêm `SystemAuditLog` (BR-AD-06).

### 7.4. 🟡 Thông báo (Notifications) — Staff & Citizen

| Method | Endpoint |
|---|---|
| GET | `/api/notifications?unreadOnly=false&pageIndex=1&pageSize=20` |
| GET | `/api/notifications/unread-count` |
| PUT | `/api/notifications/{id}/read` |
| PUT | `/api/notifications/read-all` |

- Item: `{ id, type, title, message, incidentId, dataJson, isRead, readAt, createdAt }`.
- `type`: `NewIncident`, `JobAssigned`, `StatusUpdate`, `Escalation`, `System`.
- **Realtime:** sự kiện **`ReceiveNotification`** trên `/hubs/incidents`, gửi riêng tới đúng người dùng theo token. Không cần `Subscribe`.

Backend tự tạo thông báo khi:

| Sự kiện | Người nhận | `type` | Push ưu tiên cao |
|---|---|---|:---:|
| Operator xác minh sự cố | Người báo | `StatusUpdate` | |
| Giao xe | **Toàn bộ RescueStaff của trạm có xe** | `JobAssigned` | ✅ |
| Giao xe | Người báo ("Đã điều động…") | `StatusUpdate` | |
| Xe `EnRoute` / `OnScene` | Người báo (MSG37 / MSG40) | `StatusUpdate` | |
| Xe từ chối nhiệm vụ | Operator + Admin | `StatusUpdate` | ✅ nếu không còn xe nào |
| Toàn bộ xe nghiệm thu xong | Người báo | `StatusUpdate` | |
| Leo thang | Operator + Admin | `Escalation` | ✅ |

→ Web Staff `StaffMissionAlertModal.tsx`, Mobile `StaffNotificationsModal.tsx`: thay dữ liệu mẫu bằng API + `ReceiveNotification`.

### 7.5. 🟡 Push FCM (còi hú)

- Mobile cần đăng ký token qua `PUT /api/users/fcm-token` sau khi đăng nhập (endpoint đã có).
- Payload `data`: `{ type, incidentId, notificationId }`.
- Lệnh điều phối và leo thang gửi `priority: high`, Android `channelId = "EMERGENCY_DISPATCH"`, `sound = "emergency_alarm"` (iOS: `emergency_alarm.caf`). → **Mobile cần tạo notification channel `EMERGENCY_DISPATCH` và đóng gói file âm thanh `emergency_alarm`**.
- Backend chỉ gửi push thật khi đã cấu hình `Firebase:CredentialPath` (file service-account). Hiện **chưa cấu hình**, nên backend chỉ ghi log; thông báo trong ứng dụng và realtime vẫn hoạt động bình thường.

### 7.6. 🟡 Quy tắc quản trị trạm / xe (FR-AD-01) — Web Admin

| Thay đổi | Việc FE cần làm |
|---|---|
| `POST/PUT /api/stations` nhận thêm **`coverageRadiusKm`** (bắt buộc 5–50, mặc định 10); `StationResponseDto` trả `coverageRadiusKm` | Map với `AdminStation.coverageRadiusKm` (`types/adminFleet.ts`) |
| Xóa trạm (soft delete): **400** nếu còn xe đang làm nhiệm vụ. Thành công thì mọi xe của trạm chuyển `OutOfService`, nhân sự bị gỡ khỏi trạm và tổ (BR-AD-02/03) | Bản mock FE đã có logic tương tự (`StationDeleteResult.affectedVehicleIds`): sau khi xóa, tải lại danh sách xe |
| `PUT /api/rescue-units/{id}` và `PUT /{id}/status`: xe đang làm nhiệm vụ (`Dispatched`/`OnScene`/`Returning` hoặc còn phân công) **không đổi trạm / trạng thái được** (400). Trạng thái `Dispatched`, `OnScene`, `CoolDown` do hệ thống tự đặt; Admin chỉ đặt được `Available` / `Maintenance` / `OutOfService` / `Returning` | Ẩn các lựa chọn đó trong form đổi trạng thái xe |
| `DELETE /api/rescue-units/{id}` đang làm nhiệm vụ → 400 | Hiện `message` |

Các trường FE có nhưng backend **không làm** trong đợt này: `crewMemberId`, `maintenanceSince` (`AdminVehicle`), `headOperatorId` (trưởng trạm). Nhóm cần chốt có giữ hay bỏ (xem 5.3).

### 7.7. 🟡 Số liệu thực nghiệm RQ2/RQ3 — Web Admin "Tổng quan" / trang nghiên cứu

```http
GET /api/benchmark/telemetry-summary?from=2026-10-01T00:00:00Z&to=2026-10-31T23:59:59Z
```
Quyền: Admin, Operator. Trả về:
- Các chỉ số độ trễ: `tAi`, `aiModelLatency`, `tOperator`, `tOperatorWithAi`, `tOperatorManual`, `tDispatch`, `tResponse`, `tResolution`. Mỗi chỉ số có dạng `{ count, avgMs, medianMs, p95Ms, minMs, maxMs }`.
- `tResponseByAlgorithm`: `{ "NearestAvailable": {...}, "LoadAware": {...}, "Unknown": {...} }`. Để so sánh được RQ2, FE **phải gửi `algorithm` khi `assign`** (6.1 #12).
- `aiAgreement`: `{ sampleCount, accuracy, cohensKappa, overrideRate, labels: ["Unclassified","Level1","Level2","Level3"], confusionMatrix[ai][operator] }`.

### 7.8. Cập nhật bảng 6.6
Các mục ⚪ ở 6.5 nay đã có API thật: StationTeam, `my-reports`, `audit-trail`, `notifications` (cùng `ReceiveNotification`), quy tắc quản trị, `benchmark/telemetry-summary`. Push FCM đã có code, đang chờ cấu hình.
