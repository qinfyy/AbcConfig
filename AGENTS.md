# ABCConfig 开发规范

本文件适用于 `Config` 仓库根目录及其子目录。使用中文交流、编写项目文档及业务注释，命令示例使用 PowerShell。开始前检查 `git status --short`，阅读对应入口、调用链、项目引用和失败处理。保留用户已有改动及暂存状态。相邻的 `ProtoDumper` 是上游数据来源，不属于本仓库；不因迁移 Config 顺手修改其他项目。

## 1. 当前入口与输入

- 本仓库面向 Honkai: Nexus Anima Windows x64 离线分析，使用 .NET 10，解决方案为 `AnimeSDK.slnx`。`Directory.Build.props` 统一目标框架、隐式 using、可空引用类型和警告即错误。参考仓库只用于理解设计，本仓库的构建和运行不应依赖 `E:\CyreneDumper`。
- `CyreneDumper.Analysis.Cli` 的 `all` 一次恢复 Config 类型注册、已验证原生布局和 TextMap 路由。`config` 是同一命令的兼容别名。选项为 `--game`、`--build`、`--out` 和可选 `--dump`。`--build` 是数字 ChangeList，不是应用版本。
- 分析读取客户端根目录的 `GameAssembly.dll` 和同批 SDK 索引。默认从客户端 `DUMP` 查找 `il2cpp.json` 或 `Script_IDA/il2cpp.json`；两处都有时用 `--dump` 明确指定文件。需要托管类型、字段或方法声明时使用同批 `DummyDll`。目录名 `Script_IDA` 仅是 Dump 的布局，常规流程不运行 IDA、不启动或注入客户端。
- `AbcDumper.ResourceExport.Cli` 用 `config-inspect` 检查两份清单和分片，用 `config-extract` 提取指定类型 ID、记录 ID 的原始正文，用 `config-export` 批量写正文和报告。导出只依赖合同、生成的 schema 和资源文件，不读取 PE 或 DummyDll。
- 资源更新目录是 `NexusAnima_Data/PersistentAssets/Bin/<ChangeList>`，缺少的分片按既有规则回退到 `NexusAnima_Data/StreamingAssets/Bin`。应用版本、ChangeList、PE、索引和资源包分别确认，不因路径同名或版本号相同就认为可以混用。
- 分析和导出使用独立空目录，不能写入客户端或输入目录。有历史结果时先在新的空目录验证，再明确发布本轮产物；不要清空版本根目录来绕过检查。参数以两条 CLI 的 `--help` 为准。

## 2. 模块职责和依赖

| 项目 | 职责 |
| --- | --- |
| `CyreneDumper.Msil.Resolver` | Mono.Cecil 读取 DummyDll，解析 Inspector 中的类型、方法、字符串引用槽及原生类型信息。封装 Dump 输入与查询。 |
| `CyreneDumper.Analysis.Engine` | Iced 指令解码、x64 PE 地址映射、函数边界、基本块、跳表、CFG、符号追踪与原生分析会话。原 `Native.Analysis` 框架已并入此项目。 |
| `CyreneDumper.Analysis.Cli` | `AnalysisApplication` 统一注册分析命令、验证参数、打开会话。对应数据域负责从分析事实恢复合同、解释游戏布局并写出结果，`Program.cs` 只启动应用。 |
| `AbcDumper.Contracts.Model` | 合同 DTO 及 JSON 加载、结构校验、客户端绑定。保持零项目引用。 |
| `AbcDumper.Proto.Build` | 编译期应用 NT 和 `abc_options.proto`，调用 protobuf-net 的 protogen 生成 CLR 模型并附加 XOR 特征。 |
| `AbcDumper.Proto` | 管理 `dump.proto`、`NameTranslation.txt` 和增量生成目标；提供生成的 CLR 模型。 |
| `AbcDumper.Bin.Serialization` | protobuf-net 扩展：按 CLR 元数据处理已验证的 ABC wire 差异、XOR 和未知字段检查。不依赖游戏模型、PE 或合同。 |
| `AbcDumper.Bin` | SDK API：打开包和清单、定位分片、限制正文读取区间、绑定 schema 和解码记录。 |
| `AbcDumper.ResourceExport.Cli` | 加载合同、调用 SDK、决定分组和文件名、序列化正文、统计诊断并写报告。 |
| `AnimeSDK.Common` | 通用有界流、二进制资源接口、随机访问、路径保护与原子文件写入。 |

