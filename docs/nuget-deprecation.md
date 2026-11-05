# NuGet.org 旧包废弃状态

状态：**APPLIED / VERIFIED**。2026-11-06（Asia/Shanghai）已使用包所有者 `MysticBoy` 在 NuGet.org 的实际管理页面保存，并从匿名公开页面和 NuGet registration 数据独立回读。

- 历史包：[MaikeBing.EntityFrameworkCore.LiteDB](https://www.nuget.org/packages/MaikeBing.EntityFrameworkCore.LiteDB/)。`0.0.14`、`1.0.0` 两个历史版本均已标记为废弃。
- 原因：**Legacy / No longer maintained**；未标记 Critical bugs。
- 替代包：[IoTSharp.EntityFrameworkCore.LiteDB](https://www.nuget.org/packages/IoTSharp.EntityFrameworkCore.LiteDB/1.0.0)，首版 `1.0.0`，目标 `net10.0` / EF Core 10。
- 替代版本：管理页面选择 **Latest**，NuGet registration 中的 `deprecation.alternatePackage.range` 为 `*`。
- 自定义说明：`This package is deprecated and no longer maintained. Use IoTSharp.EntityFrameworkCore.LiteDB for .NET 10 and EF Core 10. Recompile consumers and review SUPPORT.md; back up and export legacy data before importing into the new provider.`

新包先通过 Ubuntu CI 发布，并使用全新缓存从 NuGet.org 还原验证。随后对旧包全部版本一次提交废弃设置。保存操作的本地等待曾超时，因此没有重复提交；之后两个版本的匿名公开页面与 registration 数据均确认保存成功。

公开页面显示 “This package has been deprecated as it is legacy and is no longer maintained.”，Suggested Alternatives 指向新包。registration 中两个版本均包含 `reasons: ["Legacy"]` 和 `alternatePackage.id: "IoTSharp.EntityFrameworkCore.LiteDB"`。

历史包原有 listing 状态保留：`0.0.14` 为 listed，`1.0.0` 原已 unlisted。本次仅修改废弃和推荐替代设置，未覆盖历史包内容或变更包所有权。

本地证据位于 `artifacts/release-1.0.0-evidence/`：`old-0.0.14.html`、`old-1.0.0.html`、`old-registration.json`、`old-package-replacement.png` 和 `final-receipt.json`。早期因替代包未发布而受阻的记录保留在 `artifacts/nuget-auth/`，属于首次发布前的历史状态。
