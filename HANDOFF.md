# LiteDB / EF Core 10 后续能力实现结果

更新：2026-11-06，Asia/Shanghai。唯一工作区仍为 `D:\source\EntityFrameworkCore.LiteDB`，无新 worktree。此次实际启动 Bulk、Complex、Native 三个子智能体并行实现，主智能体串行接线及验证。下面旧交接保留为历史基线，不代表仍待启动的任务。

**最新发布状态：1.1.0 已发布。** 用户随后明确授权发布新版本，tag `v1.1.0` 指向 `30cd16022b8ccf295b876cf0a50857a2949566d5`；发布 CI `42083398903` 的 build/test/pack/publish 全部通过，133项测试无跳过。NuGet.org V2与V3分别使用全新缓存消费通过，公开DLL与CI artifact逐字节一致，源码SHA匹配；GitHub Release附同一CI包。证据见 `docs/nuget-release.md` 和 `artifacts/release-1.1.0-ci/final-receipt.json`。下文“尚未发布”描述是这次发布授权前的历史状态，不能继续当作当前状态。

## 当前能力与结果

- LDB-004：同步/异步 ExecuteUpdate/Delete、参数/旧行 setter、affected rows、分页、失败整批回滚、显式事务、取消；更新真实调用 LiteDB UpdateMany，删除调用 DeleteMany；不物化/刷新 Tracker。complex 叶子含 optional presence、computed 重算已集成。单集合来源以外、owned/继承、PK/computed直接赋值/整complex setter仍明确拒绝。
- LDB-005：新增数值混合方向 ThenBy 原生复合排序、DateTime ticks、TimeOnly/TimeSpan 和精确 decimal SUM/AVG、数值MIN/MAX；通过按 ContextId 关联的诊断证明执行路径。字符串文化、DateTimeOffset、float/NaN、强制数值转换、complex路径继续 CLR fallback。修复默认 DateTimeOffset comparer 把 UTC 相等误改为 EqualsExact 的问题。
- LDB-006：必需/可选/嵌套 ComplexProperty，叶子/完整投影、跟踪、null vs全默认对象、AutoDetectChanges关闭、BSON重开；ToJson名字作为BSON容器别名，拒绝碰撞及保留字段。complex collection仍是provider未实现，模型校验明确失败，不能写成LiteDB无法存BSON数组。
- LDB-007：公开 HasLiteDBDefaultExpression / HasLiteDBComputedExpression，实际LiteDB表达式、EF sidecar回写、失败撤销与同context重试、Never/显式默认值优先、Bulk计算值重算。它们不是LiteDB数据库触发器；外部直接写文件不会自动重算。SQL及rowversion仍明确拒绝。
- LDB-008：设计服务和type mapping codegen已接通，compiled model生成、编译、UseModel同步/异步CRUD、重开、ModelBuildCount=0均PASS。预生成查询仍FAIL（Constant(LiteDBQueryPlan)无法生成代码）；NativeAOT publish实际exit1、保留IL告警并报告Cross-OS compilation错误，原生程序运行NOT_RUN。见 docs/compiled-model.md；不把环境失败归因为LiteDB。
- LDB-009：LiteDBDataArchive显式集合导出、版本化BSON JSONL、映射导入、数量/版本/限额/尾记录校验、原子事务、用户Tracker/事务保护。旧6826e3a布局fixture迁移通过；未运行旧EF6程序集，不承诺自动原地升级或二进制兼容。
- LDB-001/002/003：保留真实关系API/ambient/savepoint失败证据，新增nested transaction及未加载FK/cascade行为回归；替代方案仍为应用升级、单context本地事务、EF客户端级联。

完整 Windows Release 首轮与最终门禁均为 **133/133 PASS、0 skipped，0 build warnings/errors**；最终门禁使用Rebuild、ContinuousIntegrationBuild=true，证据 `artifacts/tests/ef10-followup-final.trx` 和 `artifacts/validation/ef10-followup-final-gates.json`。隔离包消费者已验证异步CRUD/Bulk/optional complex/生成值/回滚/重开，读取DLL与全新缓存DLL哈希相等，缓存nupkg与最终nupkg哈希相等，nuspec运行时依赖只有LiteDB与EF Core。完整结果在 `artifacts/validation/ef10-followup-final-receipt.json`。Compiled probe源码在validation/CompiledModelProbe，生成文件明确不入库；PackageConsumer无project reference，必须使用新的专属NUGET_PACKAGES目录。

功能源码已提交推送为 `83888aa0b676014113ebef37a8c2801083c8abef`，本地HEAD/origin/master/远端master回读一致；该SHA远端Ubuntu CI `42076065611` PASS，restore/build/test/pack步骤成功，publish按分支规则skipped。随后仅同步发布基线边界与验收说明，最终Git状态以本轮回读/receipt为准。

## 同工作区其他会话与远端边界