```text
Analysis.Cli       -> Analysis.Engine -> Msil.Resolver
                   -> Contracts.Model
ResourceExport.Cli -> Contracts.Model
                   -> AbcDumper.Bin -> AbcDumper.Proto -> AbcDumper.Bin.Serialization
AbcDumper.Proto    -> AbcDumper.Proto.Build（仅编译期）
```

图中仅列主要依赖，公共 I/O 由实际使用方引用。分析链不依赖导出链，SDK 不依赖 CLI、合同 DTO 或反汇编；调用方可以用自己的清单种子、类型名映射和 codec 选择调用 SDK。不要恢复独立的 `Native.Analysis`，也不要把 HNA 版本、游戏类型名、固定 RVA 或语言顺序写入通用分析框架。

| 改动 | 归属 |
| --- | --- |
| Inspector JSON、DummyDll、类型和原生引用地址查询 | `CyreneDumper.Msil.Resolver/Input/`、`Model/` |
| VA/RVA、函数读法、通用指令判断、控制流和符号状态 | `CyreneDumper.Analysis.Engine/Image/`、`Disassembly/`、`Symbolic/`、`Client/` |
| HNA 注册定位、RawConfig 读取器验证、TextMap 路由 | `CyreneDumper.Analysis.Cli/Config/` 或新增对应数据域 |
| 分析命令注册、选项、顶层错误和退出码 | `CyreneDumper.Analysis.Cli/AnalysisApplication.cs` |
| 合同 DTO、版本、字段校验 | `AbcDumper.Contracts.Model/`、`Loader/`；对应写出逻辑留在分析 CLI |
| 清单、索引、分片、正文与 SDK 读法 | `AbcDumper.Bin` 的 `ConfigPackage`、`ConfigIndex`、`Storage/`、`Reading/` |
| Proto 重命名、编译器选项和生成目标 | `AbcDumper.Proto`、`AbcDumper.Proto.Build` |
| 经实证的非标准 wire、字段特征与转换 | `AbcDumper.Bin.Serialization` |
| 导出分组、文件命名、JSON 字段呈现、失败统计 | `AbcDumper.ResourceExport.Cli/Config/` 或新增对应数据域 |

## 3. 合同、Proto 和输出约定

### 分析合同

- 当前格式为 `abc-config-contracts-v1`。入口固定为 `contracts.json`，包含 `appVersion`、`changeList`、`config.manifestSeed`、`config.types`、`config.rawCodecs`；完整分析另提供 `textMap.recordType`、`recordStride` 和 `languages`。类型 ID 按无符号数处理，写出用固定宽度的 `0xXXXXXXXX`，这与正文文件名是两套约定。
- 合同只携带运行时实际使用的数据，稳定排序，并由 `ConfigContractLoader` 校验。PE 地址、寄存器、调用点、来源及失败线索留在版本说明或独立分析报告。不要把诊断、样本正文、外部绝对路径或未验证猜测塞进合同；含清单种子的合同是本地分析产物，不默认提交。
- 新增域时先判断是否共享现有 Config 索引和生命周期。可兼容的新增项保持可选且显式校验；不兼容的语义修改必须升级格式。写入器、加载器、CLI 调用和既有合同兼容性一起验证。若引入多个合同成员，先写成员，再原子发布入口。不得把缺失的必需证据默默写成空映射。

### Proto 与 NameTranslation

- `dump.proto` 保留原始消息、枚举、嵌套关系和 tag。向上游补漏时保留程序集边界（`// Assembly: ...`）、消息与枚举分类及可复查的原生证据。不要凭导出字段名、消息名或网络消息 ID 推导 Config 的 type ID 或 wire tag。
- `NameTranslation.txt` 每行是 `混淆标识 语义标识`，可用空行及以 `#`、`;`、`//` 开头的整行注释。同名字段优先用 `混淆类型.混淆字段 语义字段`，嵌套作用域沿原始声明。冲突要先确认归属，不依赖映射顺序。NT 只改变符号命名，不改 tag、wire type、枚举数值、默认值或正文结构。
- 生成链为 `dump.proto + NameTranslation.txt + abc_options.proto -> AbcDumper.Proto.Build -> dump.g.cs`。由 MSBuild 写入中间目录并参与编译；不要维护、提交生成的 C#，不要在 SDK 里为两套名称做兜底赋值，也不要在导出 JSON 后再批量替换字段名。检查 CLI 构建与 VS 中的增量生成，`obj` 不应作为项目内容出现。
- XOR 先证明字段 tag、wire type、掩码及适用场景，再在 dump 中标注 `// XOR: 0x...`。构建期生成 `[AbcProtoXor(Mask = 0x...)]`，运行期按类型与字段元数据处理。目前生成器接受标注的 `uint32`（含 repeated）；扩展到其他类型前，分别验证字段缺席、显式零、嵌套、集合的 packed/unpacked 或映射场景中实际受影响的形式。不能将字段未出现当作零再执行 XOR。

