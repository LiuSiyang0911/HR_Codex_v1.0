# HR_Codex_v1.0

HR_Codex_v1.0 是一个基于 WPF 的雷达数据采集、控制、信号处理与目标检测桌面应用。项目通过 UDP 与雷达或本地数据发送器通信，支持原始数据采集、参数配置、频域/距离-多普勒处理、检测点展示及 CSV 导出。

本文档面向开发维护人员，介绍开发环境、依赖还原、项目结构和主要数据链路。

## 项目简介

解决方案包含两个 .NET Framework 4.7.2 WPF 项目：

- `HR_Codex_v1.0`：主应用，负责设备连接、指令控制、数据采集、信号处理和检测结果管理。
- `UdpDataSender`：本地联调工具，从 `.dat` 文件读取数据并按指定速率通过 UDP 发送。

主应用采用 MVVM 组织界面逻辑，并通过静态 `AppServices` 共享 UDP、数据解析、信号处理、原始数据保存及检测点状态。

## 主要功能

- **UDP 连接**：配置本地/远程 IP 与端口，建立连接并显示通信状态。
- **指令发送**：发送十六进制命令、功放开关命令和预设波形命令。
- **运动控制**：发送左转、右转、停止、使能、回读与查询类控制命令。
- **雷达参数配置**：维护采样长度、距离截取长度、慢时间长度、PRI、带宽、偏移量、检测阈值等处理参数。
- **数据采集**：支持单次采集、循环采集、采集数据保存和本地 `.dat` 文件加载。
- **信号处理**：支持 FFT、PPI、RDM、参考 RDM、形变处理和时域/距离像处理。
- **目标检测**：展示距离、速度、幅度等检测结果，支持手动导出及按场次自动导出 CSV。
- **原始数据落盘**：接收 UDP 数据时可异步保存原始数据，单文件上限为 10 GB。

## 技术栈

| 类别 | 技术/版本 |
| --- | --- |
| 开发语言 | C# |
| 桌面框架 | WPF |
| 目标框架 | .NET Framework 4.7.2 |
| 架构模式 | MVVM |
| 网络通信 | `System.Net.Sockets.UdpClient` |
| 数值计算 | MathNet.Numerics 4.15.0 |
| 图表绘制 | ScottPlot 4.1.74、ScottPlot.WPF 4.1.74 |
| UI 组件 | WPF-UI 2.1.0 |
| 依赖管理 | NuGet `packages.config` |

## 开发环境

推荐使用以下环境：

- Windows 10/11
- Visual Studio 2019 或更高版本
- Visual Studio 工作负载：`.NET 桌面开发`
- .NET Framework 4.7.2 Developer Pack
- NuGet 包管理器

本项目仍使用旧式非 SDK 项目格式和 `packages.config`。仓库没有提交 `packages/` 目录，首次打开解决方案后必须先还原 NuGet 包。仅安装现代 .NET SDK 并执行 `dotnet restore`，通常不能正确还原该项目的依赖。

## 快速开始

### 1. 克隆仓库

```powershell
git clone https://github.com/LiuSiyang0911/HR_Codex_v1.0.git
cd HR_Codex_v1.0
```

### 2. 还原 NuGet 依赖

推荐使用 Visual Studio：

1. 打开 `HR_Codex_v1.0.sln`。
2. 在解决方案资源管理器中右键解决方案。
3. 选择“还原 NuGet 程序包”。
4. 确认仓库根目录生成 `packages/`，且项目引用不再显示警告。

也可以在安装了 `nuget.exe` 的环境中执行：

```powershell
nuget restore HR_Codex_v1.0.sln
```

### 3. 编译运行

1. 将启动项目设置为 `HR_Codex_v1.0`。
2. 选择 `Debug | Any CPU` 或 `Release | Any CPU`。
3. 按 `F5` 调试运行，或按 `Ctrl+F5` 直接运行。

编译产物默认位于：

```text
bin/Debug/
bin/Release/
```

需要本地模拟 UDP 数据时，可将启动项目切换为 `UdpDataSender`，也可以配置解决方案同时启动两个项目。

## 项目结构

```text
HR_Codex_v1.0/
├── Helpers/                  通用 MVVM、数组、FFT、峰值检测和 CSV 辅助类
├── Models/                   雷达配置、以太网命令、检测点及全局处理状态
├── Resources/                应用图标等资源
├── Services/                 UDP、命令构造、数据解析、信号处理和原始数据保存
├── Themes/                   WPF 主题与导航样式
├── Tools/UdpDataSender/      UDP 数据回放与联调工具
├── ViewModels/               各功能页面的状态和命令逻辑
├── Views/                    连接、控制、采集、处理和检测页面
├── App.xaml                  应用资源入口
├── MainWindow.xaml           主窗口与导航容器
├── HR_Codex_v1.0.csproj      主应用项目文件
├── HR_Codex_v1.0.sln         Visual Studio 解决方案
└── packages.config           NuGet 依赖清单
```

主要页面与 ViewModel 对应关系：

