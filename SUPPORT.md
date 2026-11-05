# LiteDB / EF Core 10 支持清单

核验日期：2026-11-05。目标框架 `net10.0`；验证版本为 **EF Core 10.0.14 / LiteDB 5.0.21**（最新稳定版，未使用 LiteDB 6 预发行包）。包依赖限制在 EF Core 10 与 LiteDB 5 的大版本范围内。

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

上表表示已有具体用例的能力，不等于整个 EF Core Specification Tests 全量通过。普通本地文件不需要手动管理连接；需要跨内部服务提供程序共享连接或显式回收时，可向 `UseLiteDB(path, root)` 传入 `LiteDBDatabaseRoot`，在所有相关 context 释放后 Dispose root。

## 查询在哪里执行

| 路径 | 当前行为 |
| --- | --- |
| LiteDB 原生查询 | 无值转换的整数/decimal 属性比较、可空数值比较、AND/OR、主排序、Skip/Take；可翻译来源上的 Count/LongCount/Any 调用 LiteDB 原生聚合 |
| provider 中的剩余查询 | 字符串/浮点/日期特殊语义、ThenBy、多集合 Join、Include、复杂投影、其他聚合等读取 LiteDB 行后由本 provider 的 EF 表达式管线计算；不承诺这些部分使用 LiteDB 索引 |
| 原生执行验证 | NativeQueryTests 订阅 `IoTSharp.EntityFrameworkCore.LiteDB.QueryExecuted` / `AggregateExecuted`；断言原生筛选分页仅物化指定行，聚合调用实际发生 |

字符串比较、数值强转和 NaN 的语义可能与 BSON 不同，无法保持一致的表达式保留在 provider 计算路径。分页组合、继承、转换器等超出原生子集时不会冒充原生翻译；大集合上的剩余计算目前可能需要较多内存。

## 暂不支持清单

以下编号可直接用于 Issue。明确区分 LiteDB 引擎限制与 provider 尚未实现，不能把后者称为“LiteDB 做不到”。

| 编号 | 能力与状态 | 原因、替代方案 |
| --- | --- | --- |
| LDB-001 | EF relational migrations、关系 DDL、FromSql/ExecuteSql、存储过程、关系数据库反向工程：**不支持关系语义** | LiteDB 无关系表/列 DDL、关系 SQL 连接。使用 EnsureCreated、应用数据升级及 LiteDB 自身 API。不会生成虚假的迁移 SQL |
| LDB-002 | System.Transactions/分布式事务、保存点、嵌套事务、跨线程延续同一个本地事务：**引擎限制** | LiteDB 本地事务绑定线程，不能提供分布式协调/保存点。使用单 context、同线程本地事务；Async API 包装 LiteDB 同步操作，不能据此推导异步磁盘 I/O 或跨 await 线程迁移的事务保证 |
| LDB-003 | 数据库级外键/级联删除：**无引擎约束** | EF 可以处理已跟踪实体的关系修复和客户端级联；LiteDB 不建立跨集合 FK 约束，不会自动删除未加载的关联文档 |
| LDB-004 | EF10 ExecuteUpdate/ExecuteDelete：**provider 暂未实现** | LiteDB 有 UpdateMany/DeleteMany，尚未接到 EF 批量写入表达式及 affected-row 契约。当前调用会失败且有测试；临时使用查询后 SaveChanges。后续应实现独立 bulk 翻译并测试不经过 ChangeTracker 的语义 |
| LDB-005 | 全量查询下推、字符串排序规则/特殊类型的原生翻译：**provider 部分实现** | 当前只有上述参数化原生子集；其他已支持组合在 provider 计算。需要继续逐运算符验证 BSON 与 CLR 语义及索引计划 |
| LDB-006 | EF10 ComplexProperty、complex collection、ToJson 映射与复杂类型批量更新：**provider 暂未实现** | LiteDB 支持嵌套 BSON；provider 的 complex 元数据/投影/物化管线尚未接通。模型验证立即报告 LDB-006。可用 owned type 或显式 ValueConverter；不得把它归因于 LiteDB 无法嵌套文档 |
| LDB-007 | SQL 计算列、SQL 默认值表达式、数据库自动 rowversion：**未提供等价契约** | SQL 表达式不可直接当 LiteDB 表达式。模型验证拒绝这些配置。应用赋值、应用维护并发令牌可用；精确服务端生成表达式需要另行实现与验证 |
| LDB-008 | 预生成/NativeAOT 查询、compiled model 代码生成与完整 EF provider 规范套件：**未验证/未接通** | 常规 EF.CompileQuery 已验证；不据此声称 AOT 或 compiled model 支持。EF 内部扩展点升级需要重跑测试 |
| LDB-009 | 旧 provider 数据文件自动迁移与旧程序集二进制兼容：**不承诺** | 新版本改变命名空间、主键编码、文档字段及集合映射。旧包数据须先备份，再使用旧版本导出与新模型导入；不可直接当作就地升级兼容 |

已测试的“不支持”是可预期失败，不能通过忽略警告或空事务实现假装成功。其余未列出的特殊 API、空间类型及异常集合类型保持未验证；新增支持须同时补入可重开的持久化用例。

## 旧包退役

本仓库已将 `MaikeBing.EntityFrameworkCore.LiteDB` 定为废弃包，替代项为 `IoTSharp.EntityFrameworkCore.LiteDB`，原项目名 `MaikeBing.EntityFrameworkCore.NoSqLiteDB` 已移除。

NuGet.org 上历史版本的 Deprecated 标志是独立的账号操作，不能通过改 `.csproj` 自动生效。新包应先发布并公开回读，然后由包所有者对旧包的全部版本设置 Legacy 和替代包；待执行的确切内容见 [docs/nuget-deprecation.md](docs/nuget-deprecation.md)。当前代码交付不表示已完成在线发布或在线废弃标记。
