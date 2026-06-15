# Chinese Developer README Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a Chinese `README.md` that lets a new maintainer understand, restore, build, run, and navigate the HR_Codex_v1.0 WPF project.

**Architecture:** The README is a single developer-facing entry point grounded in the existing project file and source tree. It documents only capabilities verified in code, then uses static checks to ensure every required section and key fact is present.

**Tech Stack:** Markdown, PowerShell, Git, WPF, .NET Framework 4.7.2, NuGet `packages.config`

---

### Task 1: Verify Project Facts

**Files:**
- Read: `HR_Codex_v1.0.csproj`
- Read: `packages.config`
- Read: `ViewModels/MainViewModel.cs`
- Read: `Services/AppServices.cs`
- Read: `Services/UdpService.cs`
- Read: `Services/RawDataSaveService.cs`
- Read: `Services/SignalProcessingService.cs`
- Read: `Services/DataParserService.cs`
- Read: `Tools/UdpDataSender/UdpDataSender.csproj`

- [ ] **Step 1: Confirm framework and package versions**

Run:

```powershell
rg -n "TargetFrameworkVersion|Reference Include|HintPath" HR_Codex_v1.0.csproj
Get-Content packages.config
```

Expected: `.NET Framework v4.7.2` and package references for MathNet.Numerics 4.15.0, ScottPlot 4.1.74, ScottPlot.WPF 4.1.74, and WPF-UI 2.1.0.

- [ ] **Step 2: Confirm navigation modules and shared services**

Run:

```powershell
rg -n "ConnectionView|CommandView|MotionView|RadarConfigView|AcquisitionView|ProcessingView|DetectionView" ViewModels/MainViewModel.cs
rg -n "UdpService|SignalProcessing|DataParser|RawDataSaver|DetectionPoints|DetectionAutoExportDirectory" Services/AppServices.cs
```

Expected: seven application views and shared UDP, processing, parsing, raw-data saving, and detection services.

- [ ] **Step 3: Confirm output behavior and helper tool scope**

Run:

```powershell
rg -n "MyDocuments|DetectionExports|ExportDetectionPointsCsv|DataReceived" Services/AppServices.cs Services/RawDataSaveService.cs
rg -n "TargetFrameworkVersion|UdpClient|Send|dat" Tools/UdpDataSender/UdpDataSender.csproj Tools/UdpDataSender/MainWindow.xaml.cs
```

Expected: detection CSV output under the user's Documents directory and a separate WPF UDP data sender utility capable of sending sample data.

### Task 2: Write And Validate The README

**Files:**
- Create: `README.md`
- Reference: `docs/superpowers/specs/2026-06-15-chinese-readme-design.md`

- [ ] **Step 1: Create the developer-facing Chinese README**

Create `README.md` with these exact top-level sections:

```markdown
# HR_Codex_v1.0

## 项目简介
## 主要功能
## 技术栈
## 开发环境
## 快速开始
## 项目结构
## 核心架构与数据流
## 配置与输出
## 辅助工具：UdpDataSender
## 开发注意事项
## 版本管理
```

Include verified package versions, Visual Studio/NuGet restore steps, build instructions for `HR_Codex_v1.0.sln`, the seven view modules, the `AppServices` data path, the detection export directory, the large `.dat` sample warning, and the `HR_Codex_v0` namespace note.

- [ ] **Step 2: Verify required sections and facts**

Run:

```powershell
rg -n "^## (项目简介|主要功能|技术栈|开发环境|快速开始|项目结构|核心架构与数据流|配置与输出|辅助工具：UdpDataSender|开发注意事项|版本管理)$" README.md
rg -n "\.NET Framework 4\.7\.2|MathNet\.Numerics 4\.15\.0|ScottPlot 4\.1\.74|WPF-UI 2\.1\.0|HR_Codex_v0|DetectionExports|H1_197_H2_300_卓尔南20m_3\.dat" README.md
```

Expected: all 11 headings and every listed implementation fact are present.

- [ ] **Step 3: Scan for placeholders and inspect the final diff**

Run:

```powershell
rg -n "TBD|TODO|待定|待补充|以后完善" README.md
git diff --check
git diff -- README.md
```

Expected: placeholder search returns no matches, `git diff --check` returns no errors, and the README diff contains only the intended developer documentation.

- [ ] **Step 4: Commit the README**

Run:

```powershell
git add README.md docs/superpowers/plans/2026-06-15-chinese-readme.md
git commit -m "docs: add Chinese developer README"
```

Expected: one commit containing the README and its implementation plan.