| 页面 | ViewModel | 职责 |
| --- | --- | --- |
| `ConnectionView` | `ConnectionViewModel` | UDP 端点配置、连接与断开 |
| `CommandView` | `CommandViewModel` | 十六进制、功放和波形命令发送 |
| `MotionView` | `MotionViewModel` | 运动、回读和查询命令发送 |
| `RadarConfigView` | `RadarConfigViewModel` | 雷达与信号处理参数配置 |
| `AcquisitionView` | `AcquisitionViewModel` | 单次/循环采集、保存与加载数据 |
| `ProcessingView` | `ProcessingViewModel` | 数据解析、处理模式选择和图形更新 |
| `DetectionView` | `DetectionViewModel` | 检测点展示、清空和 CSV 导出 |

## 核心架构与数据流

### MVVM 分层

- `Views/` 负责 XAML 界面和少量图表交互代码。
- `ViewModels/` 负责绑定属性、命令、状态更新和业务流程编排。
- `Models/` 保存雷达参数、命令和检测结果等数据结构。
- `Services/` 封装网络通信、二进制解析、信号处理和文件写入。
- `AppServices` 作为共享服务入口，连接采集、处理和检测页面之间的事件与状态。

### 数据链路

```text
雷达 / UdpDataSender
        │ UDP 数据包
        ▼
UdpService.DataReceived
        ├──► RawDataSaveService（可选原始数据落盘）
        └──► AcquisitionViewModel（按目标大小组成采集批次）
                  │ DataBatchReady
                  ▼
          ProcessingViewModel
                  ├──► DataParserService（帧头定位、I/Q 与方位数据解析）
                  └──► SignalProcessingService
                          ├── FFT / PPI / RDM / TD 等处理
                          └── DetectionPoint[]
                                   ▼
                         DetectionViewModel / CSV 导出
```

`DataParserService` 使用 `AA BB 55 66` 作为帧头标记，将帧内数据拆分为 I/Q 采样和方位数据。`SignalProcessingService` 根据 `RadarConfig` 初始化距离、速度等处理状态，并执行对应算法。

## 配置与输出

### 雷达参数

全局参数保存在 `Models/RadarConfig.cs`，修改参数后，`RadarConfigViewModel` 会重新初始化 `SignalProcessingService`。维护算法时应同步检查参数约束和数组尺寸，尤其是：

- `Nr`：快时间长度
- `Ncut`：距离截取长度
- `Np`：慢时间长度
- `Pri`：脉冲重复间隔
- `Bandwidth`：带宽
- `Vptz`：方位相关参数
- `Offset`：数据偏移
- `Rr`：形变观测单元
- `Threshold`：检测阈值
- `PointsMax`：检测点数量上限

### 文件输出

- 原始数据默认目录：`%USERPROFILE%\Documents\HR_Codex_v1.0\RawData`
- 自动检测结果目录：`%USERPROFILE%\Documents\HR_Codex_v1.0\DetectionExports`
- 手动采集数据：通过文件对话框保存为 `.dat`
- 手动检测结果：通过文件对话框保存为 `.csv`

原始数据保存采用后台队列写入：先生成临时文件，停止保存或达到 10 GB 后再完成最终 `.dat` 文件。

## 辅助工具：UdpDataSender

`Tools/UdpDataSender` 是独立的 WPF 工具，可用于没有实际设备时的本地联调：

1. 选择一个 `.dat` 数据文件。
2. 配置目标 IP、目标端口、可选源 IP/端口及每包字节数。
3. 设置发送速率和是否循环发送。
4. 启动发送，在主程序中使用匹配的本地端口接收。

仓库包含样例文件：

```text
Tools/UdpDataSender/H1_197_H2_300_卓尔南20m_3.dat
```

该文件约 73 MB，GitHub 会提示它超过推荐的 50 MB 上限，但仍低于普通 Git 文件的 100 MB 硬限制。

## 开发注意事项

1. **命名空间尚未随项目名升级**：项目程序集名为 `HR_Codex_v1.0`，但根命名空间和现有源码仍使用 `HR_Codex_v0`。新增代码应遵循当前命名空间，除非计划一次性完成全项目迁移。
2. **依赖目录不入库**：`.gitignore` 已忽略 `packages/`，新工作副本必须先执行 NuGet 还原。
3. **不要提交构建产物**：`.vs/`、`bin/`、`obj/`、缓存、日志和 PDB 文件均已忽略。
4. **UI 与后台线程切换**：UDP 接收、处理和保存包含后台任务；更新 WPF 绑定集合或属性时应通过 Dispatcher 切回 UI 线程。
5. **共享状态范围较大**：`AppServices` 保存最新数据批次、全局雷达配置和检测点集合。修改事件订阅或生命周期时，需要检查多个页面之间的影响。
6. **数据格式与算法参数强相关**：帧长度、I/Q 排列和处理矩阵尺寸依赖雷达配置，调整协议或参数时应同时检查 `DataParserService` 和 `SignalProcessingService`。
7. **暂无自动化测试工程**：修改解析或算法代码后，至少应使用固定 `.dat` 数据完成回归，并核对输出维度、检测点和图形结果。

## 版本管理

主分支为 `main`，远程仓库为：

```text
https://github.com/LiuSiyang0911/HR_Codex_v1.0.git
```

当前样例 `.dat` 文件由普通 Git 直接管理。若后续继续加入大体积数据集，建议迁移到 Git LFS，或将数据集放到独立的发布附件/对象存储中，避免仓库历史持续膨胀。
