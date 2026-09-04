# IT 自動化 API (IT-Automation-API1)

這是一個 .NET 8 Web API 專案，作為 IT 自動化任務的後端服務中心。提供一套 RESTful API 端點，涵蓋 Active Directory、VMware vCenter、Veeam 備份，以及 VM / AD / DB / 應用系統四大帳號稽核功能。

此專案採用系統環境變數配置架構，以確保「正式環境」的機密（如資料庫連線字串、服務帳號密碼）遵循雲端原生安全標準進行管理。

> 🤖 **AI 輔助開發參考指引**
>
> 若您是 AI 助手（如 Copilot、Antigravity 等）正在協助開發本專案，
> 除了閱讀本 README 外，**務必同時參考 `相關文件紀錄/` 目錄下的補充文件**，
> 以獲得更完整的專案脈絡：
>
> | 文件 | 用途 |
> |------|------|
> | `專案 API 架構 (文字模式).txt` | 完整的檔案架構圖、API 端點一覽、歷次重構與功能強化紀錄 |
> | `VMware硬體與ESXi實體規格整合報表規格驅動開發文件.md` | VMware 整合硬體與 ESXi 實體主機資源查詢之 SDD 規格驅動開發文件 |
> | `Scaffold Record 操作方法.txt` | EF Core Database-First 的 Scaffold 指令與注意事項 |
> | `設定系統全域環境變數.txt` | 正式環境部署時的環境變數設定 SOP |
> | `正式伺服器部署標準作業流程 (SOP).docx` | 完整的 IIS 部署流程 |
> | `Template/` | 報表範本 Excel 檔案，供美化輸出時參考 |
>
> 這些文件包含了程式碼以外的設計決策、部署慣例與歷史記錄，
> 能幫助您做出更符合專案規範的建議。

## 目錄