开工HEAD为305890e；期间其他会话在同一master提交并推送1ee4af3（tag发布流程/诊断修复）、d145613（Ubuntu单平台CI）和c3b7962（1.0.0发布及旧包废弃回执），它们已存在于当前提交祖先，未reset/覆盖。当前CI以实时.github/workflows/build.yml为准，Ubuntu restore/build/test/pack且只tag触发publish。本任务只推源码分支、不推tag、不发布NuGet；线上1.0.0为先前基线，不能当作本次功能已发布。新出现的`.playwright-cli/`属于其他会话，未stage/清理，后来被其所有者移除。

GitHub先前3条Dependabot告警独立回读均为fixed（旧LiteDB5.0.12、jQuery3.3.1），本轮`dotnet list package --vulnerable --include-transitive --no-restore`未报告漏洞。最终远端CI仍须绑定本次功能提交精确SHA回读，不能拿基线或其他会话CI替代。

各阶段遵守45分钟/15轮上限，外部命令180秒上限、单连接Cordonix串行调用、低并发构建。原生小探针已核验归属后清理；新消费者与probe运行临时数据库由finally回收。保留最终证据和包产物；上一阶段被策略拒绝的缓存仍原样保留，不绕过删除。所有worker均未单独提交或推送。

---

# 上一阶段历史交接（2026-11-05）

日期：2026-11-05，Asia/Shanghai。工作区：`D:\source\EntityFrameworkCore.LiteDB`。远端：`https://github.com/IoTSharp/EntityFrameworkCore.LiteDB`，当前分支及跟踪分支为 `master` / `origin/master`。本文件与第一次 EF Core 10 升级代码一起提交；精确提交与推送状态由交接主会话回读，新会话开始时重新核对 Git。

## 用户目标与已作决定

升级最新稳定 LiteDB、适配 EF Core 10，尽量通过 LiteDB 实现 EF provider 能力；真实引擎限制写清楚，provider 尚未实现的功能不能伪称 LiteDB 无法支持。项目、程序集和包统一为 `IoTSharp.EntityFrameworkCore.LiteDB`，旧包 `MaikeBing.EntityFrameworkCore.LiteDB` 退役，旧项目名 `MaikeBing.EntityFrameworkCore.NoSqLiteDB` 已移除。

用户明确要求：参考并改写其他 EF provider 的有意义测试；只学习参考 InMemory 实现，不能依赖 InMemory 包、实现 `IInMemory*`、继承或重载 InMemory provider；类型命名不得把 LiteDB/InMemory 混为一谈。

本次最新指令是：“现在提交并推送代码，然后创建新会话，在新会话中实现后续能力，补齐功能，把后续要完成的能力在新会话中使用多个智能体同时开工进行完成。”新会话应实际启动多个子智能体进行实现并完成集成，不停留在计划或口头宣布并行。

## 已实现与证据基线

- `net10.0`；EF Core `10.0.14`、LiteDB `5.0.21`；运行时只有 EF Core、LiteDB 两个直接包，禁止加回 InMemory 依赖。
- 独立服务、BSON 存储、主键/复合键、唯一索引、shadow 属性、转换器、常用标量精度、基元集合、导航、owned、多对多、继承、并发检查及 Reload。
- LiteDB 原生保存批次事务、显式提交/回滚、未提交 Dispose 回滚、跨 context 事务隔离保护、失败批次中止本地事务。
- 整数/decimal 筛选、主排序、分页和受支持来源上的 Count/LongCount/Any 有原生路径和诊断断言；其他已支持组合仍可能读取 LiteDB 行后在 provider 计算。
- HasData、UseSeeding/UseAsyncSeeding、常量默认值、显式 ValueGeneratedNever 优先级及 ASP.NET Core Identity 集成均有用例。
- 上一阶段在 Windows / SDK `10.0.403` 上完整 Release 构建为 0 警告、0 错误，65 测试全部通过，无跳过；独立 NuGet 消费程序验证过异步 CRUD、生成键、文件重开、事务回滚。当前提交前需复核实际 CI 门禁。
- 既有本地证据：`artifacts/validation/final-receipt.json`、`artifacts/validation/provider-source-manifest.json`、`artifacts/test-results/release.trx`；本地产物 `artifacts/packages/IoTSharp.EntityFrameworkCore.LiteDB.1.0.0.nupkg`，均忽略且不提交。不要把本地 PASS 当作远端 CI、Linux、Web 页面或 NuGet 线上验收。
- 官方参考来源和 MIT 归属在 `UPSTREAM.md`、`licenses/EFCore-MIT.txt`。具体能力边界以 `SUPPORT.md` 为准。

## 新会话立即启动的并行分工

主智能体先核对推送提交、读取本文件与 SUPPORT/UPSTREAM/README/CI，然后同时创建三个真实子智能体。第一轮建议如下；主智能体持有公共注册、共享接口、集成修改和最终验证。

