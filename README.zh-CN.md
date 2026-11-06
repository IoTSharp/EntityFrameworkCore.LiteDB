# IoTSharp.EntityFrameworkCore.LiteDB

[English](README.md) | [简体中文](README.zh-CN.md)

`IoTSharp.EntityFrameworkCore.LiteDB` 是面向 .NET 10 的 Entity Framework Core 10 提供程序。它让应用继续使用 `DbContext`、实体类和 LINQ，把数据保存到 LiteDB 文件和集合中。

它适合桌面工具、离线优先应用、小型服务，以及希望使用本地数据文件、但不想放弃 EF Core 编程模型的 .NET 项目。项目实现了自己的服务、存储接口、查询节点、BSON 映射和事务处理；它不依赖 `Microsoft.EntityFrameworkCore.InMemory`，也不实现或继承 InMemory provider 的接口和类型。

当前包目标为 `net10.0`，核验组合为 Microsoft.EntityFrameworkCore `10.0.14` 与 LiteDB `5.0.21`。运行时依赖只包含 EF Core 和 LiteDB。

## 安装

```bash
dotnet add package IoTSharp.EntityFrameworkCore.LiteDB --version 1.1.0
```

也可以使用 Package Manager Console：

```powershell
Install-Package IoTSharp.EntityFrameworkCore.LiteDB -Version 1.1.0
```

在 `DbContext` 中配置 LiteDB 文件路径或连接字符串：

```csharp
using IoTSharp.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

public sealed class AppContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseLiteDB("app.db");
}
```

数据会写入 `app.db`。程序关闭后，重新使用同一路径创建上下文即可继续读取。

## 1.1.0 已发布能力

### EF Core 日常使用

- 独立的 provider 服务、真实 LiteDB 文件持久化、同步和异步 CRUD、变更跟踪与关闭后重开读取。
- 常用标量类型、主键和复合主键、shadow property、值转换器、基元集合，以及引用、集合、Include、owned type、多对多和继承等关系装配。
- 乐观并发检查、唯一索引、`EnsureCreated`、`HasData`、`UseSeeding` / `UseAsyncSeeding` 和 ASP.NET Core Identity 集成。
- LiteDB 本地事务、显式提交和回滚；保存批次失败时不会留下前半批数据。

### EF Core 10 批量操作

`ExecuteUpdate` 和 `ExecuteDelete` 已支持同步和异步调用，适用于当前支持的单集合查询目标。更新和删除使用 LiteDB 的批量写入能力，在同一事务中完成，返回受影响行数；失败时整批回滚。

当前支持标量、shadow property、外键和复杂对象叶子字段。主键、计算值、完整复杂对象赋值、owned/继承目标和 Join 重塑目标会明确拒绝，而不是悄悄改变语义。

### 复杂对象与 BSON

必填、可选和嵌套的 `ComplexProperty` 会保存为嵌套 BSON。实现区分 `null` 与“对象存在但成员为默认值”的情况，支持叶子查询、完整投影、变更跟踪、关闭重开和复杂叶子字段的批量更新。

`ToJson` 在这里是 BSON 容器名称的别名，不等同于关系数据库的 JSON 列功能。

### 默认值和计算值

可以使用 LiteDB 表达式定义默认值和计算值：

```csharp
modelBuilder.Entity<Line>().Property(x => x.CreatedLabel)
    .HasLiteDBDefaultExpression("'new'");
modelBuilder.Entity<Line>().Property(x => x.Total)
    .HasLiteDBComputedExpression("$.Price * $.Quantity");
```

生成表达式由 LiteDB 执行，保存后的值会回写到受跟踪实体，批量更新也会重新计算相关字段。表达式读取普通持久化字段；生成值依赖另一个生成值、主键生成表达式和 SQL 字符串表达式会被拒绝。

### 查询、编译模型与旧数据迁移

一部分数值与日期时间筛选、混合方向 `ThenBy`、分页、`Count`、`LongCount`、`Any`、`decimal` 的 `Sum` / `Average` 以及数值 `Min` / `Max` 会交给 LiteDB 执行。字符串文化规则、`DateTimeOffset`、浮点数/NaN、数值强制转换、复杂路径和复杂投影等需要保持 EF Core 语义的组合，仍由 provider 处理；这类路径不承诺使用 LiteDB 索引。

编译模型生成、生成源码编译，以及 `UseModel` 的同步/异步 CRUD 和重开已经验证。项目还提供 `LiteDBDataArchive` 的显式集合导出、版本化 BSON JSONL、映射导入和事务校验，用于有意识地迁移旧数据布局。

## 当前边界

以下能力没有被写成“已支持”：

- **复杂对象集合**：当前是 provider 缺口，模型校验会明确失败。LiteDB 本身可以存嵌套数组，但本提供程序尚未完成相应的 EF Core 合同。
- **预生成查询代码**：当前查询计划中仍有不能生成源码的运行时对象，尚未完成预生成查询支持。
- **NativeAOT 运行时**：尚未取得可运行的 NativeAOT 程序验证；请不要把普通 JIT 运行结果当成 AOT 兼容证明。
- **关系数据库专属能力**：关系迁移 SQL、`FromSql` / `ExecuteSql`、存储过程、分布式事务、保存点和数据库级外键约束不属于 LiteDB 的支持范围。
- **旧文件自动升级**：项目提供显式导出和映射导入，不承诺自动原地升级或二进制兼容。

详细支持范围、引擎限制和替代方案请查看 [SUPPORT.md](SUPPORT.md)。

## 迁移旧包

旧包 `MaikeBing.EntityFrameworkCore.LiteDB` 已在 NuGet.org 标记为由 `IoTSharp.EntityFrameworkCore.LiteDB` 替代。升级时需要：

1. 将 `using MaikeBing.EntityFrameworkCore;` 改为 `using IoTSharp.EntityFrameworkCore;`。
2. 保留既有的 `UseLiteDB(...)` 调用，并重新编译消费者项目。
3. 先备份历史数据；如需处理旧布局，使用显式导出和映射导入流程，不能假定新 provider 可以直接打开旧文件。

## 验证与发布

1.1.0 发布版本通过了 133 个 provider 测试，未跳过用例。公开 NuGet 的全新缓存消费程序还验证了异步读写、批量操作、可选复杂对象叶子更新、生成值、关闭重开和事务回滚。

项目采用 tag 驱动的 NuGet 发布：分支和 PR 构建只验证、打包；符合 `vMAJOR.MINOR.PATCH` 格式的 tag 在测试通过后发布经过同一 CI 验证的包。

## 项目地址

- 项目源码（Gitee）：https://gitee.com/IoTSharp/EntityFrameworkCore.LiteDB
- 项目源码与 Release（GitHub）：https://github.com/IoTSharp/EntityFrameworkCore.LiteDB
- GitHub Release `v1.1.0`：https://github.com/IoTSharp/EntityFrameworkCore.LiteDB/releases/tag/v1.1.0
- NuGet 包：https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/1.1.0

## 许可证

本项目采用 [MIT License](LICENSE)。