- [專案架構](#專案架構)
- [核心功能](#核心功能)
- [技術棧](#技術棧)
- [安裝與設定](#安裝與設定)
- [安全組態設定 (環境變數方案)](#安全組態設定-環境變數方案)
- [API 端點](#api-端點)
- [開發指南](#開發指南)
- [部署](#部署)
- [故障排除](#故障排除)
- [授權](#授權)

## 專案架構

### API (主專案)

* .NET 8.0 Web API 專案 (`net8.0-windows`)
* 負責處理所有 HTTP 請求、日誌記錄 (NLog) 和依賴注入 (DI)
* 啟動時 (`Program.cs`) 判斷環境，並動態載入設定檔或環境變數
* 使用系統環境變數進行正式環境的機密設定管理

## 核心功能

### Active Directory (LDAP) 服務 (`ADController`)

* **使用者驗證：** 使用者帳號/密碼驗證 (`AuthenticateLdapUserAsync`)
* **密碼到期通知：** 自動檢查 AD 帳號密碼到期日並透過 Email 發送通知 (`CheckPasswordStatusAndNotifyAsync`)
* **使用者同步：** 將 AD 使用者帳號（包含群組隸屬）同步至 `LdapUserHistory` 資料庫 (`SyncUsersToDatabaseAsync`)
* **稽核報表：** 產生 AD 帳號清查稽核報表 (Excel) (`GenerateAuditReportDataAsync`)
* **使用者管理：** 建立 (`CreateLdapUserAsync`) 和更新 (`UpdateLdapUserAsync`) AD 使用者（未正式啟用）
* **FEB CMS 整合：** 自動同步 AD 使用者狀態（離職/職稱）至 `FEB_CMS` 資料庫 (`SyncUsersFromAdAsync`)

### 帳號清查服務 (`AccountAuditController`) — 四大稽核功能

* **VM 本機帳號稽核：** 觸發 PowerShell 腳本透過 vCenter 盤點各 VM 的本機帳號，以 Upsert 邏輯同步至 `AuditVmAccountHistory`，支援軟刪除與手動維護主機（`IsManual`）豁免
* **AD 帳號稽核：** 透過 LDAP 取得 AD 帳號，匯出含特權帳號標記、名稱空白帳號分頁的 Excel 稽核報表
* **DB 帳號稽核：** 執行 SQL 腳本盤點指定 SQL Server 的登入帳號，自動帶入承辦人（`Assignee`）與說明，支援人工記錄保護欄位不被覆蓋
* **應用系統帳號稽核：** 採**組態驅動**設計，透過 `appsettings.json` 定義各應用系統的連線字串與 SQL 查詢，無需改程式碼即可新增系統。目前支援 `FEB_CMS`，報表格式為「組室、帳號、姓名、清查結果」。
* **稽核決策更新（骨架）：** 提供 PATCH 端點預備接收未來稽核管理 UI 的決策寫入（`ActionDecision`），目前暫不對外公開

### VMware 服務 (`VMwareController`)

* **虛擬機查詢：** 透過 vCenter API 獲取指定環境（正式/測試）的虛擬機 (VM) 列表及其電源狀態 (`GetVmsAsync`)
* **整合硬體配置報告：** 透過 vCenter REST API 獲取所有 VM 之記憶體、vCPU、Guest OS 與網段優先排序 IP，並透過底層原生 SOAP Web Service (`/sdk`) 查詢 ESXi 實體主機之硬體 RAM 與 CPU 核心數，自動依「非維護模式」與「搭載 VM 數 > 0」進行嚴格篩選，產出包含實體/虛擬雙層資源對比之視覺化 HTML 報告 (`GetVMsHardwareReport`)

### Veeam 服務 (`VeeamController`)

* **備份日誌查詢：** 從資料庫讀取 Veeam 備份工作階段的日誌 (`GetBackupSessionsAsync`)

### 通用服務 (`Classes/`)

* **郵件服務 (`MailSend`)：** 透過 SMTP (Mail Relay) 發送郵件
* **Excel 服務 (`ExcelService`)：** 使用 EPPlus.Free 產生 Excel 報表，統一 VM / AD / DB 三份報表的視覺風格（標楷體、大標題、狀態色彩標記）

## 技術棧

* **框架：** ASP.NET Core 8.0 (Windows)
* **語言：** C# (.NET 8.0)
* **資料庫：** 
  - Microsoft SQL Server (Entity Framework Core 9.0)
  - SQLite (開發環境測試用)
* **目錄服務：** Novell LDAP (Active Directory 整合)
* **日誌記錄：** NLog 6.0
* **文件產生：** EPPlus.Free (Excel)
* **郵件服務：** NETCore.MailKit、MimeKit
* **API 文件：** Swagger/OpenAPI (Swashbuckle.AspNetCore)
* **虛擬化管理：** VMware vCenter REST API & 原生 SOAP Web Service (/sdk, RetrieveProperties)

## 安裝與設定

### 1. 複製專案

```bash
git clone <repository-url>
cd API
```

### 2. 還原 NuGet 套件

```bash
dotnet restore
```

### 3. 設定開發環境組態

1. 複製範本檔案：
   ```bash
   copy appsettings.Development_參考.json secrets.dev.json
   ```

2. 編輯 `secrets.dev.json`，填入您的開發環境設定：
   - 資料庫連線字串
   - LDAP/AD 設定和憑證
   - VMware vCenter 設定和憑證
   - SMTP 郵件伺服器設定

> ⚠️ **重要：** `secrets.dev.json` 是本機開發專用的明碼檔案，已加入 `.gitignore`。

### 4. 資料庫遷移（如需要）

```bash
dotnet ef database update --context MISContext
dotnet ef database update --context FEB_CMSContext
```

### 5. 執行應用程式

```bash
dotnet run
```

應用程式將在 `https://localhost:5001` 或 `http://localhost:5000` 啟動。

### 6. 存取 Swagger UI

開啟瀏覽器並導航至：
```
https://localhost:5001/swagger
```

## 安全組態設定 (環境變數方案)

本專案不再使用加密檔案 (DPAPI)，而是將敏感機密（密碼、連線字串）透過環境變數進行隔離管理：

### 1. 開發環境 (Development)

* **觸發方式：** `ASPNETCORE_ENVIRONMENT` 環境變數**未設定**或設定為 `Development`
* **載入檔案：** `secrets.dev.json` (明碼)
* **安全性：** 此檔案被 `.gitignore` 忽略，**永遠不會**被 Commit

### 2. 正式環境 (Production)

* **觸發方式：** 需將機器的 `ASPNETCORE_ENVIRONMENT` 環境變數設定為 `Production`。
* **變數存放位置 (設定點)：** 
  - 本專案統一依賴 Windows 的 **系統環境變數 (Machine-level System Environment Variables)**。
  - 您可以透過 GUI 檢視：`控制台` -> `系統及安全性` -> `系統` -> `進階系統設定` -> `環境變數` -> 下方的 **「系統變數」** 區塊。
  - **重要**：設定或更新此區塊的變數後，IIS 通常需要重新啟動 (`net stop was /y & net start w3svc`) 才能載入最新的環境變數。
* **優點：** 遵循雲端部署的 12-Factor App 標準，密碼與機密設定 (secrets) 不落地儲存於專案的任何實體檔案中。
* **管理工具：** 為了避免手動新增變數容易出錯，我們推薦使用內附的 `批次檔/Set-EnvVariables.ps1` 進行自動化注入。該腳本會使用 PowerShell API 直接將您設定的參數寫入 Windows 的「系統變數」中，包含 `ASPNETCORE_ENVIRONMENT`。

> ⚠️ **【重要】PowerShell 中文亂碼問題（UTF-8 BOM）**
>
> `Set-EnvVariables.ps1` 必須以 **UTF-8 with BOM** 格式儲存，PowerShell 5.1（Windows Server 環境）才能正確顯示中文。
>
> **每次使用 AI 工具重新生成或覆寫此 `.ps1` 檔案後，都必須重新執行以下 BOM 轉換指令**，  
> 因為 AI 工具預設儲存的是 UTF-8 without BOM：
>
> ```powershell
> $f = "批次檔\Set-EnvVariables.ps1" # 調整為實際路徑
> [System.IO.File]::WriteAllText($f, (Get-Content $f -Raw -Encoding UTF8), (New-Object System.Text.UTF8Encoding $true))
> ```
>
> 腳本本身也已內建 `chcp 65001` 與 `[Console]::OutputEncoding = UTF8` 雙重保護，但檔案本身的 BOM 仍需手動確認。

### 3. 環境變數命名規則

當您需要將 `secrets.dev.json` 裡的巢狀結構轉為環境變數時，請使用 **雙底線 `__`** 代替冒號。
例如：`ConnectionStrings:MISContext` 應設定為 `ConnectionStrings__MISContext`。

## API 端點

### Active Directory (AD) 端點 (`/AD/`)

> ⚠️ **注意：** ADController 的路由前綴為 `/AD/`（沒有 `/api/` 前綴），與其他 Controller 不同。

| 方法 | 端點 | 說明 |
|------|------|------|
| GET  | `/AD/UserLdapAuth` | 驗證 LDAP 使用者帳號密碼 (傳入帳號密碼) |
| GET  | `/AD/LdapAuth` | 驗證 LDAP 使用者帳號密碼 |
| GET  | `/AD/LdapSSLAuth` | 驗證 LDAP 使用者帳號密碼 (透過 SSL) |
| GET  | `/AD/UserPwdLastSetCheck` | 檢查所有 AD 帳號密碼到期狀態並發送通知 |
| GET  | `/AD/UserPwdLastSetCheckOne` | 檢查【單一】AD 帳號密碼到期狀態並發送通知 |
| GET  | `/AD/CheckFebCmsUserFromAD` | 產生離職及非約聘僱員工清單 (與 FEB CMS 關聯) |
| GET  | `/AD/SyncAdUsersToDatabase` | 同步 AD 使用者歷史紀錄至資料庫 |
| GET  | `/AD/GetAdUserInfoHTML` | 取得 AD 使用者清單 (HTML 頁面格式) |
| GET  | `/AD/AdAuditReport` | 產生並下載 AD 帳號稽核報表 (Excel 格式) |
| POST | `/AD/UserLdapCreate` | 建立新的 AD 使用者 (預設未啟用) |
| PUT  | `/AD/UserLdapEdit/{username}` | 更新 AD 使用者資料 (預設未啟用) |

### 帳號清查端點 (`/api/AccountAudit/`)

| 方法 | 端點 | 說明 |
|------|------|------|
| GET   | `/api/AccountAudit/Database/Export` | 一次掃描全部 DB 伺服器 (225、226、206)，產出含三個獨立頁籤的 Excel 報表 |
| GET   | `/api/AccountAudit/VM/Export` | 觸發 PowerShell 掃描 VM 本機帳號並下載 Excel 報表 |
| GET   | `/api/AccountAudit/AD/Export` | 掃描 AD 帳號並下載 Excel 報表 |
| GET   | `/api/AccountAudit/App/{systemKey}/Export` | 掃描指定應用系統帳號並下載 Excel 報表；`systemKey` 對應 `appsettings.json` 的 `AppAuditSettings`，目前支援 `FEB_CMS` |
| PATCH | `/api/AccountAudit/db/{id}/decision` | ⚙️ 更新 DB 帳號的稽核決策（ActionDecision）與承辦人（Assignee），待搭配管理 UI 後正式啟用 |

### VMware 端點 (`/api/VMware/`)

| 方法 | 端點 | 說明 |
|------|------|------|
| GET  | `/api/VMware/GetVMsList` | 取得 VMware 虛擬機狀態報表 (帶入參數 ?val=1 或 2) |
| GET  | `/api/VMware/GetVMsHardwareReport` | 取得 VMware 整合硬體配置報告（ESXi 實體總 RAM/總 CPU、排除維護與無 VM 主機、各 VM 記憶體/vCPU/Guest OS/多 IP 排序與對比）(?val=1 正式, 2 測試) |

### Veeam 端點 (`/api/Veeam/`)

| 方法 | 端點 | 說明 |
|------|------|------|
| GET  | `/api/Veeam/GetVeeamBackupSessions` | 取得 Veeam 備份工作階段結果 |
| GET  | `/api/Veeam/GetVeeamBackupSessionsHTML` | 取得 Veeam 備份工作階段結果 (HTML 格式) |

> 📖 **完整的 API 文件請參考 Swagger UI：** `https://localhost:5001/swagger`

## 開發指南

### 專案結構

```
API/
├── Classes/                    # 共用類別（郵件、Excel、LDAP 等）
│   ├── LDAP/                   # LDAP 設定模型與擴充方法
│   ├── Reporting/              # Excel 報表服務 (ExcelService.cs)
│   └── VMware/                 # VMware 設定模型
├── Controllers/                # API 控制器
│   ├── AccountAuditController.cs  # 帳號清查統一入口 (VM/AD/DB/App)
│   ├── ADController.cs
│   ├── VMwareController.cs
│   ├── VeeamController.cs
│   └── WMIController.cs
├── DataModels/                 # DTOs & ViewModels
├── Models/                     # Entity Framework 資料庫實體
│   ├── AppAudit/               # 應用系統稽核設定強型別模型
│   │   └── AppAuditSettings.cs # 對應 appsettings.json -> AppAuditSettings
│   ├── FEB_CMS/                # FEB_CMS 資料庫實體
│   └── MIS/                    # MIS 資料庫實體 (稽核 Models)
│       ├── AuditAdAccountHistory.cs   # AD 帳號清查歷史
│       ├── AuditAppAccountHistory.cs  # 應用系統帳號清查歷史
│       ├── AuditDbAccountHistory.cs   # DB 帳號清查歷史
│       ├── AuditVmAccountHistory.cs   # VM 本機帳號清查歷史
│       └── MISContext.cs
├── 批次檔/                     # 自動化腳本與環境變數工具
│   ├── Audit_VM_LocalAccounts_API.ps1 # VM 本機帳號清查 (API 呼叫版)
│   └── Set-EnvVariables.ps1           # 正式環境變數注入腳本
├── Services/                   # 核心業務邏輯層
│   ├── AppAudit/               # 應用系統帳號清查服務
│   │   ├── IAppAuditService.cs # 服務介面（組態驅動設計）
│   │   └── AppAuditService.cs  # 服務實作（ADO.NET 直連 + Upsert）
│   ├── Database/               # DB 帳號清查服務
│   ├── FEB_CMS/
│   ├── LDAP/                   # AD/LDAP 服務
│   ├── Veeam/
│   └── VMware/                 # VM 查詢 + VM 帳號清查服務
│       ├── VmAuditService.cs   # PowerShell 執行與 Upsert 邏輯
│       └── VMwareService.cs
├── Logs/                       # NLog 日誌輸出目錄
├── Program.cs                  # 應用程式進入點
├── nlog.config                 # NLog 設定檔
├── appsettings.json            # 所有功能設定（含 AppAuditSettings）
└── secrets.dev.json            # 開發環境機密（不納入版控）
```


### 依賴注入 (DI)

本專案使用 ASP.NET Core 內建的 DI 容器，所有服務都在 `Program.cs` 中註冊：

```csharp
// 服務註冊範例
builder.Services.AddScoped<ILdapService, LdapService>();
builder.Services.AddScoped<IVMwareService, VMwareService>();
builder.Services.AddScoped<IMailSend, MailSend>();
```

### 日誌記錄

使用 NLog 進行日誌記錄，設定檔位於 `nlog.config`。日誌將輸出至 `Logs/` 目錄，並按日期滾動。

### 新增 API 端點

1. 在 `Controllers/` 建立新的控制器
2. 在 `Services/` 建立對應的服務介面和實作
3. 在 `Program.cs` 註冊服務到 DI 容器
4. 使用 XML 註解來產生 Swagger 文件

## 部署

### Windows Server / IIS 部署

1. **發佈專案：**
   ```bash
   dotnet publish -c Release -o ./publish
   ```

2. **設定環境變數：**
   - 以管理員身分執行 `批次檔/Set-EnvVariables.ps1`。
   - 依照腳本提示輸入各項正式機參數。
   - 此動作會自動設定 `ASPNETCORE_ENVIRONMENT=Production` 以及所需的連線字串。

3. **設定 IIS：**
   - 安裝 .NET 8.0 Hosting Bundle。
   - 建立網站並指向發佈目錄。
   - **重要：** 設定或更新環境變數後，必須重新啟動 IIS 應用程式集區 (Recycle App Pool) 才能讓應用程式偵測到新值。

### Windows 服務部署

可以將 API 註冊為 Windows 服務，實現開機自動啟動。相關批次檔位於 `批次檔/` 目錄。

## 故障排除

### 常見問題

#### 1. 環境變數未生效 (正式環境)

**錯誤訊息：** `ArgumentNullException: Value cannot be null. (Parameter 'connectionString')`

**解決方案：**
- 確認您已執行 `批次檔/Set-EnvVariables.ps1`。
- 檢查 Windows 系統進階設定中的環境變數是否已包含 `ConnectionStrings__MISContext` 等項目。
- **必須重新啟動 IIS 應用程式集區**。

#### 2. LDAP 連線失敗

**錯誤訊息：** `LDAP 伺服器連線逾時`

**解決方案：**
- 檢查網路連線和防火牆設定
- 確認 LDAP 伺服器位址 (Host) 正確
- 驗證服務帳號 (AdminBaseDC) 的憑證是否有效

#### 3. 資料庫連線失敗

**錯誤訊息：** `無法連線至 SQL Server`

**解決方案：**
- 檢查環境變數中的連線字串格式是否正確。
- 確認 SQL Server 服務正在執行，且防火牆允許連線。

#### 4. NLog 日誌未產生

**解決方案：**
- 確認 `Logs/` 目錄存在且 IIS 帳號 (如 `IIS AppPool\YourPoolName`) 有寫入權限。
- 檢查 `nlog.config` 設定是否正確。

### 日誌位置

* **應用程式日誌：** `Logs/` 目錄
* **IIS 日誌：** `C:\inetpub\logs\LogFiles\`
* **Windows 事件檢視器：** 應用程式日誌

## 授權

內部專案，不提供使用。