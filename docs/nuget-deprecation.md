# NuGet.org 旧包废弃操作稿

状态：**PREPARED / NOT_APPLIED**。这是可供包所有者执行的确切配置，不是在线操作回执。

- 历史包：[MaikeBing.EntityFrameworkCore.LiteDB](https://www.nuget.org/packages/MaikeBing.EntityFrameworkCore.LiteDB/)。2026-11-05 的 NuGet flat-container 回读显示版本 `0.0.14`、`1.0.0`。
- 替代包：`IoTSharp.EntityFrameworkCore.LiteDB`，本地产物版本 `1.0.0`，目标 `net10.0`。
- 选择旧包全部版本，废弃原因选择 **Legacy / No longer maintained**；不要选择 Critical bugs 来代替一般退役。
- Alternative package ID：`IoTSharp.EntityFrameworkCore.LiteDB`；推荐版本范围 `[1.0.0,2.0.0)`。
- 自定义说明：`This package is deprecated and no longer maintained. Use IoTSharp.EntityFrameworkCore.LiteDB for .NET 10 and EF Core 10. Recompile consumers and review SUPPORT.md; back up and export legacy data before importing into the new provider.`

先发布并公开核验新包的 ID、版本、依赖、README 和许可证，然后由旧包所有者在 NuGet.org 的 Manage package / Deprecate 中提交上述配置，最后独立回读旧包 catalog 的 deprecation/alternatePackage。修改本仓库元数据不会替 NuGet.org 设置该标志。

本轮未发布 NuGet 新包，也未修改线上旧包。2026-11-05 新包 ID 的公开 flat-container 索引回读为 404；需要包所有者登录及新包发布流程完成后再执行在线废弃操作。保留历史版本供旧应用重现构建，不应把旧版程序集用新内容覆盖发布。
