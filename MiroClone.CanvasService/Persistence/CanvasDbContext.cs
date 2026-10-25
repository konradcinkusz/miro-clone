using Microsoft.EntityFrameworkCore;

namespace MiroClone.CanvasService.Persistence;

/// <summary>
/// The canvas database. It has no entity model: the snapshot table is created with idempotent DDL by
/// <see cref="CanvasSnapshotStore"/> (ADR 0015). This context is the connection the store and the migration share.
/// </summary>
public sealed class CanvasDbContext(DbContextOptions<CanvasDbContext> options) : DbContext(options);
