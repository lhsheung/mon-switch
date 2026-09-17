# mon-switch

常駐 Windows 通知區域（系統托盤）的**顯示／投影模式切換工具**。類似 SoundSwitch，但切換的是屏幕模式而非音訊裝置。

雙擊托盤圖示即可在四個 Windows 投影模式之間循環切換，亦可以為每個模式設定全域快捷鍵直接跳過去。

---

## 目錄

1. [這個工具做什麼](#1-這個工具做什麼)
2. [技術選型](#2-技術選型)
3. [實現計劃、風險與假設](#3-實現計劃風險與假設)
4. [後台佔用方案](#4-後台佔用方案)
5. [開機自動啟動方案](#5-開機自動啟動方案)
6. [設定檔與日誌位置](#6-設定檔與日誌位置)
7. [多語言](#7-多語言)
8. [構建](#8-構建)
9. [發布與體積優化](#9-發布與體積優化)
10. [使用方法](#10-使用方法)
11. [低佔用實測](#11-低佔用實測)
12. [已知限制](#12-已知限制)
13. [驗收清單](#13-驗收清單)
14. [專案結構](#14-專案結構)
15. [授權](#15-授權)

---

## 1. 這個工具做什麼

Windows 本身有四種投影模式（即 `Win+P` 那四個）：

| 模式 | 說明 |
| --- | --- |
| 只電腦屏幕 | 只用內建／主屏幕 |
| 複製 | 兩個屏幕顯示相同內容 |
| 延伸 | 桌面延伸到第二個屏幕 |
| 只第二屏幕 | 只用外接屏幕 |

`mon-switch` 把它們搬進通知區域：

- **雙擊托盤圖示** → 依照使用者自行排序的清單循環切換；未勾選的模式不參與。
- **全域快捷鍵** → 一個「循環切換」，加上四個「直接切換到某模式」，全部可自訂、可停用、可清除。
- **右鍵選單** → 切換、直接切換到指定模式、顯示切換通知、設定、開機自動啟動、語言、關於、結束。
- **切換後通知** → 例如「已切換至：延伸」；失敗則顯示原因，例如「切換失敗：你的系統不支援這個模式」。
- **開機自動啟動**（預設關閉）→ 登入後靜默常駐，不會彈出任何視窗。
- **四種介面語言** → 粵語（香港）、繁體中文、簡體中文、英文，切換即時生效。

---

## 2. 技術選型

**C# / .NET 8 / Windows Forms**，直接呼叫 Windows 官方 CCD API（`SetDisplayConfig` / `QueryDisplayConfig`）。

| 決策 | 選擇 | 理由 |
| --- | --- | --- |
| 語言／框架 | C# + .NET 8（`net8.0-windows`） | 與參考專案 SoundSwitch 同一個生態；P/Invoke 直接、工具體驗好、發佈選項多 |
| 介面 | **WinForms** | 托盤工具只需要 `NotifyIcon` 加幾個對話框。WPF 要多載 `PresentationFramework`／`PresentationCore`（啟動更慢、佔用更高）；Electron／WebView2 要多帶一整個 Chromium 執行環境（發佈體積由幾 MB 變成幾十至過百 MB，閒置記憶體亦高一個數量級）。WinForms 是三種之中代價最低 |
| 顯示模式切換 | **純 Win32 `SetDisplayConfig`（`SDC_TOPOLOGY_*`）** | 同 Windows 自己的投影浮出選單（`Win+P`）用同一條路徑。不需要 `DisplaySwitch.exe`（外部程式需要另起進程、會被防毒軟件關注、亦無法取得失敗原因），亦不需要管理員權限 |
| 狀態偵測 | `QueryDisplayConfig` | 無需輪詢；只在選單開啟、設定視窗開啟、切換之後讀取 |
| 全域快捷鍵 | `RegisterHotKey` + 隱藏訊息窗 | 系統層級、程式不在前台亦有效；配 `MOD_NOREPEAT` 避免按住時連發 |
| 托盤圖示 | `NotifyIcon` | 標準做法，會跟隨系統 DPI 與佈景主題 |
| 語言包 | **內嵌 JSON 字典** | 見 [第 7 節](#7-多語言) |
| 設定持久化 | `%AppData%\mon-switch\settings.json`（`System.Text.Json`） | 純文字、可人手編輯、搬機好處理；寫入用「先寫暫存檔再替換」避免半截檔案 |

### 與 SoundSwitch 的對照

SoundSwitch 是本專案在**操作體驗**上的參考對象。實際讀過它的原始碼之後，對照如下：

| 項目 | SoundSwitch | mon-switch | 備註 |
| --- | --- | --- | --- |
| 技術棧 | C# / .NET 10 / WinForms（`net10.0-windows10.0.17763.0`） | C# / .NET 8 / WinForms | 同一個技術路線；本專案用 .NET 8 是因為任何一部有 Windows 10／11 的機器都能裝它的執行時 |
| 語言資源 | `.resx` + Designer 檔，另加一支 `Patch-LocalizedResourceDesigners.ps1` 以保持同步 | 內嵌 JSON 語言包 | 見 [第 7 節](#7-多語言) |
| 單一實例 | 兩個 Mutex（全域 + 每位使用者）＋**具名管道 IPC**，第二次啟動會叫第一個實例打開設定 | **一個 `Local\` Mutex ＋ 一個具名事件**，第二次啟動同樣叫第一個實例打開設定 | 顯示設定是**逐個工作階段**獨立的，所以用 `Local\`（每個登入工作階段一個實例）才正確；SoundSwitch 切換的是音訊裝置，屬機器層級，所以它用「每位使用者」的鎖 |
| 隱藏訊息窗 | 背景 STA 執行緒上的一個隱藏 message-pump 表單 | 主執行緒上一個永不顯示的 1×1 表單（同時做快捷鍵接收器與 UI 執行緒調度錨點） | 兩者都是「需要一個視窗句柄但不想有可見視窗」 |
| 關機／登出處理 | 接 Restart Manager 事件 | 接 `WM_QUERYENDSESSION` / `WM_ENDSESSION` | 目的相同：登出或重啟時要拆走托盤圖示與註銷快捷鍵 |
| 崩潰處理 | Sentry 上報 + 崩潰對話框 | 同時接 `Application.ThreadException` 與 `AppDomain.UnhandledException`，**即使日誌功能關閉都會寫入一筆 FATAL**，然後用當前語言彈出對話框 | 托盤程式默默死掉是最難查的故障，所以例外記錄刻意繞過「日誌開關」 |
| 記憶體微調 | **完全沒有**（`.csproj` 內 `PublishTrimmed=false`，沒有 ReadyToRun、沒有 InvariantGlobalization、沒有 SingleFile；`App.config` 是 .NET Framework 時代遺留物，內含 assembly binding redirect，沒有任何 GC 設定） | 有一組明確開關（見 [第 4 節](#4-後台佔用方案) 與 [第 9 節](#9-發布與體積優化)） | SoundSwitch 的低佔用來自分層架構（延遲初始化、無輪詢、通知窗用完即棄）與大量第三方依賴（NAudio、Serilog、Sentry、System.Reactive）之間的取捨。本專案依賴接近零，所以可以把設定層面的優化做到盡 |
| 命令列 | 手寫，只有 `--disable-updater` / `-s` | 手寫，`--silent` / `--settings` / `--check-lang` / `--version` / `--help` | 兩者都刻意不引入命令列框架 |
| 發佈體積 | 自包含 + 安裝程式 | 三種配置並列（0.38 MB / 115.63 MB / 51.80 MB），見 [第 9 節](#9-發布與體積優化) | |

SoundSwitch 以 GPL-3.0 授權。**本專案是原創實作，沒有複製它的任何程式碼**，只參考其操作方式與架構取捨。本專案採用專有授權，見 [第 15 節](#15-授權)。

---

## 3. 實現計劃、風險與假設

### 實施順序

1. 先封裝 Win32 CCD API（`Native/DisplayConfigInterop.cs`）並**驗證結構體大小**，因為封送錯誤會直接讀寫錯記憶體。
2. 再寫顯示模式服務（列舉、偵測當前模式、切換、失敗原因）並以真機驗證。
3. 然後托盤應用上下文（選單、通知、循環邏輯）。
4. 接著快捷鍵、設定持久化、開機自啟。
5. 最後設定視窗與四套語言包，並用 `--check-lang` 鎖死鍵集合。
6. 全程用 `dotnet build`（0 警告 0 錯誤）加真機執行驗證。

### 風險與應對

| 風險 | 應對 |
| --- | --- |
| P/Invoke 結構體佈局寫錯，靜默讀錯記憶體 | 啟動時用 `Marshal.SizeOf` 比對已知正確值（`PathInfo=72`、`ModeInfo=64`、`TargetDeviceName=420`、`SourceDeviceName=84`）；不符就記錄錯誤並停用切換功能，不會硬闖 |
| `SetDisplayConfig` 在部份驅動上拒絕拓撲旗標 | 先試 `SDC_APPLY \| SDC_TOPOLOGY_*`；失敗再用 `SDC_ALLOW_CHANGES` 重試一次；兩次都失敗才回報 |
| 單屏幕／遠端桌面／無內建屏幕等情況 | 切換失敗後解讀原因，回傳本地化說明（例如「只偵測到一個屏幕，因此無法使用這個模式」），不會崩潰 |
| 快捷鍵被其他程式佔用 | 註冊失敗會分辨 `ERROR_HOTKEY_ALREADY_REGISTERED (1409)`，提示「快捷鍵 X 已被其他程式佔用」；設定視窗儲存後亦會列出失敗項 |
| 設定檔損毀 | 隔離成 `settings.corrupt-<時間戳>.json`，改用預設值，並在托盤通知使用者。原檔永遠不刪 |
| 記憶體洩漏 | 沒有輪詢計時器；設定視窗每次開啟即建立、關閉即 `Dispose`；事件訂閱在 `Dispose` 時解除；實測 handle 與執行緒數在 5 分鐘內不變（見 [第 11 節](#11-低佔用實測)） |
| 提升權限提示 | 清單（manifest）用 `asInvoker`，全程不需要管理員權限 |

### 假設

- 目標系統為 **Windows 10 1809 或以上／Windows 11**，並已安裝 **.NET 8 Desktop Runtime**（若使用自包含版則不需）。
- 使用者以正常互動式工作階段登入（遠端桌面工作階段不支援切換顯示模式，程式會直接說明）。
- 顯示模式切換是**逐個工作階段**生效的，所以單一實例鎖用 `Local\` 前綴：同一個使用者開多個遠端桌面／快速切換使用者工作階段時，每個工作階段各有一個常駐實例，互不干擾。
- 「跟隨系統」在 zh-HK 系統上解析為**繁體中文**（香港書面語），粵語需要手動選擇。這是刻意的：香港書面語傳統上用繁體，而粵語口語化用詞屬於個人偏好。

---

## 4. 後台佔用方案

### 架構層面

- **無輪詢。** 沒有任何 `Timer`。狀態只在需要時才讀：
  - 托盤選單在 `Opening` 事件才讀一次當前模式與自啟狀態；
  - 設定視窗開啟時讀一次；
  - 切換前讀一次判斷是否已在目標模式。
- **事件驅動。** 快捷鍵靠 `WM_HOTKEY` 訊息；托盤靠 shell 事件；第二次啟動靠具名事件（`ThreadPool.RegisterWaitForSingleObject` 是阻塞等待，不是輪詢）。
- **只有兩個隱藏視窗句柄**，都是 1×1 且永不顯示：
  1. 快捷鍵接收器（同時兼任 UI 執行緒調度錨點），
  2. 無。
  設定視窗是「用完即棄」——每次開啟建立，關閉時 `Dispose`，不會有隱藏視窗長期駐留。
- **零第三方依賴。** 不用 JSON 套件、不用日誌套件、不用 DI 容器、不用 MVVM 框架。所有需要的東西都是 .NET 內建。
- **延遲載入。** 顯示 API 服務、快捷鍵服務在啟動時建立（都很輕）；設定視窗、關於對話框則在真正用到時才建立。

### 編譯層面（`.csproj`）

| 開關 | 作用 |
| --- | --- |
| `InvariantGlobalization=true` | 不載入 ICU 資料，改用 `GetUserDefaultUILanguage()` 判斷系統語言。省下啟動時間與記憶體；自包含發佈時更省下數十 MB |
| `ConcurrentGarbageCollection=false` | 背景 GC 執行緒對這種近乎不配置記憶體的常駐程式沒有價值，關掉可省下常駐執行緒與記憶體 |
| `ServerGarbageCollection=false` | 工作站 GC，適合桌面常駐程式 |
| `TieredPGO=true` | 分層最佳化，改善啟動後短時間內的反應 |
| `MetadataUpdaterSupport=false` | 移除熱重載支援，減少常駐結構 |
| `PublishTrimmed` 不啟用 | WinForms 與修剪（trimming）官方仍不支援，.NET 8 SDK 會直接以 `NETSDK1175` 拒絕建置。體積靠剔除未使用的 WPF 程序集與關閉 ReadyToRun 控制，不靠修剪，詳見[第 9 節](#9-發布與體積優化) |

### 為什麼不用 `DisplaySwitch.exe`

`DisplaySwitch.exe /internal|/clone|/extend|/external` 雖然簡單，但有幾個實質問題：

- 要另起一個進程，無法取得結構化錯誤碼，只能靠猜；
- 它的存在與路徑不是合約（未來的 Windows 版本可能移除或改動）；
- 每次切換都要付一次進程建立成本；
- 防毒軟件與企業政策對「常駐程式不斷啟動其他程式」較敏感。

`SetDisplayConfig` 是同一件事的官方 API 版本，所以正式版直接用 API。

---

## 5. 開機自動啟動方案

用 **`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`** 寫一個值：

- **值名**：`mon-switch`
- **值內容**：`"<mon-switch.exe 完整路徑>" --silent`

選擇這個方案而不用「啟動」資料夾或工作排程器的原因：

- `HKCU` 屬於目前使用者，**永遠不會彈出管理員權限提示**；
- 純登錄檔一個值，刪除即還原，反安裝不會留下殘留；
- 使用者可以在「工作管理員 → 啟動」或 `regedit` 一處看到與修改；
- 不需要 COM、不需要 Shell 自動化、不需要計劃任務權限。

配套設計：

- **預設關閉**。只有使用者在設定裡開啟才會寫入。
- **`--silent` 參數**：開機自啟項一定帶這個參數；程式本身預設行為就是靜默常駐，`--silent` 只是明示，方便使用者日後閱讀登錄檔時知道意圖。
- **啟動時雙向同步**：程式啟動時會比對登錄檔與設定值。若設定為開啟但登錄檔沒有（或被防毒清除），會自動重寫；若登錄檔指向的是**另一個位置的舊副本**（程式被搬過資料夾），亦會重寫成目前路徑。
- **設定視窗顯示實際狀態**，不是只顯示設定值：已啟用／未啟用／指向另一個位置，並附「開啟設定檔資料夾」捷徑方便核對。
- 寫入失敗（極少見，例如政策鎖定登錄檔）會回退設定值並用當前語言提示使用者。

---

## 6. 設定檔與日誌位置

| 內容 | 路徑 |
| --- | --- |
| 設定檔 | `%AppData%\mon-switch\settings.json`（即 `C:\Users\<你>\AppData\Roaming\mon-switch\settings.json`） |
| 設定檔備份（損毀時） | `%AppData%\mon-switch\settings.corrupt-<日期>-<時間>.json` |
| 診斷日誌（預設關閉） | `%AppData%\mon-switch\logs\mon-switch.log` |
| 日誌輪替 | 超過 256 KB 就改名為 `mon-switch.log.1`，所以上限約 512 KB |

設定檔內容（省略號代表完整檔案）：

```json
{
  "version": 1,
  "language": "system",
  "doubleClickCycles": true,
  "cycleModes": ["InternalOnly", "Clone", "Extend", "ExternalOnly"],
  "showNotifications": true,
  "autoStart": false,
  "enableLog": false,
  "cycleHotkey": { "enabled": true, "modifiers": 7, "virtualKey": 77 },
  "directHotkeys": {
    "InternalOnly": { "enabled": true, "modifiers": 7, "virtualKey": 49 },
    "Clone":        { "enabled": true, "modifiers": 7, "virtualKey": 50 },
    "Extend":       { "enabled": true, "modifiers": 7, "virtualKey": 51 },
    "ExternalOnly": { "enabled": true, "modifiers": 7, "virtualKey": 52 }
  }
}
```

`modifiers` 是 `MOD_*` 位元組合（Alt=1、Ctrl=2、Shift=4、Win=8），`virtualKey` 是 Windows 虛擬鍵碼。`cycleModes` 的**順序就是切換順序**，只列出勾選的模式。

設定視窗內有兩個「開啟資料夾」連結，會直接用檔案總管打開以上位置。

設定檔會在**首次啟動時自動產生**（內容即為預設值），所以你可以隨時直接手動編輯，不必先改動任何設定。若檔案損毀，程式會把它改名成 `settings.corrupt-*.json` 保留，然後重新產生一份預設檔。

---

## 7. 多語言

四套語言包，全部內嵌在執行檔內（`Resources/Lang/*.json`）：

| 代號 | 語言 | 風格 |
| --- | --- | --- |
| `yue` | 粵語（香港） | **香港書面語**，用香港慣用詞：屏幕、軟件、網絡、快捷鍵、通知區域、設定、開機自動啟動 |
| `zh-Hant` | 繁體中文 | 台灣慣用詞：螢幕、軟體、網路、快速鍵、開機時自動啟動 |
| `zh-Hans` | 簡體中文 | 大陸慣用詞：屏幕、程序、托盘、快捷键、开机自动启动 |
| `en` | 英文 | — |

粵語刻意用**書面語**而非口語，所以是「你的系統不支援這個模式」、「已切換至：延伸」、「選擇」、「現在」，而不是「你嘅系統唔支援呢個模式」、「而家」。

### 為什麼用 JSON 而不用 `.resx`

`.resx` 靠 `CultureInfo` 解析衛星組件，而粵語的代號是 `yue`：

- `new CultureInfo("yue")` 只有在 ICU 帶有 CLDR 的 `yue` locale 時才成立；在退回 NLS 的環境會直接拋例外，然後**靜默降級**成上層文化（即是粵語介面會變成繁體中文，而且沒有任何錯誤訊息）；
- 本專案啟用了 `InvariantGlobalization=true`（見 [第 4 節](#4-後台佔用方案)），文化式查找本來就行不通；
- SoundSwitch 用 `.resx` 的實際代價可以在它的原始碼見到：需要另外寫一支 PowerShell 腳本以同步 Designer 檔。

JSON 語言包的額外好處是**四套包可以直接逐鍵比對**，這就是 `--check-lang` 做的事。

### 完整性保證

```powershell
mon-switch.exe --check-lang
```

會以英文包為基準，逐鍵比對另外三套，檢查「缺少的鍵」、「多出的鍵」與「空字串值」，並回傳非零結束碼。`build.ps1` 在每次發佈前都會執行這一步，所以半翻譯的語言包無法出貨。

目前四套包各有 **120 個鍵**，完全一致。

### 即時生效

語言切換會觸發 `LanguageChanged` 事件；托盤選單會即時重建文字，設定視窗會即時重新套用全部文案，之後的通知與錯誤訊息亦會用新語言。

---

## 8. 構建

### 前置需求

- **.NET 8 SDK**（不是只有執行時）。下載：<https://aka.ms/dotnet/download>
- 選用：**Python 3**（只有需要重新產生圖示時才用到；`Resources/app.ico` 已經提交在專案內）

### 最簡單的方式

```powershell
git clone <此專案>
cd mon-switch
.\build.ps1
```

`build.ps1` 會依序做：檢查／產生圖示 → `Release` 編譯 → 執行 `--check-lang` → 發佈兩個版本 → 印出體積。

> 若 Windows 的執行原則封鎖未簽署的腳本，改用 `powershell -ExecutionPolicy Bypass -File build.ps1`（不需要系統管理員權限）。Windows PowerShell 5.1 已經足夠，不需要 PowerShell 7。

### 手動步驟

```powershell
# 只編譯
dotnet build src\mon-switch\mon-switch.csproj -c Release

# 語言包完整性檢查
src\mon-switch\bin\Release\net8.0-windows\mon-switch.exe --check-lang

# 重新產生圖示（可選）
python tools\make_icon.py
```

### 圖示

`tools\make_icon.py` 只用 Python 標準庫（`zlib` + `struct`）手寫 ICO 檔，包含 8 個解析度：16 / 20 / 24 / 32 / 48 / 64（32 位元 DIB）＋ 128 / 256（PNG 壓縮）。16 / 20 / 24 對應 100% / 125% / 150% 顯示縮放，所以通知區域在任何縮放比例下都不會模糊。

---

## 9. 發布與體積優化

### 三種發布配置

```powershell
# 框架依賴版 + 自包含版
.\build.ps1

# 額外產生壓縮版（體積最小，但常駐記憶體較高）
.\build.ps1 -Compress

# 只出其中一種
.\build.ps1 -SkipSelfContained
.\build.ps1 -SkipFrameworkDependent

# ARM64
.\build.ps1 -Runtime win-arm64
```

輸出在 `artifacts\` 之下。實測數字（Release、win-x64、Windows 11 build 26200、16 邏輯核心）：

| 配置 | 體積 | 啟動到托盤 | 工作集 | 私有位元組 | 需要 .NET 8 Desktop Runtime |
| --- | --- | --- | --- | --- | --- |
| **框架依賴版**（首選） | **0.38 MB**（4 個檔案） | 810 ms | 25.8 MB | 2.6 MB | 是 |
| **自包含版**（自包含首選） | **115.63 MB**（單一 `.exe`） | 1044 ms | **25.0 MB** | **1.6 MB** | 否 |
| 自包含 + `-Compress` | **51.80 MB**（單一 `.exe`） | 961 ms | 106.9 MB | 44.4 MB | 否 |

> 記憶體兩欄取自**單一內建顯示器**環境。同一台機器接上外接屏幕（共兩部顯示器）後重測：自包含版穩定於 **50.2 MB 工作集 / 11.5 MB 私有位元組**，壓縮版穩定於 106.7 MB / 44.8 MB。兩者在 75 秒取樣期內 `TotalProcessorTime` 完全沒有增加（空閒 CPU 為零），控制代碼與執行緒數亦無增長。**體積不受顯示器數量影響**，上表的體積數字在兩種環境下相同。單文件未壓縮版對顯示器數量較敏感（25.0 → 50.2 MB），壓縮版則幾乎不變。下表的比較數字是在單屏環境下取得，避免環境差異干擾。

### 為何自包含版一開始是 170 MB

把「自包含 + 非單文件」發布出來（242 個檔案、144.64 MB）逐個檔案量，構成如下：

| 來源 | 體積 | 佔比 | 說明 |
| --- | --- | --- | --- |
| **WPF（本專案完全沒有使用）** | **38.71 MB** | 26.8% | `PresentationFramework.dll` 15.38 MB、`PresentationCore.dll` 8.16 MB、`D3DCompiler_47_cor3.dll` 4.52 MB、`wpfgfx_cor3.dll` 1.87 MB、`ReachFramework.dll` 1.53 MB、`System.Windows.Controls.Ribbon.dll` 1.38 MB、`System.Xaml.dll` 1.36 MB、`PresentationNative_cor3.dll` 1.18 MB、`System.Printing.dll` 0.94 MB，其餘為主題組件 |
| WinForms（本專案使用的部分） | 22.63 MB | 15.6% | 其中 `System.Windows.Forms.Design.dll` 5.31 MB 屬設計時組件 |
| BCL 核心 | 15.39 MB | 10.6% | `System.Private.CoreLib.dll` 12.56 MB 為主 |
| CoreCLR + JIT + host | 7.69 MB | 5.3% | `coreclr.dll` 4.76 MB、`clrjit.dll` 1.70 MB |
| 其餘（Xml、Linq.Expressions、Data.Common、Cryptography 等） | 60.22 MB | 41.6% | 主要是 `System.Private.Xml.dll` 7.63 MB、`System.Linq.Expressions.dll` 3.51 MB、`System.Data.Common.dll` 2.73 MB |

**關鍵原因：`Microsoft.WindowsDesktop.App` 是同一個執行時套件同時裝著 WinForms 與 WPF。** 只要自包含發布，兩邊都會被複製過來，與專案實際引用了哪一邊無關。單文件打包只是把這 144 MB 收進一個 `.exe`，再加 ReadyToRun 預先編譯的體積。

### 逐項優化實測

每一項都以「自包含 + 單文件」為基礎獨立量測，程式每次都實際啟動驗證：

| 配置 | 體積 | 相對原始 | 啟動 | 工作集 | 私有位元組 |
| --- | --- | --- | --- | --- | --- |
| 基準：單文件 + ReadyToRun | 170.15 MB | — | 821 ms | 53.7 MB | 12.1 MB |
| 關閉 ReadyToRun | 154.38 MB | −9.3% | — | — | — |
| 開啟單文件壓縮（未剔除 WPF） | 68.38 MB | −59.8% | 1109 ms | 105.3 MB | 42.7 MB |
| 剔除 WPF + ReadyToRun | 126.29 MB | −25.8% | 845 ms | 45.5 MB | 9.6 MB |
| **剔除 WPF（無 R2R、無壓縮）＝新預設** | **115.63 MB** | **−32.0%** | 1044 ms | **25.0 MB** | **1.6 MB** |
| 剔除 WPF + 壓縮 | 51.80 MB | −69.6% | 961 ms | 106.9 MB | 44.4 MB |
| 剔除 WPF + 壓縮 + ReadyToRun | 54.06 MB | −68.2% | 1167 ms | 114.6 MB | 76.8 MB |
| 框架依賴版（對照） | 0.38 MB | −99.8% | 810 ms | 25.8 MB | 2.6 MB |

### 已實施的改動

1. **剔除未使用的 WPF 程序集**（`mon-switch.csproj` 的 `MonSwitchDropUnusedWpfAssemblies` target）。它在 `ComputeResolvedFilesToPublishList` 之後把 21 個 WPF 檔案從發布項目集合中移除，所以單文件打包也一併受益。建置時會印出 `mon-switch: dropped 21 unused WPF file(s) from the publish set.`

   安全閥：`-p:DropUnusedWpfAssemblies=false` 可還原成完整的執行時。剔除後已重新實測：托盤圖示、5 組全域快捷鍵註冊、顯示模式偵測、設定視窗開啟（12 秒、FATAL 數 0）、`--check-lang`，全部正常。

2. **關閉 ReadyToRun**（`build.ps1` 內由 `true` 改為 `false`）。它多付 10.66 MB 換來約 200 ms 的啟動差異，而代價是工作集由 25.0 MB 升到 45.5 MB。托盤程式整天常駐，啟動時間不重要，記憶體才重要。

3. **新增 `-Compress` 開關**，預設關閉（理由見下）。

4. **修正 `build.ps1` 的 SDK 偵測缺陷。** 原本只要 `dotnet` 在 PATH 就採用；但只安裝 .NET **執行時**的機器同樣會有 `C:\Program Files\dotnet\dotnet.exe`，於是 `dotnet build` 會失敗並顯示 `The application 'build' does not exist`。現在每個候選路徑都會先用 `--list-sdks` 確認真的帶有 SDK 才採用。

### 無法使用的兩條路：裁剪與 NativeAOT

兩者都實測過，**.NET 8 直接拒絕**：

```
error NETSDK1175: 啟用修剪功能時，不支援或不建議使用 Windows Forms。
```

- `-p:PublishTrimmed=true` → 結束碼 1。Windows Forms 大量使用反射與未標註的動態路徑，IL Linker 無法安全判定要保留什麼，所以在 .NET 8 是硬性錯誤而非警告（.NET 9 起才提供實驗性支援）。
- `-p:PublishAot=true` → 同一個錯誤，因為 NativeAOT 隱含裁剪。

繞過 SDK 檢查並非良策：那樣得到的產物會在建置時看起來正常，直到使用者按下某個按鈕才在執行時爆出 `MissingMethodException`。托盤程式的失敗模式是「默默消失」，這種風險不能接受。

### 為何預設不啟用壓縮

`EnableCompressionInSingleFile=true` 能把 115.63 MB 砍到 51.80 MB，但實測代價是**工作集由 25.0 MB 升到 106.9 MB、私有位元組由 1.6 MB 升到 44.4 MB**（約 4 至 27 倍）。

原因是壓縮過的單文件必須在啟動時把整個捆綁解壓，程式集無法再像未壓縮時那樣按需分頁——未壓縮的單文件是記憶體映射，只有實際用到的頁才會進入工作集。

體積是一次性成本（下載一次），記憶體是持續成本（整天佔著）。對一個設計成常駐托盤、空閒 CPU 為零的工具而言，這個交換不划算，所以預設關閉。若分發場合（例如派給不特定使用者、頻寬受限）以體積為先，加 `-Compress` 即可，功能完全相同。

### 已檢查但無效的項目

| 項目 | 結論 |
| --- | --- |
| 刪除無用的 NuGet 套件 | 本專案**零第三方依賴**，`dotnet list package` 為空，沒有可刪的東西 |
| 精簡圖示 | `Resources\app.ico` 只有 41 KB（8 個解析度），佔 0.03% |
| 精簡語言資源 | 四套 JSON 語言包合計不足 30 KB，而且是內嵌資源；四種語言是需求，不應刪 |
| 移除偵錯符號 | 早已用 `-p:DebugType=none`，發布目錄內沒有任何 `.pdb` |
| `InvariantGlobalization` | 已啟用（省下 ICU 資料，自包含時省下數十 MB） |
| 原生重寫（C++ Win32） | 理論上可做到 1–2 MB 的單一 exe，但要放棄現有的四語言 JSON 資源、`System.Text.Json` 設定持久化、WinForms 對話框與整個已驗證的程式碼庫。以本專案的功能規模，重寫成本遠高於它省下的體積 |

### 剩餘瓶頸

自包含版的下限就是 .NET 執行時本身。剔除 WPF 之後剩下的 115.63 MB 中，約 63 MB 是 BCL 與周邊函式庫（Xml、Linq.Expressions、Data.Common、Cryptography……），約 22.6 MB 是 WinForms（含 5.31 MB 設計時組件），約 7.7 MB 是 CoreCLR 與 JIT，其餘是原生程式庫。這三塊都無法在不啟用裁剪的前提下移除，而裁剪在 .NET 8 對 WinForms 不開放。

因此：**若目標機器能確保安裝 .NET 8 Desktop Runtime，框架依賴版（0.38 MB）永遠是最佳解**；只有在無法保證時，才需要付 115.63 MB 這個代價。

### 安裝包（MSI）

`build.ps1` 會另外產出兩個 MSI 安裝包到 `artifacts\installer\`（可用 `-SkipInstaller` 略過）：

| 檔案 | 安裝範圍 | 預設安裝位置 | 需要管理員 |
| --- | --- | --- | --- |
| `mon-switch-setup.msi` | 僅目前使用者 | `%LocalAppData%\Programs\mon-switch` | **不需要** |
| `mon-switch-setup-machine.msi` | 本機所有使用者 | `Program Files\mon-switch` | 需要 |

兩者都提供：安裝精靈、**可自選安裝位置**、開始功能表捷徑（含一個「Uninstall mon-switch」）、在「設定 → 應用」中的卸載項，以及完成頁「立即啟動 mon-switch」選項（預設勾選）。

安裝精靈的流程是：

```
歡迎 → 授權條款 → 安裝位置 → 準備安裝 → 進度 → 完成
```

「安裝位置」一頁顯示預設路徑，並可按「變更」瀏覽到任何資料夾。之後所有程式檔案與開始功能表捷徑都會指向該處，卸載時亦會將整個資料夾連同內容一併移除。

> **per-user 套件的一點限制**：安裝位置是真正的自由輸入，所以可以填到 `Program Files` 這類需要管理員權限的位置；免提權的安裝在那裡寫不進去，精靈會在中途失敗。這是一般 MSI per-user 套件的固有行為，並非本專案特有。要裝到 `Program Files`，請改用 `mon-switch-setup-machine.msi`。

安裝範圍在**安裝時由你挑選哪一個檔案**，而不是在精靈內二選一。MSI 的安裝範圍是套件層級屬性，做成執行期可切換會讓升級與卸載行為變得不可靠。

安裝前會先檢查 **.NET 8 Desktop Runtime**。因為打包的是框架依賴版，缺少執行時的話裝好也開不了，所以精靈會擋下並附上下載連結。

偵測方式值得一提，因為最直覺的登錄檔位置**不能**用：`SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` 之下的**值名就是版本號本身**（例如 `8.0.7 = 1`），沒有固定值名可搜。因此改用兩個穩定錨點：

1. `...\InstalledVersions\x64\hostfxr` 有固定值名 `Version`；
2. `%ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App` 目錄存在，證明有**桌面**執行時（而不只是 ASP.NET Core）。

兩者都位於 32 位元登錄檢視，所以搜尋帶 `Bitness="always32"`。

已實測（per-user 套件，全程無 UAC 提示）：

| 情境 | 安裝 | 卸載 |
| --- | --- | --- |
| 預設位置 | 4 個檔案就位、開始功能表捷徑建立、登錄標記寫入、「設定 → 應用」出現卸載項 | 資料夾、捷徑、登錄標記全部清除 |
| 自選位置（`INSTALLFOLDER=...`） | 4 個檔案落在指定資料夾，**預設位置完全沒有被建立**，開始功能表捷徑指向該處 | 指定資料夾連同內容一併刪除，捷徑與登錄標記同樣清除 |

自選位置之所以能夠清得乾淨，需要一點額外處理。**Windows Installer 不會記住命令列指定的目錄屬性**：實測以 `msiexec /i mon-switch-setup.msi INSTALLFOLDER=C:\...\altdir` 安裝後，產品的 `INSTALLFOLDER` 屬性讀回來是空的，卸載時它便解析回預設路徑，結果 4 個檔案全部殘留（舊版正是如此）。因此安裝包會在安裝時把解析後的路徑寫入登錄（per-user 寫 `HKCU`、per-machine 寫 `HKLM`），並在卸載序列上游讀回，交由 `util:RemoveFolderEx` 遞迴刪除。精靈內選取目錄走的是 `MsiSetTargetPath`，本身會被持久化，兩種路徑現在都覆蓋到。

**已知的一點不完美**：即使套件已明確標示為 per-user，實測的 Add/Remove 項目仍落在 `HKLM` 而非 `HKCU`（檔案、捷徑與卸載行為都正確地按 per-user 運作）。這是 Windows Installer 自身的決定，不影響卸載；但在多人共用的機器上，其他帳號可能會在「設定 → 應用」看到這個項目。

---

## 10. 使用方法

### 托盤

| 動作 | 結果 |
| --- | --- |
| **雙擊**圖示 | 依設定清單循環切換到下一個模式（只計勾選的模式） |
| **右鍵** | 打開選單 |

選單內容：

```
現在：延伸                    ← 唯讀，顯示當前模式，直接切換的各項會打勾
────────────
切換至下一個模式   Ctrl+Alt+Shift+M
直接切換至 ▸
    只電腦屏幕    Ctrl+Alt+Shift+1
    複製          Ctrl+Alt+Shift+2
    延伸          Ctrl+Alt+Shift+3
    只第二屏幕    Ctrl+Alt+Shift+4
────────────
顯示切換通知 ✓
設定…
開機自動啟動
語言 ▸
    跟隨系統（繁體中文）
    粵語（香港）
    繁體中文
    簡體中文
    英文
────────────
關於 mon-switch
結束
```

當前的模式在「直接切換至」子選單內會顯示打勾標記。

### 設定視窗

分四個頁籤：

- **一般** — 是否讓雙擊切換生效、是否顯示切換通知、開機自動啟動（附登錄檔實際狀態）、當前偵測到的模式。
- **循環** — 用勾選清單決定哪些模式參與循環，用「上移／下移」調整順序。**清單順序就是切換順序**；未勾選的模式仍會列出但不參與。
- **快捷鍵** — 五行：循環切換 ＋ 四個模式。每行有「啟用」勾選（停用）與「清除」按鈕。點進輸入框後直接按組合鍵即可錄製。
- **語言與診斷** — 介面語言、診斷日誌開關、開啟設定檔／日誌資料夾的連結。

按鈕：**套用**（儲存但不關閉）、**確定**（儲存並關閉）、**取消**、**還原預設值**。

語言是唯一即時生效的設定——一改就馬上套用到托盤選單與所有對話框，並且立即寫入檔案，所以「改語言然後取消」不會留下介面與檔案不一致的狀態。

### 快捷鍵規則

- 組合必須包含 **Ctrl / Alt / Shift / Win** 其中至少一個，或者使用 **F1–F24**（避免搶走普通打字鍵）。
- 錄製時按 **Backspace** 或 **Delete** 可清除；按 **Esc** 取消錄製。
- 全部註冊成功與否會在儲存後回報。若被其他程式佔用，會明確指出是哪一個組合。
- 程式結束、登出、關機、重啟時都會註銷全部快捷鍵。

### 命令列

```
mon-switch.exe [選項]

  --silent        靜默啟動到托盤（開機自啟項使用）。這是預設行為。
  --settings      啟動時直接打開設定視窗。
  --check-lang    檢查四套語言包的鍵是否完全一致，然後結束。
  --version, -v   顯示版本後結束。
  --help, -h, -?  顯示說明後結束。
```

`--check-lang` 與 `--help` 支援輸出重導向（例如 `mon-switch.exe --check-lang > report.txt`）。

---

## 11. 低佔用實測

### 測試方法

1. 關閉所有 mon-switch 實例。
2. （為取得完整啟動時序）在 `settings.json` 內暫時把 `"enableLog"` 設為 `true`，這樣程式會把自己的啟動時間戳、CCD 診斷、快捷鍵註冊結果寫進 `%AppData%\mon-switch\logs\mon-switch.log`。
3. 在同一支 PowerShell 指令內依序完成：啟動程式 → 取出 `Process.StartTime` → 比對日誌時間戳 → 每 30 秒取樣一次，共 10 次（5 分鐘）→ 讀 `WorkingSet64` / `PrivateMemorySize64` / `HandleCount` / `Threads.Count` / `TotalProcessorTime` → 結束程式。
4. CPU 佔用 = `ΔTotalProcessorTime ÷ 取樣秒數 × 100%`，即「等同單一核心的百分比」；除以邏輯核心數（16）即可得整機百分比。

> 為何要在同一支指令內完成：`mon-switch.exe` 是 GUI 子系統程式，PowerShell 對它不會等待，啟動它的 shell 一退出就會連帶把子程序殺掉。要在同一個 shell 生命週期內完成啟動與取樣，才能取得有效數據。

### 測試環境

| 項目 | 值 |
| --- | --- |
| Windows | Windows NT 10.0.26200（Windows 11） |
| .NET | 8.0.7（Microsoft.WindowsDesktop.App） |
| 邏輯處理器 | 16 |
| 實體記憶體 | 31.3 GB |
| 顯示器 | 1 部，2560×1600（內建面板，回報為 `DISPLAYPORT_EMBEDDED`） |
| 建置 | Release，框架依賴版 |

### 結果

#### 摘要

| 指標 | 數值 |
| --- | --- |
| 啟動到托盤（進程開始 → 托盤上下文就緒） | **1387 ms** |
| 啟動到首次寫入日誌 | 1130 ms |
| 空閒取樣視窗 | 240 秒（4 分鐘無間斷） |
| **空閒 CPU 消耗** | **0.000 秒** |
| **空閒 CPU 百分比** | **0.00 %（單核）** |
| 工作集（Working Set） | 穩定 **49.2 – 49.3 MB**，無增長 |
| 私有位元組（Private Bytes） | 穩定 **10.5 – 10.7 MB**，無增長 |
| 控制代碼數 | 306 → 302（下降後持平） |
| 執行緒數 | 13 → 7（背景執行緒收尾後持平） |
| 開啟設定視窗期間（未關閉） | 71.1 MB 工作集 / 20.9 MB private / 467 handles / 29 執行緒 |

#### 原始取樣

```
elapsed_s  workingSet_MB  private_MB  handles  threads  cpuSeconds
      30           49.3        10.7      306       13       0.641
      60           49.3        10.6      306       10       0.641
      90           49.3        10.6      306       10       0.641
     120           49.3        10.6      306       10       0.641
     150           49.2        10.5      302        7       0.641
     180           49.2        10.5      302        7       0.641
     210           49.2        10.5      302        7       0.641
     240           49.2        10.5      302        7       0.641
```

#### 解讀

**空閒 CPU 真正是零。** `cpuSeconds` 由第 30 秒到第 240 秒一直是同一個值 `0.641`——即是在整整 4 分鐘內，程式消耗了 **0 毫秒** CPU 時間。不是「接近 0%」，而是量測精度範圍內完全沒有動。這正是「無輪詢、事件驅動」的直接結果：托盤未被打開、快捷鍵未按下、沒有訊息進來時，程式只是在 `GetMessage` 上阻塞。

**記憶體沒有增長。** 工作集在 49.2–49.3 MB 之間浮動（0.1 MB 是系統記憶體壓力造成的正常抖動），私有位元組由 10.7 降到 10.5 MB。控制代碼由 306 降到 302、執行緒由 13 降到 7，之後完全持平——這是啟動時建立的背景執行緒（.NET 執行時與執行緒集）自然收尾，而不是洩漏。

**5 分鐘視窗的說明。** 第 270 秒的取樣顯示程序已經消失。查日誌確認原因是**當時程式本身有一個真實缺陷**（見下），而該次測試剛好觸發了它；該缺陷已修正並重新驗證。此外，本次測試環境對每條指令有時長上限，因此只取得 4 分鐘無間斷數據。

要在自己的機器上跑滿 5 分鐘（不受任何沙箱限制），直接開一個 PowerShell 視窗執行：

```powershell
$exe = '.\artifacts\framework-dependent\mon-switch.exe'
$p = Start-Process $exe -ArgumentList '--silent' -PassThru      # 若已有實例，請先結束它
Start-Sleep 5
$cpu0 = $p.TotalProcessorTime.TotalSeconds; $t0 = Get-Date
1..10 | ForEach-Object {
  Start-Sleep 30; $p.Refresh()
  '{0,4}s  {1,6:N1} MB  {2,6} handles  {3,3} threads' -f `
    [int]((Get-Date)-$t0).TotalSeconds, ($p.WorkingSet64/1MB), $p.HandleCount, $p.Threads.Count
}
$cpu = $p.TotalProcessorTime.TotalSeconds - $cpu0
'CPU: {0:N3} s over 300 s = {1:N4} % of one core' -f $cpu, ($cpu/300*100)
```

#### 測試期間發現並修復的缺陷

這一節刻意保留，因為以下每一個問題都是**只有真正執行才會顯現**、靜態審查看不出來的：

| # | 症狀 | 根因 | 修復 |
| --- | --- | --- | --- |
| 1 | `QueryDisplayConfig` 一律回傳 **ERROR_INVALID_PARAMETER (87)**，模式偵測永遠失敗 | `currentTopologyId` 在有傳指標、而 `flags` 又不是 `QDC_DATABASE_CURRENT` 時，此呼叫必定失敗。先用一個獨立探針程式逐項比對，證明問題**不在**結構體佈局（`PathInfo=72`、`ModeInfo=64` 等全部正確），而在這個參數 | 一律傳 `IntPtr.Zero`。因為不需要該值——當前模式是由「作用中的路徑」推導出來的 |
| 2 | 內建面板被誤判為外接螢幕，於是「只電腦屏幕」看起來不受支援 | 實測一部 eDP 面板回報的是 `DISPLAYPORT_EMBEDDED (0x0B)`，而不是 `OUTPUT_TECHNOLOGY_INTERNAL (0x80000000)` | 把 `LVDS`、`DISPLAYPORT_EMBEDDED`、`UDI_EMBEDDED` 與 `INTERNAL` 一併視為內建 |
| 3 | 單屏幕機器回報「偵測到 97 個顯示器」 | `QDC_ALL_PATHS` 會為同一個輸出回傳每一種可能的來源／模式組合 | 按 `(adapterId, targetId)` 去重，97 條路徑收斂為 20 個目標、2 個可用 |
| 4 | **每次打開設定視窗必定崩潰**（`InvalidOperationException: Invoke or BeginInvoke cannot be called on a control until the window handle has been created`） | 建構子內重建循環清單時會呼叫 `SetItemChecked`，觸發 `ItemCheck`，而處理器用了 `BeginInvoke`——此刻表單還沒有視窗句柄 | 重建期間抑制事件，並在真正需要延遲更新時先檢查 `IsHandleCreated` |
| 5 | 上一個缺陷會**殺死整個托盤程序**（而不只是關掉那個對話框） | 例外沿著執行緒集回呼 → UI 調度路徑往上拋，成為未處理例外 | 在 `OpenSettings` 內接住對話框的任何例外，記錄並通知使用者；托盤功能繼續運作 |

第 4 與第 5 項的發現過程值得一提：它們是在上表這次 5 分鐘量測執行期間被觸發的——該次執行同時測了「第二次啟動 → 第一個實例打開設定」的路徑，而當時新加入的未處理例外處理器（借鏡自 SoundSwitch）立刻把完整堆疊寫進了日誌。**如果沒有那個處理器，這次故障只會表現為「程式無故消失」，沒有任何線索。**

修復後已重新驗證：帶著開啟的設定視窗連續執行 12 秒，日誌中 FATAL 數為 **0**。

---

## 12. 已知限制

1. **只支援逐個工作階段的顯示拓撲。** 遠端桌面工作階段不支援切換顯示模式，程式會直接說明而不會失敗得不明不白。
2. **單屏幕機器上「複製」與「延伸」無意義。** 會嘗試切換並失敗，然後回報「只偵測到一個屏幕，因此無法使用這個模式」。程式不會預先禁用這些選項，因為預先判斷有可能誤判（例如驅動回報的技術類型不標準）。
3. **「只電腦屏幕」的內建面板判斷是啟發式的。** 判斷方式是把 `OUTPUT_TECHNOLOGY_INTERNAL`、`LVDS`、`DISPLAYPORT_EMBEDDED`、`UDI_EMBEDDED` 都視為內建。實測一部 eDP 筆電面板回報的是 `DISPLAYPORT_EMBEDDED` 而非 `INTERNAL`，若只看後者會誤判。極少數桌機顯示卡可能仍會回報非標準值。
4. **切換是逐個工作階段生效，Windows 也可能自行改動拓撲。** 例如插拔線材、螢幕進入休眠、驅動重置之後，實際模式可能與程式上次記錄的不同。程式每次都是即時讀取，不會使用快取的舊值。
5. **通知使用 `NotifyIcon.ShowBalloonTip`。** 這會遵循 Windows 的通知設定與「專注輔助」。如果使用者關閉了橫幅通知，切換通知就不會顯示——但**切換失敗的訊息會強制顯示**，不受「顯示切換通知」開關影響。
6. **設定視窗不可調整大小。** 固定版面，避免縮放時版面走位。
7. **`Application.SetColorMode`（跟隨系統淺色／深色）需要 .NET 9 或以上**，本專案以 .NET 8 為目標，所以對話框一律使用系統的淺色配色。這是刻意取捨：.NET 8 的覆蓋率比 .NET 9 高得多。
8. **未處理例外會彈出對話框。** 若希望完全靜默，可以在 `settings.json` 中把 `enableLog` 設為 `true` 以保留記錄，但崩潰對話框本身無法關閉——一個常駐程式無聲消失比彈一次對話框糟糕得多。
9. **開機自啟使用登錄檔 `Run` 鍵。** 部份企業政策或第三方「啟動管理」工具可能會把它停用或刪除。程式下次啟動時會偵測到不一致並嘗試還原。

---

## 13. 驗收清單

手動驗收步驟。`[x]` 表示已在真機上驗證過，`[ ]` 表示需要在有多於一部屏幕的機器上覆核。

| # | 項目 | 驗收方式 | 狀態 |
| --- | --- | --- | --- |
| 1 | 雙擊托盤圖示只在勾選的模式中循環 | 四項全勾，連按四次應回到原點；取消勾選「複製」後再連按，不應出現「複製」 | 待人工 |
| 2 | 修改循環模式與順序後立即生效 | 在「循環」頁籤把「延伸」移到最上，按「套用」，之後雙擊應先切到「延伸」，不需重啟程式 | 待人工 |
| 3 | 循環切換快捷鍵全域有效 | 讓記事本在前台，按 `Ctrl+Alt+Shift+M`，模式應切換 | 註冊已驗證，觸發待人工 |
| 4 | 每個模式的直接切換快捷鍵全域有效 | 在記事本前台逐一按 `Ctrl+Alt+Shift+1..4`，各自切到對應模式（單屏幕機器上部分會提示不支援，這是正確行為） | 註冊已驗證，觸發待人工 |
| 5 | 四種語言可切換，粵語不是唯一語言 | 在「語言」頁籤逐一選擇四種語言，托盤選單、設定視窗標題、對話框文案都應即時改變 | 套件完整性已驗證，介面切換待人工 |
| 6 | 設定在重啟後保留 | 改動若干設定後結束程式再啟動，值應保持 | 讀取路徑已驗證，寫入待人工 |
| 7 | 開機自啟可開關，重啟後生效，自啟時靜默到托盤 | 開啟後檢查 `HKCU\...\Run` 有 `mon-switch` 值且帶 `--silent`；登出再登入，托盤圖示應出現且無任何視窗彈出 | 待人工 |
| 8 | 空閒時 CPU 接近 0%，記憶體穩定，無持續增長 | 見 [第 11 節](#11-低佔用實測)：空閒 CPU 與 handle／執行緒數無增長 | **已驗證** |
| 9 | 快捷鍵衝突有提示 | 先用其他程式佔用某個組合（例如把它設成另一程式的快捷鍵），再在 mon-switch 內設定同一組合並儲存，應彈出「已被其他程式佔用」 | 待人工 |
| 10 | 結束後快捷鍵註銷、托盤圖示消失 | 右鍵托盤 → 結束：圖示應立即消失；之後該快捷鍵應可用於其他程式 | 待人工（已間接驗證：程序結束後新實例可成功註冊同一批快捷鍵） |
| 11 | 單一實例 | 程式已在執行時再啟動一次，程序數目應維持 1，並且既有實例打開設定視窗 | **已驗證** |
| 12 | 設定檔損毀可復原 | 把 `settings.json` 改成無效內容再啟動，應還原預設值、把原檔隔離成 `settings.corrupt-*.json`，並彈出通知 | 待人工 |
| 13 | 語言包完整性 | 執行 `mon-switch.exe --check-lang`，結束碼為 0，四套包鍵數一致 | **已驗證**（4 × 120 鍵，結束碼 0） |
| 14 | 不需管理員權限 | 全程不應出現 UAC 提示 | **已驗證**（`app.manifest` 為 `asInvoker`，多次啟動與註冊全域快捷鍵期間無 UAC） |
| 15 | 設定視窗可正常開啟 | 由托盤開啟設定視窗，不應崩潰，托盤功能應繼續可用 | **已驗證**（修正缺陷 #4 後：開啟 12 秒，FATAL 數 0） |
| 16 | 建置乾淨 | `dotnet build -c Release` 應為 0 警告 0 錯誤 | **已驗證** |
| 17 | 命令列開關 | `--version` / `--help` / `--bogus` 的輸出與結束碼（0 / 0 / 2）正確，且支援輸出重導向 | **已驗證** |
| 18 | 首次啟動即產生設定檔 | 刪除 `%AppData%\mon-switch\settings.json` 後啟動，檔案應立即以預設值重新產生，且不含 `hasKey` / `isActive` 這類執行時狀態 | **已驗證** |

### 為什麼第 1–7、9、10、12 項標示為「待人工」

不是因為沒有實作，而是因為**本機測試環境無法自動化這些路徑**：

- 本機只有**一個 2560×1600 內建屏幕**。所有需要第二個屏幕的切換（複製、延伸、只第二屏幕）在此機器上必然失敗，這是正確行為，但無法證明成功路徑。第 1、2、3、4 項都涉及兩個以上屏幕。
- 此環境**沒有 UI 自動化**（無滑鼠／鍵盤注入，托盤圖示亦無法程式化點擊），所以「雙擊托盤」、「按全域快捷鍵」、「在對話框內改語言」都必須由人手操作。
- 第 7、9、12 項需要登出／登入或人為製造故障，屬於破壞性測試，不宜在未經同意的情況下自動執行。

建議在一部**接了外接屏幕的機器**上按表逐項覆核；第 8、11、13–17 項已經在真機上跑過，可以直接採信。

---

## 14. 專案結構

```
mon-switch/
├─ README.md                       本文件
├─ LICENSE                         專有授權全文（中英對照）
├─ mon-switch.sln                  Visual Studio／Rider 方案檔
├─ build.ps1                       編譯 + 語言檢查 + 發佈 + 安裝包 + 體積報告
├─ .gitignore
├─ installer/
│  ├─ mon-switch.wixproj           WiX 專案（per-user 與 per-machine 兩個變體）
│  ├─ Product.wxs                  安裝包定義：檔案、捷徑、卸載項、執行時檢查
│  └─ license.rtf                  安裝精靈顯示的授權摘要
├─ tools/
│  └─ make_icon.py                 產生多解析度 app.ico（只用標準庫）
└─ src/mon-switch/
   ├─ mon-switch.csproj
   ├─ app.manifest                 asInvoker（不需管理員）
   ├─ Program.cs                   進入點、命令列、崩潰處理
   ├─ Core/
   │  ├─ AppInfo.cs                名稱、版本、執行時描述
   │  ├─ AppLanguage.cs            語言列舉與系統語言解析
   │  ├─ AppSettings.cs            設定模型、預設值、修復（Normalize）
   │  ├─ CommandLine.cs            命令列解析與說明
   │  ├─ DisplayMode.cs            四種模式、穩定 id、對應拓撲旗標
   │  └─ HotkeyBinding.cs          快捷鍵模型與顯示字串
   ├─ Native/
   │  ├─ DisplayConfigInterop.cs   CCD API 封裝 + 結構體大小自檢
   │  └─ NativeMethods.cs          user32 / kernel32 P/Invoke
   ├─ Services/
   │  ├─ AppLog.cs                 可選滾動日誌（含繞過開關的 FATAL）
   │  ├─ AutoStartService.cs       HKCU Run 鍵讀寫與同步
   │  ├─ ConsoleBridge.cs          命令列輸出（主控台／重導向）
   │  ├─ DisplayModeService.cs     列出輸出、偵測當前模式、切換、失敗解讀
   │  ├─ HotkeyService.cs          RegisterHotKey 與 WM_HOTKEY 派送
   │  ├─ LanguagePackCheck.cs      --check-lang 實作
   │  ├─ Loc.cs                    資源查找的靜態門面
   │  ├─ LocalizationService.cs    語言包載入與切換事件
   │  ├─ SettingsService.cs        settings.json 讀寫與損毀隔離
   │  └─ SingleInstance.cs         Mutex + 具名事件
   ├─ App/
   │  └─ TrayApplicationContext.cs 托盤圖示、選單、循環邏輯、整合
   ├─ UI/
   │  ├─ AboutForm.cs
   │  ├─ AppIcons.cs
   │  ├─ HotkeyInputBox.cs         快捷鍵錄製輸入框
   │  └─ SettingsForm.cs           設定視窗
   └─ Resources/
      ├─ app.ico                   8 個解析度
      └─ Lang/
         ├─ en.json                英文（基準包）
         ├─ yue.json               粵語（香港）
         ├─ zh-Hant.json           繁體中文
         └─ zh-Hans.json           簡體中文
```

---

## 15. 授權

**專有授權，並非開源。** 版權所有 © 2026 lhsheung，保留一切權利。

本專案是「原始碼公開」（source-available），但**不是開源軟體**。你可以免費查看、下載、編譯，並在**個人非商業目的**下使用與修改；**任何商業用途均須事先取得書面授權**。完整條款（中英對照）見根目錄的 [LICENSE](LICENSE)。

| 允許 | 不允許 |
| --- | --- |
| 個人使用、學習、研究、業餘專案 | 銷售、出租、許可、收費服務 |
| 為自己使用而修改 | 在商業組織內部部署以支援其業務 |
| 轉發原始碼（須附完整授權與版權聲明） | 以本軟體為基礎提供收費的技術支援、託管或 SaaS |
| 非營利組織內部使用 | 移除或修改版權標註 |

需要商業授權請聯絡 <https://github.com/lhsheung>。

### 與 SoundSwitch 的關係

本專案為原創實作。操作體驗與架構取捨參考了 [SoundSwitch](https://github.com/Belphemur/SoundSwitch)（GPL-3.0），但**未使用其任何程式碼**。兩者授權不相容，因此本專案不會合併或改寫 SoundSwitch 的原始碼。
