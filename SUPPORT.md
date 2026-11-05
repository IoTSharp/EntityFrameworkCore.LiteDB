# LiteDB / EF Core 10 支持清单

核验日期：2026-11-06，Asia/Shanghai。目标框架 `net10.0`；验证版本为 **EF Core 10.0.14 / LiteDB 5.0.21**（未使用 LiteDB 6 预发行包）。运行时包依赖限制在 EF Core 10 与 LiteDB 5 的大版本范围内；EF Design 是不传播给消费者的私有设计依赖。

本 provider 有自己的 `ILiteDBStore`、`LiteDBStoreProvider`、`LiteDBStore`、`LiteDBTable`、查询表达式和服务注册。**不依赖 InMemory 包，不实现 `IInMemory*`，不继承 InMemory provider 类型。** 官方实现和测试的参考来源、许可证及改写范围见 [UPSTREAM.md](UPSTREAM.md)。

## 已实现并有回归测试

| 能力 | 对接方式及证据 |
| --- | --- |
| EF 服务、模型、同步/异步 CRUD、变更跟踪 | 独立 provider 服务；真实 LiteDB 文件和关闭连接后的重开读取 |
| 整数、GUID、字符串主键 | 整数生成器读取已保存最大值；显式高值推进生成器；主键规范化编码 |
| 复合主键 | BSON 类型信息编码，避免分隔符拼接和字符串大小写造成碰撞 |
| 字段、shadow property、外键值、值转换器 | 按 EF 属性元数据保存/物化 BSON；不序列化导航对象图 |
| 常用标量 | 有符号/无符号整数、浮点数、decimal、char、string/null/空串、byte[]、GUID、枚举、可空值、DateTime、DateTimeOffset、DateOnly、TimeOnly、TimeSpan；日期使用保留 ticks/Kind/offset 的编码 |
| 基元集合 | 列表/数组 BSON 序列化与值比较快照；int 列表、string 数组的查询及原地变更有测试 |
| 导航与关系映射 | 引用、集合、Include、owned type、多对多、shadow 外键、继承和 OfType；由 EF 做物化、跟踪和关系修复 |
| 查询组合 | 闭包参数与缓存、筛选、投影、排序/分页、Contains、字符串函数、Join、GroupBy 聚合、Union、Count、Sum、Average、编译查询和同步/异步枚举 |
| 乐观并发 | Update/Delete 比对应用维护的 concurrency token，返回带 Entries 的 DbUpdateConcurrencyException；支持 Reload |
| 唯一性 | LiteDB `_id` 索引、EF 唯一索引与 alternate key 的唯一编码索引；回归测试覆盖主键/唯一索引失败 |
| SaveChanges 原子性 | LiteDB BeginTrans/Commit/Rollback；批次失败回滚，不保留前半批记录 |
| 显式本地事务 | BeginTransaction、Commit、Rollback、未提交 Dispose 回滚；跨 context 不会意外加入；失败批次会中止整个显式事务，禁止之后提交 |
| 数据库生命周期与默认值 | EnsureCreated、HasData、UseSeeding/UseAsyncSeeding、常量 HasDefaultValue、CanConnect、EnsureDeleted；创建标记和 seed 重开验证；删除清空该文件内集合，保留空数据库文件 |
| ASP.NET Core Identity | 实际 UserManager/RoleManager 用户密码、角色、声明、登录、令牌及角色查询集成测试 |
| ExecuteUpdate / ExecuteDelete | 同步/异步；事务内快照选择行，LiteDB DeleteMany/参数化 UpdateMany，受影响行数、旧值 setter、分页、取消、唯一冲突与计算失败回滚；不会刷新或物化跟踪实体 |
| ComplexProperty | 必需/可选、嵌套 BSON、叶子查询及完整投影、原地/替换变更、关闭重开；存在标记区分 null 与全空/默认值对象；支持嵌套叶子批量更新 |
| LiteDB 生成值 | HasLiteDBDefaultExpression / HasLiteDBComputedExpression 使用真实 BSON 表达式；回写 EF store-generated 值，Never/显式默认值优先，失败回滚；Bulk 自动重算计算值 |
| Compiled model | 真实 dotnet ef optimize 设计服务、生成源码编译、UseModel 同步/异步 CRUD 与重开，OnModelCreating 计数为 0；不等于预生成查询或 AOT |
| 显式旧数据迁移 | 版本化 JSONL/BSON 导出与显式模型映射导入、数量/版本/限额校验、事务回滚；见 [迁移说明](docs/migration-archive.md) |

上表表示已有具体用例的能力，不等于整个 EF Core Specification Tests 全量通过。普通本地文件不需要手动管理连接；需要跨内部服务提供程序共享连接或显式回收时，可向 `UseLiteDB(path, root)` 传入 `LiteDBDatabaseRoot`，在所有相关 context 释放后 Dispose root。

## 查询在哪里执行

| 路径 | 当前行为 |
| --- | --- |
| LiteDB 原生查询 | 无转换的整数/decimal、DateTime ticks、TimeOnly/TimeSpan 比较；AND/OR、主排序、混合方向 ThenBy、Skip/Take；Count/LongCount/Any、decimal Sum/Average、整数/decimal Min/Max |
| provider 中的剩余查询 | 字符串文化规则、浮点/NaN、DateTimeOffset、数值强转、复杂属性、多集合 Join、Include、复杂投影、整数 Sum/Average 等保留 CLR 计算；不承诺这些部分使用 LiteDB 索引 |
| 原生执行验证 | NativeQueryTests / NativeExtendedQueryTests 按 ContextId 订阅 QueryExecuted / AggregateExecuted，验证实际原生路径、参数和读取行数；复合排序使用 BSON 数组字典序，不能据此承诺索引命中 |