### 正文标准

- 用 protobuf-net 生成的模型反序列化，再用 `System.Text.Json` 写正文，沿用 `ProtoJson` 的 protobuf 字段筛选、存在性、tag 排序和 64 位值格式。保留原始对象、集合、map 及 oneof 结构；不注入资源 ID、原始正文、解析状态或自创字段。实际声明为 `bytes` 的数据保持 Base64，不因能试解就改成嵌套对象。
- 正式目录固定为 `Bin/<类型名>.json`、`TestMap/TextMap<语言>.json` 和 `contracts/`，不新增 `Bin/config`。文件名优先用可信语义名，缺失或冲突时保留原始混淆名称，不用 `0xXXX` 作为正式正文文件名。保留真实类型身份，并用稳定的记录 ID 排序。
- TextMap 保留每条表记录及其 `Entries`，保留原顺序、空文本和重复键。不扁平合成字典，不用设置界面的语言枚举猜分片顺序。报告列出无法识别的语言桶。当前保留历史输出目录名 `TestMap`，不要只为拼写调整破坏产物路径。
- `Partial` 可按当前约定写出已知字段，报告必须保留未知字段信息；`MissingSchema`、`DecodeFailed` 和 `NonProtobuf` 不生成伪 protobuf 正文。原生数据如需正式导出，必须先实现明确的 SDK codec 并约定独立的数据结构，禁止把原生布局包装成假 Proto 消息。

## 4. 适配新版本

1. **锁定输入。** 记录 AppVersion、ChangeList、平台、同一客户端的 PE、SDK 索引及资源包来源。保存旧合同和旧导出作为对照，不放到新分析的输入里。安装包与更新包不匹配时先修正输入，不能靠关闭版本校验让流程继续。
2. **核对上游 schema。** 检查新 `dump.proto` 的程序集、消息数、枚举、UNRESOLVED 项和 XOR 变化。优先用已验证的写入器确认 tag 和实际 wire，用合并读取器、字段声明、默认值和实包交叉验证。单一方向恢复不出来的字段保留疑点；不要凭同名变量补 tag 或把有冲突的 NT 沿用到新类型。
3. **确定原生入口。** 在 `ConfigAnalyzer`、`ConfigRegistrationReader`、`RawConfigLayout`、`TextMapLayout` 对应职责内迁移。优先用当前 metadata 关系、可达调用、注册实参和稳定的指令形状定位。核对函数边界、调用目标、RIP 相对引用以及 VA/RVA 的原点，不能用旧地址替换表暂时凑出结果。必须使用特定版本的 RVA、预期数量或 ABI 时，集中在对应分析代码并验证当前映像；不靠遍历混淆名定位入口，不让不支持的版本悄悄套用旧规则。
4. **验证清单与索引。** 先跑分析，再跑 `config-inspect`，核对实际两份清单的版本、ChangeList、分片名和长度、回退来源、索引列及记录区间。清单失败先核对本批输入和分析出的种子；只有确认布局或算法确实变化才修改 `AbcDumper.Bin`，不要把密文分片一律当成清单解密。
5. **跑针对性样本。** 用 `config-extract` 取异常记录，按 type ID、record ID 和分片区间复查。类型绑定、正常和边界值、空字段、XOR、原生记录与 TextMap 各取能区分新旧布局的样本。TextMap 单独核对根类型、步长、语言桶及同键翻译；语言集合可能增加、减少或调序，旧版 13 桶不是通用前提。
6. **完整导出并复盘。** 从同一资源包重跑全量。确认所有索引记录都有终态，检查分片问题、类型绑定、语言覆盖及状态计数，抽查实际 JSON 的字段和值。结论注明已验证版本、原生布局、样本、退化与未完成项。除非明确要求多版本并存，不为单次迁移复制项目或堆一套运行时；需要并存时再设计可核实的版本选择和拒绝路径。

### 常见症状和定位顺序

