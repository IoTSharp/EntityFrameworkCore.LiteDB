# NuGet.org 旧包废弃状态

状态：**BLOCKED_ALTERNATE_NOT_PUBLISHED / NOT_APPLIED**。2026-11-06（Asia/Shanghai）已使用所有者 `MysticBoy` 登录 NuGet.org 并检查实际管理页面；平台无法找到尚未发布的替代包，因此正式替代关系尚未保存。

- 历史包：[MaikeBing.EntityFrameworkCore.LiteDB](https://www.nuget.org/packages/MaikeBing.EntityFrameworkCore.LiteDB/)。2026-11-05 的 NuGet flat-container 回读显示版本 `0.0.14`、`1.0.0`。
- 替代包：`IoTSharp.EntityFrameworkCore.LiteDB`，已验证 tag `v1.0.1` 对应的本地包版本 `1.0.1`，目标 `net10.0`；首次公开发布尚未执行。
- 选择旧包全部版本，废弃原因选择 **Legacy / No longer maintained**；不要选择 Critical bugs 来代替一般退役。
- Alternate package ID：`IoTSharp.EntityFrameworkCore.LiteDB`；版本选择 **Latest**，首次上线后核对实际推荐版本。当前网页提供版本下拉框，不应把任意版本范围当成已保存的设置。
- 自定义说明：`This package is deprecated and no longer maintained. Use IoTSharp.EntityFrameworkCore.LiteDB for .NET 10 and EF Core 10. Recompile consumers and review SUPPORT.md; back up and export legacy data before importing into the new provider.`

先发布并公开核验新包的 ID、版本、依赖、README 和许可证，然后由旧包所有者在 NuGet.org 的 Manage package / Deprecate 中提交上述配置，最后独立回读旧包 catalog 的 deprecation/alternatePackage。修改本仓库元数据不会替 NuGet.org 设置该标志。

本轮实际页面检查：已选择旧包 `0.0.14`、`1.0.0` 全部版本，勾选 Legacy，填写上述替代包与自定义说明。页面校验明确返回：`Could not find alternate package 'IoTSharp.EntityFrameworkCore.LiteDB'.` 因校验未通过，未提交 Save。公开 flat-container 索引也返回 HTTP 404，不能把网页填表或本地元数据视为线上废弃成功。

本地证据：`artifacts/nuget-auth/deprecation-blocked.png` 与 `artifacts/nuget-auth/deprecation-receipt.json`。新包公开发布并核验后，应继续保存此正式替代关系，并独立回读两个旧版本的公开页面与 NuGet registration/catalog 中的 `deprecation.alternatePackage`。保留历史版本的原有 listing 状态，不覆盖旧版程序集、不变更包所有权。