字符串比较、数值强转和 NaN 的语义可能与 BSON 不同，无法保持一致的表达式保留在 provider 计算路径。分页组合、继承、转换器等超出原生子集时不会冒充原生翻译；大集合上的剩余计算目前可能需要较多内存。

## 能力编号与剩余边界

以下编号可直接用于 Issue。明确区分 LiteDB 引擎限制与 provider 尚未实现，不能把后者称为“LiteDB 做不到”。

| 编号 | 能力与状态 | 原因、替代方案 |
| --- | --- | --- |
| LDB-001 | EF relational migrations、关系 DDL、FromSql/ExecuteSql、存储过程、关系数据库反向工程：**不支持关系语义** | LiteDB 无关系表/列 DDL、关系 SQL 连接。使用 EnsureCreated、应用数据升级及 LiteDB 自身 API。不会生成虚假的迁移 SQL |
| LDB-002 | System.Transactions/分布式事务、保存点、嵌套事务、跨线程延续同一个本地事务：**引擎限制** | LiteDB 本地事务绑定线程，不能提供分布式协调/保存点。使用单 context、同线程本地事务；Async API 包装 LiteDB 同步操作，不能据此推导异步磁盘 I/O 或跨 await 线程迁移的事务保证 |
| LDB-003 | 数据库级外键/级联删除：**无引擎约束** | EF 可以处理已跟踪实体的关系修复和客户端级联；LiteDB 不建立跨集合 FK 约束，不会自动删除未加载的关联文档 |
| LDB-004 | EF10 ExecuteUpdate/ExecuteDelete：**已实现有界合同** | 单集合 filter/order/page 等来源；scalar/shadow/FK/complex 叶子可设，PK/computed/整 complex/owned/继承/Join 重塑目标明确拒绝。选行及 setter 保留 CLR ValueBuffer 语义，更新按文档调用 UpdateMany，全部同一事务；不宣称所有 setter 算术已下推 |
| LDB-005 | 全量查询下推：**部分实现** | 上述扩展子集有诊断与语义测试；字符串文化排序、DateTimeOffset、浮点、复杂路径等保留准确 fallback。默认 DateTimeOffset 查询比较遵循 UTC 时刻，不使用 change-tracking 的 EqualsExact 替换 CLR == |
| LDB-006 | ComplexProperty：**已实现**；complex collection：**provider 缺口** | 标量 complex、optional presence、nested leaf bulk 与 ToJson 容器别名映射为嵌套 BSON，ToJson 不提供关系 JSON 列/SQL 合同。complex collection 明确模型校验拒绝，替代为 owned collection 或显式 converter；LiteDB 本身支持嵌套数组，不能归因为引擎限制 |
| LDB-007 | LiteDB 原生默认/计算表达式：**已实现**；SQL/rowversion：**仍拒绝** | HasLiteDBDefaultExpression / HasLiteDBComputedExpression 只读普通持久化字段，拒绝生成值相互依赖、PK/FK 生成及内部字段访问。SQL 字符串不是 BSON 表达式；自动数据库 rowversion 尚无等价合同，使用应用并发 token |
| LDB-008 | Compiled model：**PASS**；预生成查询：**FAIL**；NativeAOT：**FAIL / 运行 NOT_RUN** | 标准设计服务及精确 mapping clone 已接通。预生成查询实报 Constant(LiteDBQueryPlan) 不能生成代码；AOT 发布有 IL 告警并因本机 Cross-OS native compilation 错误失败，未执行原生程序。详见 [实证](docs/compiled-model.md)，均不归因为 LiteDB 引擎限制；完整 EF specification suite 未全量接入 |
| LDB-009 | 显式导出/映射导入：**已实现**；自动原地升级/二进制兼容：**不承诺** | 归档 fixture 按旧提交 6826e3a 的 BsonMapper 布局构造并验证新模型导入；未运行旧 EF6 程序集。必须备份、显式选择集合与映射；不自动猜测历史实体结构 |

已测试的“不支持”是可预期失败，不能通过忽略警告或空事务实现假装成功。其余未列出的特殊 API、空间类型及异常集合类型保持未验证；新增支持须同时补入可重开的持久化用例。

## 旧包退役

本仓库已将 `MaikeBing.EntityFrameworkCore.LiteDB` 定为废弃包，替代项为 `IoTSharp.EntityFrameworkCore.LiteDB`，原项目名 `MaikeBing.EntityFrameworkCore.NoSqLiteDB` 已移除。

NuGet.org 的 Deprecated 标志是独立账号操作，不能通过 `.csproj` 生效。另一个会话已经提交首版 1.0.0 与旧包两个版本的 Legacy/替代包回执，见 [docs/nuget-deprecation.md](docs/nuget-deprecation.md)。该线上 1.0.0 属于先前基线，**不包含本次后续功能提交**；本次交付只提交/推送源代码并验证本地包，未再次发布 NuGet，也未更改线上旧包。