| 症状 | 先核对 |
| --- | --- |
| Dump 不能加载或找不到入口 | 索引实际位置、是否存在两个候选，与 PE 是否同批，函数起点及 VA/RVA 是否一致。 |
| 合同和资源包版本不符、清单解密失败 | 分别确认 AppVersion、ChangeList、资源目录、清单来源和本批分析的种子，保留校验。 |
| `UnmappedType` | 当前包 type ID 是否真实存在；分析是否漏了注册函数、注册路径或单例调用。 |
| `MissingSchema` | 合同原始类型名、程序集、嵌套类型或别名是否能与生成模型的原始 protobuf 名绑定；检查 schema 是否确实缺失。 |
| `Partial` | 报告里的字段路径、wire 类型与原生读写证据。未知不等于可忽略，不添加全局丢弃规则。 |
| `DecodeFailed` | 抽取一条原始记录核对区间、tag、字段类型、packed/oneof 和 XOR，确认它不是原生布局后再改 Proto。 |
| `NonProtobuf` | 验证合同 codec 注册及已证明的偏移、大小、键和范围；未实现 SDK 读法前只提供原始提取和诊断。 |
| `ReadFailed`、分片不可用 | 文件是否存在、分片尺寸、索引区间和回退来源。不能用 schema 修复存储层错误。 |
| `UnmappedLanguage` 或语言文件数不对 | 检查 TextMap 根类型、记录 ID 商及实际同键文本，不按语言枚举顺序补齐。 |

## 5. 新增数据域、类型或 codec

- **先确认证据。** 从本批清单、索引、记录和读取入口判断数据是否真实存在、如何定位、用的是什么编码、身份与边界是什么。没有逻辑资源路径时就用已证实的索引身份，不造 Excel 路径或借用其他游戏的容器。历史导出、样本文本和相似结构只作对照。
- **同一个 Config 包新增 protobuf 类型。** 注册与索引能找到记录时，先补同批 dump 中的消息定义及可信 NT，让编译器生成模型并通过标准绑定读取；不单独手写 DTO、重复反射字典或为单个表增加 CLI 命令。验收包括实际类型 ID、模型身份、若干非默认字段和失败样本。
- **同一个包新增原生布局。** 在分析域收集入口、边界、字段宽度和偏移原点，合同只写已证实的 codec 标识和必要参数。SDK 读取器基于有界正文实现解码，校验计数、整数溢出、字符串长度、偏移和递归。未验证的记录保留 `NonProtobuf`，不要误报完整。若只支持读取，明确说明，不据此宣称能重建包或回写客户端。
- **新增独立存储域。** 先明确合同、资源源、SDK 读取器、正文模型和导出会话的边界，并记录实际目录、容器、索引和命名规则。仅当格式确实独立时才提取项目，否则在现有项目按职责分目录。分析命令仍在 `AnalysisApplication` 注册；扩展 `all` 时明确包含域、失败策略和合同格式，不能静默跳过所请求的域。
- **新增跨记录引用。** 保留调用点、源类型、源字段与目标身份，使用完整解码和已验证映射进行查找。处理多候选、别名冲突、循环和未命中，不让部分对象或猜测扩散成正式结果。将依赖关系和诊断写入报告，不修改原 Proto 正文契约。
- **新增 wire 扩展。** 明确作用域与触发条件，以自定义特征、注册或正式 schema 表达。只能对已证明的类型或字段生效，缺失证据保持未知。验证异常输入不越界、不使其他字段的流游标错位，普通 protobuf 的行为保持不变。

## 6. C# 代码规范

- 延续所在模块的缩进、换行和命名风格。新代码使用文件作用域命名空间；类型、公开成员用 PascalCase，局部变量用 camelCase，私有实例字段用 `_camelCase`。`Msil` 的目录、项目名和命名空间写法一致。业务注释写中文，只解释来源、布局依据或非显然的约束；公开 SDK API 用 XML 注释写清输入、返回、异常和流所有权。
- 按职责组织对象。会话持有当批输入和资源，索引负责定位，读取器负责限定区间，业务分析器解释取证，导出会话负责分组、写出和终态。除简单 DTO 或无状态小工具外，不把状态与行为拆成一串共享静态字典和工具函数。接口服务于真实的多实现或稳定扩展点，不抽取只供一个内部调用点使用的空壳。
- 复用 `NativeImage`、`NativeControlFlowGraph`、`NativeTrace` 和 Resolver；注册读法区分函数全体指令与可达路径。原始二进制上出现相同指令不等于证明同一语义。结构化输入用现有 JSON、PE 或 protobuf API，不另起一套 ad hoc 文件解析器。
- 资源源返回有限长度的正文。绝对位置、相对原点、大小单位、字节序和 signed/unsigned 需要明确。计数、乘法、偏移、索引和读取上界在最早能确认的位置检查。流和映像的句柄由所有者释放，并发读取使用独立游标；缓存必须绑定到正确的类型或本批会话，不得跨客户端复用分析结果。
- 底层保留阶段和原因，上层补充来源、类型、记录与区间。不要捕获所有异常后返回空数据；将不支持、部分解码、读失败、分析失败和取消区分开。批处理传递取消令牌，在记录或阶段边界检查，进度反映实际处理数量，不伪造总量或完成度。
- 路径构造和文件发布复用 `SafePath`、`ExportDirectory`、`AtomicFile`。保持客户端、Dump 和参考资源只读。不要把机器绝对路径写入代码默认值；输出报告引用使用相对路径，正文文件名需经过安全检查。

