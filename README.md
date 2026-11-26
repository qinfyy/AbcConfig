# ABCConfig

导出 Honkai: Nexus Anima CNCBWindows0.75.2 的 Config 和 TextMap。

## 快速开始

```powershell
$game = 'NexusAnima游戏目录'
$contracts = '.\out\analysis\3133461'
$output = '.\out\3133461'

dotnet build .\AnimeSDK.slnx -c Release
dotnet run --project .\CyreneDumper.Analysis.Cli -c Release --no-build -- all --game $game --build 3133461 --out $contracts
dotnet run --project .\AbcDumper.ResourceExport.Cli -c Release --no-build -- config-export --game $game --contracts $contracts --out $output
```

分析规则针对 Windows 0.75.2。默认从客户端 `DUMP` 查找 SDK 索引，也可通过 `--dump` 指定 Dump 目录或 `il2cpp.json` 文件。分析与导出分别使用空目录。已有包含 `textMap` 路由的 `contracts.json` 时，可跳过分析。

## 输出

- `Bin/<类型名>.json`：按记录 ID 排序的 protobuf 消息数组。文件名优先用语义名，缺失或冲突时保留混淆名。
- `TestMap/TextMap<语言>.json`：每种语言一个 `LocalizedTextTable[]`，保留每条表记录及其 `Entries`，不按 `TextKey` 合成字典。
- `contracts/contracts.json`：当前运行使用的输入Schame；`contracts/export.json`：导出统计和诊断。

正文由 protobuf-net 反序列化后的对象直接交给 `System.Text.Json`，不添加资源 ID、原始记录字节或诊断。缺少 schema、无法反序列化和非 protobuf 的记录不会写入正文；成功读出的已知字段可导出，但未知字段会标为部分解码。退出码 `0` 表示全部完整，`2` 表示有跳过或未完整记录，`1` 表示操作失败。

资源格式见 [Config 格式](docs/config-0.75.2.md)。
