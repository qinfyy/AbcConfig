# NexusAnima Config

离线导出 NexusAnima Windows 0.75.2 的 Config 和 TextMap。消息结构来自 `AbcDumper.Proto/dump.proto`，字段名由同目录的 `NameTranslation.txt` 映射。分析合同只负责资源定位，导出阶段不读取 PE 或 SDK Dump，也不需要 IDA。

## 快速开始

在 Config 目录运行。分析需要 Python、Capstone 和 pefile，构建和导出需要 .NET 10。

```powershell
$game = 'D:\Games\NexusAnima Game'
$contracts = '.\out\3133461\analysis'
$output = '.\out\3133461\export'

dotnet build .\AnimeSDK.slnx -c Release
py .\tools\analysis\config.py --game $game --build 3133461 --tools E:\CyreneDumper\tools --out $contracts
dotnet run --project .\AbcDumper.ResourceExport.Cli -c Release --no-build -- config-export --game $game --contracts $contracts --out $output
```

分析默认从客户端 `DUMP2` 查找 SDK 索引，可通过 `--dump` 指定。分析与导出分别使用空目录。已有包含 `textMap` 路由的 `contracts.json` 时，可跳过分析。

## 输出

- `Bin/<类型名>.json`：按记录 ID 排序的 protobuf 消息数组。文件名优先用语义名，缺失或冲突时保留混淆名。
- `TestMap/TextMap<语言>.json`：每种语言一个 `LocalizedTextTable[]`，保留每条表记录及其 `Entries`，不按 `TextKey` 合成字典。
- `contracts/contracts.json`：当前运行使用的输入合同；`contracts/export.json`：导出统计和诊断。

正文由 protobuf-net 反序列化后的对象直接交给 `System.Text.Json`，不添加资源 ID、原始记录字节或诊断。缺少 schema、无法反序列化和非 protobuf 的记录不会写入正文；成功读出的已知字段可导出，但未知字段会标为部分解码。退出码 `0` 表示全部完整，`2` 表示有跳过或未完整记录，`1` 表示操作失败。

SDK 调用、项目职责和资源格式见 [Config 格式](docs/config-0.75.2.md)。运行 `dotnet test .\AnimeSDK.slnx -c Release` 验证读取和 XOR 行为。