## 7. 验证与交付

在仓库根目录运行。以下命令使用独立的验证目录，避免覆盖已有正式输出：

```powershell
$game = Read-Host '本批客户端根目录'
$changeList = Read-Host '本批资源 ChangeList'
$run = Join-Path '.\out\verify' "$changeList-$([guid]::NewGuid().ToString('N'))"
$contracts = Join-Path $run 'analysis'
$output = Join-Path $run 'export'

dotnet build .\AnimeSDK.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw '构建失败' }
dotnet run --project .\CyreneDumper.Analysis.Cli -c Release --no-build -- all --help
dotnet run --project .\AbcDumper.ResourceExport.Cli -c Release --no-build -- config-export --help
dotnet run --project .\CyreneDumper.Analysis.Cli -c Release --no-build -- all --game $game --build $changeList --out $contracts
if ($LASTEXITCODE -ne 0) { throw '分析失败' }
dotnet run --project .\AbcDumper.ResourceExport.Cli -c Release --no-build -- config-inspect --game $game --contracts $contracts
if ($LASTEXITCODE -ne 0) { throw '清单或分片检查未通过' }
dotnet run --project .\AbcDumper.ResourceExport.Cli -c Release --no-build -- config-export --game $game --contracts $contracts --out $output
$exportExit = $LASTEXITCODE
if ($exportExit -notin 0, 2) { throw "导出失败：$exportExit" }
$report = Get-Content -LiteralPath (Join-Path $output 'contracts/export.json') -Raw | ConvertFrom-Json
$report | Select-Object AppVersion, ChangeList, Records, Complete
$report.Counts
```

- C# 改动至少要求 Release 构建零警告、零错误。CLI 验证帮助、必需参数、错误退出与实际命令。合同、读取器、codec 或导出改动须用同批真实样本跑相关路径；有相应测试时执行并报告结果，不声称执行了不存在的测试。修改 Proto 或生成器时验证从 schema 重新生成并能读取样本。纯文档改动检查相对链接、命令、示例路径、项目名和 `git diff --check`。
- 分析成功只说明所请求的合同生成成功。导出退出码 `0` 表示完整、`2` 表示扫描结束但仍有非完整记录或资源问题、`1` 表示操作失败；主动取消与失败分开。以 `contracts/export.json` 的 `Complete`、`Counts`、`Files` 和 `Issues` 为准，没有终态报告的运行不算完成。验证报告中的文件确实存在、计数能对回清单、所有宣称的语言有正文，并抽查类型名、结构和字段值。
- 对比真实来源、区间、解析结构和正文，不以额外计算的文件摘要代替验证。原生哈希 ID、协议规定的校验或密钥派生仍按实际格式处理。基线计数不是代码常量，以该批输入及报告为准。
- 当前可复查的基线为 Windows 0.75.2 / ChangeList `3133461`：合同 604 个类型、1 个原生布局、13 种 TextMap 语言；实包扫描 67,054 条，写出 523 个 Config JSON 和 13 个 TextMap JSON。已知只有 36,186 条完整解码，其他状态见本地 `out/3133461/contracts/export.json`。扫描覆盖与解码完整性必须分别陈述。
- 指定正式版本目录时，发布 `Bin`、`TestMap`、`contracts` 到 `out/<ChangeList>/`；不覆盖未被明确授权替换的旧产物。交付说明记录用户可访问的路径、版本、验证命令、统计和已知问题。客户端、完整 Dump、运行合同、生成目录及批量正文留在本地，检查 `git status` 后再提交。提交、改写历史或推送按用户授权执行；提交信息沿用 `type(scope): 摘要`，正文说明变化、验证和遗留项。

快速开始见 [README.md](README.md)。版本专项证据、格式和 SDK 示例见 [Config 0.75.2](docs/config-0.75.2.md)。新版本的偏移、证据和差异放在对应版本文档；本文件维护跨版本的工作流程与边界。