| 子任务 | 实现及验收 | 文件归属建议 |
| --- | --- | --- |
| Bulk，LDB-004 | 接通 EF10 ExecuteUpdate/ExecuteDelete 同步/异步，使用 LiteDB 批量写入能力；验证受影响行数、参数、setter 计算、事务失败原子性，以及不通过 ChangeTracker 的语义 | 新增 `Query/Internal/Bulk/`、`Storage/Internal/Bulk/`、`Tests/Bulk*.cs`；公共 visitor/store 接口的最小接线改动交主智能体集成 |
| Complex，LDB-006 | 接通 ComplexProperty 与嵌套 BSON、属性路径、空值、值比较快照、投影、变更跟踪和重开持久化；随后检查 complex collection/ToJson 的准确边界 | 新增 complex 专属目录/测试；负责 EntityProjectionExpression、LiteDBQueryExpression 及 shaper 中 complex 所需改动；与 Bulk 共用的 translating visitor 只交最小接线方案 |
| Native queries，LDB-005 | 扩展保持语义一致的 LiteDB 查询下推，优先 ThenBy、字符串排序规则、日期/特殊类型和聚合；用执行计划/诊断证明原生执行，同时保持闭包、nullable、数值强转、NaN 语义 | `LiteDBQueryPlan.cs`、新增 `Query/Internal/Native/`、NativeQueryTests；Store/Table 的共有执行入口交主智能体修改 |

所有 worker 开工前声明准确文件清单，禁止同时覆盖共享文件或用整个文件覆盖其他 worker 改动。可并行分析/编码，但大型 restore/build/test 由主智能体协调串行，使用 `-m:1 -nr:false -p:UseSharedCompilation=false` 控制资源。worker 不单独提交或推送。

第一轮集成后，在空出的 worker 槽继续并行处理 LDB-007 的可实现生成值合同、LDB-008 的 compiled model/预生成查询/AOT 实证、LDB-009 的旧数据导出导入工具和回归。核查全部 LDB 编号；能实现的缺口应接线并测试，不得只删掉“不支持”文字或跳过测试。关系 SQL DDL、分布式事务、保存点等须根据 LiteDB 真实能力保留有依据的边界；真正无法支持的能力记录最小复现、引擎/API 原因和替代方案。

## 必须延续的执行与验收约束

每个子智能体的任务提示必须重复本段的循环边界、工具发现边界、进程归属和回收要求：每个工作阶段最多 45 分钟、最多 15 轮修复/验证，超出则由主智能体基于结果明确划定下一有界阶段；单条长命令设不超过 180 秒墙钟超时，搜索限定命名目录与至多 200 个文件、10 秒窗口，支持取消与定期进度。禁止无界循环/轮询、忙等待；执行生成循环前核对比较操作及退出条件并做极小试运行。

通用本地调用优先 Cordonix 的 typed program + args；它的单连接一次只接受一个操作，不要并发调用同一个 Cordonix 连接。需要 PowerShell 时只使用 `C:\Program Files\PowerShell\7\pwsh.exe` 和保存的 `-File` 脚本，不使用 powershell.exe、EncodedCommand 或嵌套远程 shell 字符串。工具发现仅使用 PATH、明确配置或已知固定路径，禁止盘根/home/Program Files 大范围扫描与未经授权安装工具。网络失败按机器规则逐请求改用 `http://127.0.0.1:7890`。

禁止 Graphify，包括加载技能、MCP、查询、生成 graphify-out。保留其他会话改动，禁止 reset/clean/stash-all/宽泛 stage。仅回收核验为本任务自建的完整进程树和临时对象；记录 PID、启动时间、完整命令行与父链，禁止按 pwsh/node/dotnet 等进程名批量终止。删除前验证绝对路径和归属；不绕过工具审查拒绝。

新增支持必须有真实 LiteDB 数据库、关闭后重开、同步/异步及相关失败语义测试；不要用 mock 或实现镜像测试冒充支持。保留并扩展官方 provider 场景测试，测试来源与许可证同步到 UPSTREAM。维持 PASS/FAIL/UNKNOWN/NOT_RUN，不压制告警伪造 AOT/引擎能力。

最终门禁按 `.github/workflows/build.yml`：

```text
dotnet restore IoTSharp.EntityFrameworkCore.LiteDB.sln
dotnet build IoTSharp.EntityFrameworkCore.LiteDB.sln -c Release --no-restore
dotnet test IoTSharp.EntityFrameworkCore.LiteDB.Tests -c Release --no-build --no-restore --logger trx --results-directory artifacts/tests
dotnet pack IoTSharp.EntityFrameworkCore.LiteDB -c Release --no-build --no-restore -o artifacts/packages
```

更新 SUPPORT、UPSTREAM、README 和本交接文件，并用最终包做隔离的 NuGet 消费验证；包缓存使用新的任务目录，避免相同版本号命中旧 DLL。检查原有 65 项用例回归；实现旧 unsupported 项后把对应断言改成真实成功/失败契约，不直接禁用用例。

## 发布与资源边界

NuGet 新包尚未发布，旧包在线 Deprecated 尚未设置，操作稿在 `docs/nuget-deprecation.md`。当前用户指令授权本次源代码提交、推送和新会话并行实现；不据此自动发布 NuGet、变更线上包或操作其他仓库。

上一阶段清理临时目录被工具策略以 `blocked by policy` 拒绝，目录仍保留；确切列表在本地 final-receipt.json，不属于待提交源码。不得换工具绕过审查来重复删除；它们也不是继续开发的必要基线，当前仓库源码和提交才是事实来源。
