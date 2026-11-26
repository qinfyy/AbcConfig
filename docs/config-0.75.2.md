# Config 0.75.2

## 代码分层

| 模块 | 职责 |
| --- | --- |
| `tools/analysis` | 复用 CyreneDumper 的 `native_probe.py`，从 PE 注册代码和 SDK Dump 生成合同包。 |
| `AbcDumper.Contracts.Model` | 读入并校验版本、构建、清单种子、类型 ID 及已证实的二进制布局。 |
| `Abc.Config/Storage` | 解密清单，验证索引，按区间读取分片。 |
| `AbcDumper.Proto` | 从 `dump.proto` 和 `NameTranslation.txt` 生成 protobuf-net 模型。 |
| `Abc.Config.Core` | 反射查找模型，处理 HNA wire 差异，调用 protobuf-net 和 System.Text.Json。 |
| `Abc.Config.Parser` | 将合同的资源类型 ID 和实际记录绑定到 protobuf 模型。 |
| `AbcDumper.ResourceExport.Cli` | 按类型或语言分组，并写出正文、合同和诊断。 |

`AbcDumper.Proto` 在编译期把 `NameTranslation.txt` 应用到 protobuf-net.BuildTools 的合约输入；原始 `dump.proto` 不改动。反序列化遵循原 wire tag，C# 属性和导出的 protobuf 字段名称使用 NT 语义名。64 位整数和 map 键写作十进制字符串。合同含本构建的清单种子，属于本地分析产物。

## 存储与记录

- 先读取更新目录 `NexusAnima_Data/PersistentAssets/Bin/<ChangeList>` 的下载与加载清单。前 16 字节是 IV，其余为 AES-256-CBC / PKCS7 密文。两份清单共用同一密钥派生方案。
- 记录分片优先读取更新目录，未安装的分片由 `NexusAnima_Data/StreamingAssets/Bin` 回退。按加载清单给出的索引、偏移和长度读取，分片不整体套用清单的 AES。
- 分片记录可为 protobuf，也可为原生结构。只有 protobuf 写入正文。schema 中标注 XOR 的 `uint32` 可在反序列化前恢复，字段未出现时保留默认值；实际声明的 `bytes`（包括 type-prefixed payload）按 Base64 写出，不猜测内层类型。无法读出的 wire 类型及 protobuf-net 保存的未知字段在 `contracts/export.json` 中诊断。
- 原生类型 `0xA0DACFF1` 的头部为 32 字节，含记录 ID、相对记录起点的键和值数组偏移及元素数。键为有序 `uint32`；每个值为 48 字节，首项为相对当前值起点的字符串偏移，其余含三个小端 Vector3 和三个布尔值。字符串为 4 字节长度加 UTF-8 正文。记录标为 `NonProtobuf`，可用 `config-extract` 取得原始字节。

原生证据来自同版本 DummyDll 的 `BC2C1D694F4D41BF`、`BB90D29E6BCF6B85`、`FBDF286172E459E3` 声明，以及对应读取器（RVA `0x04D7ED60`）、键查找（RVA `0x04F28990`）和向量 getter。该布局通过了包内全部 108 条记录的 ID、相对偏移、区间和填充检查。

## TextMap

- 从加载清单选取 `LocalizedTextTable` 记录，分片可同时包含同语言的不同文本组。RVA `0x04BE0B82` 使用 `100000000` 作为记录 ID 的语言步长。根据实包的同键翻译对照，前缀 1 至 13 依次为 `CHS EN JP KR CHT VI DE RU FR TH ES PT ID`，不等同于设置界面的语言枚举顺序。
- 表字段 1 为重复条目；条目字段 3 是 UTF-8 文本，字段 4 是无符号 64 位键。条目中 dump 未声明的字段 1 在实包中均为空；兼容层只忽略已确认的空基类子消息，非空或重复仍记为未知字段。
- 同语言的表记录按记录 ID 分组写到 `TestMap/TextMap<语言>.json`。保留表边界、表内原始条目顺序及空文本，不筛除重复键，也不重建文本字典。
